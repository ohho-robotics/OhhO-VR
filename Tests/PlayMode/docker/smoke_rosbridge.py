#!/usr/bin/env python3
"""Rosbridge websocket smoke test for the disposable broker in compose.yml.

Proves the broker accepts a websocket and echoes a publish on /ohho/ci_smoke.
Does not publish /cmd_vel/teleop or /emergency_stop. Run --self-test to check
the framing client without Docker.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import socket
import struct
import sys
import threading

GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"


class SmokeError(Exception):
    pass


def read_exact(sock: socket.socket, n: int, buf: bytearray) -> bytes:
    while len(buf) < n:
        chunk = sock.recv(max(4096, n - len(buf)))
        if not chunk:
            raise SmokeError("socket closed")
        buf.extend(chunk)
    out = bytes(buf[:n])
    del buf[:n]
    return out


def send_frame(sock: socket.socket, opcode: int, payload: bytes, masked: bool) -> None:
    header = bytearray([0x80 | (opcode & 0x0F)])
    length = len(payload)
    mask_bit = 0x80 if masked else 0x00
    if length < 126:
        header.append(mask_bit | length)
    elif length < 65536:
        header.append(mask_bit | 126)
        header.extend(struct.pack("!H", length))
    else:
        header.append(mask_bit | 127)
        header.extend(struct.pack("!Q", length))
    if masked:
        mask = os.urandom(4)
        header.extend(mask)
        payload = bytes(b ^ mask[i % 4] for i, b in enumerate(payload))
    sock.sendall(bytes(header) + payload)


def recv_frame(sock: socket.socket, buf: bytearray) -> tuple[int, bytes]:
    head = read_exact(sock, 2, buf)
    opcode = head[0] & 0x0F
    masked = (head[1] & 0x80) != 0
    length = head[1] & 0x7F
    if length == 126:
        length = struct.unpack("!H", read_exact(sock, 2, buf))[0]
    elif length == 127:
        length = struct.unpack("!Q", read_exact(sock, 8, buf))[0]
    if length > 2_000_000:
        raise SmokeError(f"frame too large: {length}")
    mask = read_exact(sock, 4, buf) if masked else b""
    payload = bytearray(read_exact(sock, length, buf))
    if masked:
        for i in range(len(payload)):
            payload[i] ^= mask[i % 4]
    return opcode, bytes(payload)


def parse_ws_url(url: str) -> tuple[str, int, str]:
    if not url.startswith("ws://"):
        raise SmokeError(f"only ws:// is supported, got {url}")
    rest = url[len("ws://"):]
    if "/" in rest:
        hostport, path = rest.split("/", 1)
        path = "/" + path
    else:
        hostport, path = rest, "/"
    if ":" in hostport:
        host, port_s = hostport.rsplit(":", 1)
        port = int(port_s)
    else:
        host, port = hostport, 80
    if not host:
        raise SmokeError(f"missing host in {url}")
    return host, port, path


def connect(url: str, timeout: float) -> tuple[socket.socket, bytearray]:
    host, port, path = parse_ws_url(url)
    sock = socket.create_connection((host, port), timeout)
    sock.settimeout(timeout)
    key = base64.b64encode(os.urandom(16)).decode("ascii")
    request = (
        f"GET {path} HTTP/1.1\r\n"
        f"Host: {host}:{port}\r\n"
        "Upgrade: websocket\r\n"
        "Connection: Upgrade\r\n"
        f"Sec-WebSocket-Key: {key}\r\n"
        "Sec-WebSocket-Version: 13\r\n"
        "\r\n"
    )
    sock.sendall(request.encode("ascii"))
    buf = bytearray()
    while b"\r\n\r\n" not in buf:
        chunk = sock.recv(4096)
        if not chunk:
            raise SmokeError("handshake closed")
        buf.extend(chunk)
    header, _, rest = bytes(buf).partition(b"\r\n\r\n")
    status = header.split(b"\r\n", 1)[0]
    if b" 101 " not in status:
        raise SmokeError("handshake status " + status.decode("ascii", "replace"))
    accept = ""
    for line in header.split(b"\r\n")[1:]:
        if line.lower().startswith(b"sec-websocket-accept:"):
            accept = line.split(b":", 1)[1].strip().decode("ascii")
    expected = base64.b64encode(hashlib.sha1((key + GUID).encode("ascii")).digest()).decode("ascii")
    if accept != expected:
        raise SmokeError(f"Sec-WebSocket-Accept {accept!r} != {expected!r}")
    return sock, bytearray(rest)


def collect_text(sock: socket.socket, buf: bytearray, timeout: float) -> list[str]:
    sock.settimeout(timeout)
    texts: list[str] = []
    partial = ""
    try:
        while True:
            opcode, payload = recv_frame(sock, buf)
            if opcode == 0x8:
                break
            if opcode == 0x9:
                send_frame(sock, 0xA, payload, masked=True)
                continue
            if opcode == 0x1:
                partial = payload.decode("utf-8", "replace")
                texts.append(partial)
            elif opcode == 0x0:
                partial += payload.decode("utf-8", "replace")
                texts.append(partial)
            if any("ping-ros1" in t or "ping-ros2" in t for t in texts):
                break
    except socket.timeout:
        pass
    return texts


def roundtrip(url: str, timeout: float) -> None:
    sock, buf = connect(url, timeout)
    try:
        def send(obj: dict) -> None:
            send_frame(sock, 0x1, json.dumps(obj).encode("utf-8"), masked=True)

        # Headset dialect (ROSBridgeClient advertises std_msgs/String) and the
        # ROS 2 form humble rosbridge registers. Either echo is a pass.
        send({"op": "advertise", "topic": "/ohho/ci_smoke", "type": "std_msgs/String"})
        send({"op": "subscribe", "topic": "/ohho/ci_smoke", "type": "std_msgs/String"})
        send({"op": "publish", "topic": "/ohho/ci_smoke", "msg": {"data": "ping-ros1"}})
        send({"op": "advertise", "topic": "/ohho/ci_smoke_ros2", "type": "std_msgs/msg/String"})
        send({"op": "subscribe", "topic": "/ohho/ci_smoke_ros2", "type": "std_msgs/msg/String"})
        send({"op": "publish", "topic": "/ohho/ci_smoke_ros2", "msg": {"data": "ping-ros2"}})

        texts = collect_text(sock, buf, timeout)
    finally:
        try:
            send_frame(sock, 0x8, struct.pack("!H", 1000), masked=True)
        except OSError:
            pass
        sock.close()

    blob = "\n".join(texts)
    ros1 = "ping-ros1" in blob
    ros2 = "ping-ros2" in blob
    print(f"headset dialect std_msgs/String echoed: {ros1}")
    print(f"ros2 dialect std_msgs/msg/String echoed: {ros2}")
    if not texts:
        raise SmokeError("broker accepted the websocket and then sent no text frames")
    if not (ros1 or ros2):
        tail = blob[-800:]
        raise SmokeError("broker did not echo /ohho/ci_smoke. frames:\n" + tail)
    if not ros1:
        print(
            "WARNING: humble echoed the ROS 2 type string and not std_msgs/String. "
            "ROSBridgeClient advertises ROS 1 type names (std_msgs/Bool, geometry_msgs/Twist). "
            "See docs/test-plan.md human TODOs before pointing the Unity client at this broker."
        )


def self_test() -> None:
    """Loopback websocket echo. Validates framing without a ROS image."""
    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    listener.bind(("127.0.0.1", 0))
    listener.listen(1)
    port = listener.getsockname()[1]
    errors: list[BaseException] = []

    def serve() -> None:
        try:
            conn, _ = listener.accept()
            conn.settimeout(5)
            data = bytearray()
            while b"\r\n\r\n" not in data:
                data.extend(conn.recv(4096))
            header, _, rest = bytes(data).partition(b"\r\n\r\n")
            key = ""
            for line in header.split(b"\r\n"):
                if line.lower().startswith(b"sec-websocket-key:"):
                    key = line.split(b":", 1)[1].strip().decode("ascii")
            accept = base64.b64encode(hashlib.sha1((key + GUID).encode("ascii")).digest()).decode("ascii")
            response = (
                "HTTP/1.1 101 Switching Protocols\r\n"
                "Upgrade: websocket\r\n"
                "Connection: Upgrade\r\n"
                f"Sec-WebSocket-Accept: {accept}\r\n"
                "\r\n"
            )
            conn.sendall(response.encode("ascii"))
            buf = bytearray(rest)
            opcode, payload = recv_frame(conn, buf)
            if opcode != 0x1:
                raise SmokeError(f"expected text frame, got opcode {opcode}")
            send_frame(conn, 0x1, payload, masked=False)
            conn.close()
        except BaseException as ex:  # noqa: BLE001 - report from the server thread
            errors.append(ex)
        finally:
            listener.close()

    thread = threading.Thread(target=serve, daemon=True)
    thread.start()
    sock, buf = connect(f"ws://127.0.0.1:{port}/", 5)
    send_frame(sock, 0x1, b"ping-ros1", masked=True)
    texts = collect_text(sock, buf, 5)
    sock.close()
    thread.join(timeout=5)
    if errors:
        raise errors[0]
    if not any("ping-ros1" in t for t in texts):
        raise SmokeError(f"self-test echo missing: {texts!r}")
    print("self-test ok")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="ws://127.0.0.1:9090")
    parser.add_argument("--timeout", type=float, default=15.0)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    try:
        if args.self_test:
            self_test()
        else:
            roundtrip(args.url, args.timeout)
            print(f"rosbridge smoke ok: {args.url}")
    except (SmokeError, OSError, TimeoutError) as ex:
        print(f"rosbridge smoke failed: {ex}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
