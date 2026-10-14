"""Build a JPEG COM segment that carries an OhhO capture timestamp.

The headset searches frame bytes for the ASCII marker OHHO_CAP:<unix_ms>.
This module only builds those bytes. It does not open a camera.
"""

MARKER = "OHHO_CAP:"


def jpeg_com_segment(unix_ms):
    if unix_ms < 0:
        raise ValueError("unix_ms must be >= 0")
    payload = "{0}{1}".format(MARKER, int(unix_ms)).encode("ascii")
    if len(payload) > 65533:
        raise ValueError("payload too long for a JPEG COM segment")
    length = len(payload) + 2
    return b"\xff\xfe" + length.to_bytes(2, "big") + payload
