#!/usr/bin/env python3
"""Helpers for .github/workflows/release.yml.

The release keystore never lives in git. This module only parses tags,
checks player settings, and validates an APK that Unity already signed.
"""

from __future__ import annotations

import argparse
import base64
import io
import os
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

PACKAGE_ID = "com.ohhorobotics.ohhovr"
COMPANY_NAME = "ohho-robotics"
PRODUCT_NAME = "OhhO-VR"
KEYSTORE_NAME = "ohho-vr-release.keystore"
MIN_APK_BYTES = 5_000_000
PLAY_VERSION_CODE_LIMIT = 2_100_000_000
DEBUG_ALIAS = "androiddebugkey"
DEBUG_PASSWORD = "android"
DEBUG_CERT_MARK = "CN=Android Debug"

SECRET_NAMES = (
    "OHHO_VR_RELEASE_KEYSTORE_BASE64",
    "OHHO_VR_RELEASE_KEYSTORE_PASSWORD",
    "OHHO_VR_RELEASE_KEY_ALIAS",
    "OHHO_VR_RELEASE_KEY_PASSWORD",
    "UNITY_LICENSE",
    "UNITY_EMAIL",
    "UNITY_PASSWORD",
)

NOTE_PLACEHOLDERS = (
    "TAG",
    "BUNDLE_VERSION",
    "VERSION_CODE",
    "COMMIT",
    "SHA256",
    "APK_NAME",
    "CERT_SHA256",
)

# vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-rcN. rc.N is rejected so rc1 and rc.1
# cannot share a versionCode under two different tags.
TAG_RE = re.compile(
    r"^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-rc(0|[1-9]\d*))?$"
)

# major * 10_000_000 + minor * 100_000 + patch * 1_000 + channel
# channel is the rc number (1..998) or 999 for the final tag of that triple.
# Final stays above every rc of the same triple and below the next patch.
CHANNEL_FINAL = 999


class ReleaseError(Exception):
    pass


def version_from_tag(tag: str) -> tuple[str, int]:
    match = TAG_RE.fullmatch(tag)
    if not match:
        raise ReleaseError(
            f"Tag {tag!r} must match vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-rcN "
            "(example: v1.0.0-rc1). See docs/release.md."
        )
    major, minor, patch = (int(match.group(i)) for i in range(1, 4))
    rc_text = match.group(4)
    if major > 200 or minor > 99 or patch > 99:
        raise ReleaseError(
            "versionCode budget exceeded (major<=200, minor<=99, patch<=99). "
            "See docs/release.md."
        )
    if rc_text is None:
        channel = CHANNEL_FINAL
    else:
        channel = int(rc_text)
        if not 1 <= channel <= 998:
            raise ReleaseError(
                "rc number must be 1..998 so the final release of that version stays higher. "
                "See docs/release.md."
            )
    code = major * 10_000_000 + minor * 100_000 + patch * 1_000 + channel
    if not 1 <= code < PLAY_VERSION_CODE_LIMIT:
        raise ReleaseError(f"versionCode {code} is outside the Android allowed range")
    return tag[1:], code


def _newline(text: str) -> str:
    return "\r\n" if "\r\n" in text else "\n"


def _set_scalar(lines: list[str], key: str, value: str) -> None:
    prefix = f"  {key}:"
    found = [i for i, line in enumerate(lines) if line.startswith(prefix)]
    if len(found) != 1:
        raise ReleaseError(f"ProjectSettings has {len(found)} {key} entries; expected 1")
    lines[found[0]] = f"  {key}: {value}"


def _set_platform(lines: list[str], key: str, platform: str, value: str) -> None:
    prefix = f"  {key}:"
    found = [i for i, line in enumerate(lines) if line.startswith(prefix)]
    if len(found) != 1:
        raise ReleaseError(f"ProjectSettings has {len(found)} {key} entries; expected 1")
    index = found[0]
    child = f"    {platform}: {value}"
    header = lines[index].strip()
    if header == f"{key}: {{}}":
        lines[index] = f"  {key}:"
        lines.insert(index + 1, child)
        return

    if header != f"{key}:":
        raise ReleaseError(f"Unexpected ProjectSettings line for {key}: {lines[index]!r}")

    cursor = index + 1
    while cursor < len(lines):
        line = lines[cursor]
        if line.strip() == "":
            cursor += 1
            continue
        indent = len(line) - len(line.lstrip(" "))
        if indent <= 2:
            break
        if line.startswith(f"    {platform}:"):
            lines[cursor] = child
            return
        cursor += 1
    lines.insert(index + 1, child)


def patch_player_settings(text: str) -> str:
    """Force the Quest release identity on a ProjectSettings.asset string.

    The committed asset on main leaves scriptingBackend and applicationIdentifier
    empty. The release job applies this on the runner and does not commit it.
    """
    newline = _newline(text)
    lines = text.splitlines()
    _set_scalar(lines, "companyName", COMPANY_NAME)
    _set_scalar(lines, "productName", PRODUCT_NAME)
    _set_scalar(lines, "overrideDefaultApplicationIdentifier", "1")
    _set_scalar(lines, "AndroidTargetArchitectures", "2")
    _set_platform(lines, "applicationIdentifier", "Android", PACKAGE_ID)
    _set_platform(lines, "scriptingBackend", "Android", "1")
    return newline.join(lines) + newline


def _platform_value(text: str, key: str, platform: str) -> str | None:
    lines = text.splitlines()
    prefix = f"  {key}:"
    indexes = [i for i, line in enumerate(lines) if line.startswith(prefix)]
    if len(indexes) != 1:
        return None
    for line in lines[indexes[0] + 1 :]:
        if line.strip() == "":
            continue
        indent = len(line) - len(line.lstrip(" "))
        if indent <= 2:
            break
        needle = f"    {platform}:"
        if line.startswith(needle):
            return line.split(":", 1)[1].strip()
    return None


def assert_release_player_settings(text: str) -> None:
    if f"  companyName: {COMPANY_NAME}" not in text.splitlines():
        raise ReleaseError("companyName was not set for the release")
    if f"  productName: {PRODUCT_NAME}" not in text.splitlines():
        raise ReleaseError("productName was not set for the release")
    if "  overrideDefaultApplicationIdentifier: 1" not in text.splitlines():
        raise ReleaseError("overrideDefaultApplicationIdentifier was not enabled")
    if "  AndroidTargetArchitectures: 2" not in text.splitlines():
        raise ReleaseError("AndroidTargetArchitectures is not ARM64 (2)")
    if _platform_value(text, "applicationIdentifier", "Android") != PACKAGE_ID:
        raise ReleaseError(f"Android application id is not {PACKAGE_ID}")
    if _platform_value(text, "scriptingBackend", "Android") != "1":
        raise ReleaseError("Android scripting backend is not IL2CPP (1)")


def verify_apk(path: Path, bundle_version: str, min_bytes: int = MIN_APK_BYTES) -> int:
    data = path.read_bytes()
    size = len(data)
    if size < min_bytes:
        raise ReleaseError(
            f"APK {path.name} is {size} bytes; expected at least {min_bytes} "
            "(an IL2CPP Quest player is tens of megabytes)"
        )
    if b"APK Sig Block 42" not in data:
        raise ReleaseError(
            "APK has no v2/v3 signing block. Refusing to publish an unsigned release."
        )
    if bundle_version.encode("utf-8") not in data:
        raise ReleaseError(
            f"APK does not contain bundleVersion {bundle_version!r}. "
            "The tag did not make it into the player."
        )
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        names = [item.replace("\\", "/") for item in archive.namelist()]
    il2cpp = any(
        name == "lib/arm64-v8a/libil2cpp.so" or name.endswith("/lib/arm64-v8a/libil2cpp.so")
        for name in names
    )
    if not il2cpp:
        raise ReleaseError(
            "APK is missing lib/arm64-v8a/libil2cpp.so. This is not an ARM64 IL2CPP build."
        )
    if any(name.startswith("lib/armeabi-v7a/") for name in names):
        raise ReleaseError("APK contains ARMv7 libraries. The release target is ARM64 only.")
    return size


def render_notes(template: str, values: dict[str, str]) -> str:
    missing = [name for name in NOTE_PLACEHOLDERS if name not in values]
    if missing:
        raise ReleaseError(f"release notes missing values for {', '.join(missing)}")
    rendered = template
    for name in NOTE_PLACEHOLDERS:
        token = "{{" + name + "}}"
        if token not in template:
            raise ReleaseError(f"release notes template is missing {token}")
        rendered = rendered.replace(token, values[name])
    leftover = re.findall(r"\{\{[A-Z0-9_]+\}\}", rendered)
    if leftover:
        raise ReleaseError(f"release notes still contain placeholders: {', '.join(leftover)}")
    for heading in ("## What works", "## Known limits", "## Safety notice"):
        if heading not in rendered:
            raise ReleaseError(f"release notes are missing {heading}")
    return rendered


def _decode_keystore(raw_b64: str) -> bytes:
    compact = re.sub(r"\s+", "", raw_b64)
    if not compact:
        raise ReleaseError("OHHO_VR_RELEASE_KEYSTORE_BASE64 is empty. See docs/release.md.")
    compact += "=" * (-len(compact) % 4)
    try:
        data = base64.b64decode(compact, validate=True)
    except Exception as exc:
        raise ReleaseError(
            "OHHO_VR_RELEASE_KEYSTORE_BASE64 is not valid base64. "
            "Encode the keystore as one line. See docs/release.md."
        ) from exc
    if len(data) < 32:
        raise ReleaseError("decoded release keystore is too small to be a keystore")
    if data[:4] != b"\xfe\xed\xfe\xed" and data[:1] != b"\x30":
        raise ReleaseError(
            "decoded release keystore is neither JKS (magic FEEDFEED) nor PKCS12. "
            "See docs/release.md."
        )
    return data


def _reject_debug_material(alias: str, store_password: str, key_password: str) -> None:
    if alias == DEBUG_ALIAS:
        raise ReleaseError(
            "OHHO_VR_RELEASE_KEY_ALIAS is the CI debug alias androiddebugkey. "
            "Create a release keystore offline. See docs/release.md."
        )
    if store_password == DEBUG_PASSWORD or key_password == DEBUG_PASSWORD:
        raise ReleaseError(
            "A release password is the public Android debug password 'android'. "
            "Choose a unique password. See docs/release.md."
        )


def _redact(text: str, secrets: list[str]) -> str:
    redacted = text
    for secret in secrets:
        if secret:
            redacted = redacted.replace(secret, "***")
    return redacted


def check_keystore_material() -> str:
    """Decode the release keystore from the environment and return its SHA-256 fingerprint.

    Writes only under RUNNER_TEMP (never the git workspace) and deletes the file
    before returning. Does not print passwords or keystore bytes.
    """
    missing = [name for name in SECRET_NAMES[:4] if not os.environ.get(name, "").strip()]
    if missing:
        raise ReleaseError(
            "Missing GitHub Actions secrets: " + ", ".join(missing) + ". See docs/release.md."
        )
    alias = os.environ["OHHO_VR_RELEASE_KEY_ALIAS"].strip()
    store_password = os.environ["OHHO_VR_RELEASE_KEYSTORE_PASSWORD"]
    key_password = os.environ["OHHO_VR_RELEASE_KEY_PASSWORD"]
    _reject_debug_material(alias, store_password, key_password)
    data = _decode_keystore(os.environ["OHHO_VR_RELEASE_KEYSTORE_BASE64"])

    temp_root = os.environ.get("RUNNER_TEMP") or tempfile.gettempdir()
    temp_dir = Path(temp_root).resolve()
    workspace = Path.cwd().resolve()
    if workspace == temp_dir or workspace in temp_dir.parents:
        raise ReleaseError("refusing to write the release keystore inside the repository")

    keytool = shutil.which("keytool")
    if not keytool:
        raise ReleaseError("keytool is not on PATH. The release runner needs a JDK.")

    fd, raw_path = tempfile.mkstemp(prefix="ohho-vr-release-", suffix=".keystore", dir=temp_dir)
    path = Path(raw_path)
    try:
        os.close(fd)
        path.write_bytes(data)
        try:
            os.chmod(path, 0o600)
        except OSError:
            pass
        command = [
            keytool,
            "-list",
            "-v",
            "-keystore",
            str(path),
            "-storepass",
            store_password,
            "-alias",
            alias,
        ]
        try:
            completed = subprocess.run(command, check=True, capture_output=True, text=True)
        except subprocess.CalledProcessError as exc:
            detail = _redact((exc.stderr or "") + (exc.stdout or ""), [store_password, key_password])
            raise ReleaseError(
                "keytool rejected the release keystore (wrong password or unknown alias). "
                "See docs/release.md.\n" + detail.strip()
            ) from None
        output = completed.stdout
        if DEBUG_CERT_MARK in output:
            raise ReleaseError(
                "The release secret is the Android debug certificate (CN=Android Debug). "
                "Create a release keystore offline. See docs/release.md."
            )
        match = re.search(r"SHA256:\s*([0-9A-Fa-f:]+)", output)
        if not match:
            raise ReleaseError("keytool did not print a SHA256 certificate fingerprint")
        fingerprint = match.group(1).upper()
        print(f"Release keystore accepted ({len(data)} bytes). Certificate SHA-256 {fingerprint}.", file=sys.stderr)
        return fingerprint
    finally:
        try:
            path.unlink(missing_ok=True)
        except OSError:
            pass


def _github_kv(key: str, value: str) -> None:
    if "\n" in value or "\r" in value:
        raise ReleaseError(f"{key} contains a newline; refusing to write GITHUB_OUTPUT")
    print(f"{key}={value}")


def _read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


# Unity 6000.5 on the game-ci image aborts batchmode after licensing with
# "ADTM: Attempting to update control while a mix is ongoing, but realtime
# thread is disabled will lead to a deadlock". That image has no realtime
# audio thread. m_DisableAudio skips ADTM startup. The committed asset stays
# at 0 so local and headset builds keep Unity audio; CI flips it per run.
_AUDIO_ENABLED = "m_DisableAudio: 0"
_AUDIO_DISABLED = "m_DisableAudio: 1"


def disable_batchmode_audio(text: str) -> str:
    enabled = text.count(_AUDIO_ENABLED)
    disabled = text.count(_AUDIO_DISABLED)
    if enabled == 0 and disabled == 1:
        return text
    if enabled != 1 or disabled != 0:
        raise ReleaseError(
            "ProjectSettings/AudioManager.asset must contain exactly one "
            f"{_AUDIO_ENABLED!r} (found enabled={enabled}, disabled={disabled}). "
            "Headless Unity 6000.5 aborts unless that editor run disables audio."
        )
    return text.replace(_AUDIO_ENABLED, _AUDIO_DISABLED, 1)


def self_test() -> None:
    samples = [
        ("v1.0.0-rc1", "1.0.0-rc1", 10_000_001),
        ("v1.0.0-rc2", "1.0.0-rc2", 10_000_002),
        ("v1.0.0", "1.0.0", 10_000_999),
        ("v1.0.1-rc1", "1.0.1-rc1", 10_001_001),
        ("v1.0.1", "1.0.1", 10_001_999),
        ("v1.1.0-rc1", "1.1.0-rc1", 10_100_001),
        ("v1.1.0", "1.1.0", 10_100_999),
        ("v2.0.0-rc1", "2.0.0-rc1", 20_000_001),
        ("v2.0.0", "2.0.0", 20_000_999),
    ]
    codes = []
    for tag, bundle, code in samples:
        got_bundle, got_code = version_from_tag(tag)
        if got_bundle != bundle or got_code != code:
            raise ReleaseError(f"{tag} mapped to {got_bundle} {got_code}, expected {bundle} {code}")
        codes.append(got_code)
    if codes != sorted(codes) or len(codes) != len(set(codes)):
        raise ReleaseError(f"versionCodes are not strictly increasing: {codes}")

    rejected = ["1.0.0", "v1.0", "v1.0.0-rc", "v1.0.0-rc.1", "v1.0.0-beta1", "v01.0.0", "v1.0.0-rc0", "v1.100.0"]
    for tag in rejected:
        try:
            version_from_tag(tag)
        except ReleaseError:
            continue
        raise ReleaseError(f"{tag} should have been rejected")

    fixture = "\n".join(
        [
            "PlayerSettings:",
            "  companyName: DefaultCompany",
            "  productName: vr_app",
            "  applicationIdentifier: {}",
            "  overrideDefaultApplicationIdentifier: 0",
            "  AndroidTargetArchitectures: 1",
            "  scriptingBackend: {}",
            "",
        ]
    )
    patched = patch_player_settings(fixture)
    again = patch_player_settings(patched)
    if again != patched:
        raise ReleaseError("player settings patch is not idempotent")
    assert_release_player_settings(patched)

    settings_path = Path("ProjectSettings/ProjectSettings.asset")
    if settings_path.is_file():
        original = _read_text(settings_path)
        live = patch_player_settings(original)
        live_again = patch_player_settings(live)
        if live != live_again:
            raise ReleaseError("live ProjectSettings patch is not idempotent")
        assert_release_player_settings(live)
        if live == original and "scriptingBackend: {}" in original:
            raise ReleaseError("live ProjectSettings patch did not change an empty scriptingBackend")

    good = io.BytesIO()
    with zipfile.ZipFile(good, "w") as archive:
        archive.writestr("lib/arm64-v8a/libil2cpp.so", b"il2cpp")
        archive.writestr("assets/version.txt", b"1.0.0-rc1")
    good_bytes = good.getvalue() + b"APK Sig Block 42"
    with tempfile.TemporaryDirectory() as tmp:
        apk = Path(tmp) / "OhhO-VR-v1.0.0-rc1.apk"
        apk.write_bytes(good_bytes)
        verify_apk(apk, "1.0.0-rc1", min_bytes=1)
        bad = Path(tmp) / "unsigned.apk"
        bad.write_bytes(good.getvalue())
        try:
            verify_apk(bad, "1.0.0-rc1", min_bytes=1)
        except ReleaseError:
            pass
        else:
            raise ReleaseError("unsigned apk was accepted")
        v7 = io.BytesIO()
        with zipfile.ZipFile(v7, "w") as archive:
            archive.writestr("lib/arm64-v8a/libil2cpp.so", b"il2cpp")
            archive.writestr("lib/armeabi-v7a/libil2cpp.so", b"v7")
            archive.writestr("assets/version.txt", b"1.0.0-rc1")
        v7_path = Path(tmp) / "v7.apk"
        v7_path.write_bytes(v7.getvalue() + b"APK Sig Block 42")
        try:
            verify_apk(v7_path, "1.0.0-rc1", min_bytes=1)
        except ReleaseError:
            pass
        else:
            raise ReleaseError("ARMv7 apk was accepted")

    root = Path(__file__).resolve().parents[2]
    workflow = _read_text(root / ".github/workflows/release.yml")
    docs = _read_text(root / "docs/release.md")
    template = _read_text(root / "docs/release-notes-template.md")
    for name in SECRET_NAMES:
        if name not in workflow:
            raise ReleaseError(f"release.yml does not name secret {name}")
        if name not in docs:
            raise ReleaseError(f"docs/release.md does not name secret {name}")
    keystore_names = re.findall(r"androidKeystoreName:\s*(\S+)", workflow)
    if keystore_names != [KEYSTORE_NAME]:
        raise ReleaseError(f"release.yml keystore file name drifted: {keystore_names}")
    if f"rm -f {KEYSTORE_NAME}" not in workflow:
        raise ReleaseError("release.yml does not delete the workspace keystore")
    if "versioning: Custom" not in workflow:
        raise ReleaseError("release.yml must pass the tag as a Custom version")
    tests_workflow = _read_text(root / ".github/workflows/unity-tests.yml")
    for label, body in (("release.yml", workflow), ("unity-tests.yml", tests_workflow)):
        if "disable-batchmode-audio" not in body:
            raise ReleaseError(f"{label} does not disable Unity audio for headless batchmode")

    audio_path = root / "ProjectSettings/AudioManager.asset"
    committed_audio = _read_text(audio_path)
    if _AUDIO_ENABLED not in committed_audio or _AUDIO_DISABLED in committed_audio:
        raise ReleaseError("committed AudioManager.asset must keep Unity audio enabled")
    disabled_audio = disable_batchmode_audio(committed_audio)
    if disable_batchmode_audio(disabled_audio) != disabled_audio:
        raise ReleaseError("batchmode audio disable is not idempotent")
    if _AUDIO_ENABLED in disabled_audio or _AUDIO_DISABLED not in disabled_audio:
        raise ReleaseError("batchmode audio disable did not flip m_DisableAudio")
    with tempfile.TemporaryDirectory() as tmp:
        sample = Path(tmp) / "AudioManager.asset"
        sample.write_text("AudioManager:\n  m_DisableAudio: 0\n", encoding="utf-8", newline="\n")
        if main(["disable-batchmode-audio", "--project", str(sample)]) != 0:
            raise ReleaseError("disable-batchmode-audio command failed")
        written = sample.read_text(encoding="utf-8")
        if written != "AudioManager:\n  m_DisableAudio: 1\n":
            raise ReleaseError(f"disable-batchmode-audio wrote {written!r}")
        if main(["disable-batchmode-audio", "--project", str(sample)]) != 0:
            raise ReleaseError("disable-batchmode-audio was not idempotent on disk")
    rendered = render_notes(
        template,
        {
            "TAG": "v1.0.0-rc1",
            "BUNDLE_VERSION": "1.0.0-rc1",
            "VERSION_CODE": "10000001",
            "COMMIT": "abc",
            "SHA256": "deadbeef",
            "APK_NAME": "OhhO-VR-v1.0.0-rc1.apk",
            "CERT_SHA256": "AA:BB",
        },
    )
    if "deadbeef" not in rendered or "{{" in rendered:
        raise ReleaseError("release notes template did not render")
    print("release_support self-test ok")


def _cmd_version(args: argparse.Namespace) -> None:
    bundle, code = version_from_tag(args.tag)
    _github_kv("bundle_version", bundle)
    _github_kv("version_code", str(code))


def _cmd_disable_batchmode_audio(args: argparse.Namespace) -> None:
    path = Path(args.project)
    text = _read_text(path)
    patched = disable_batchmode_audio(text)
    if patched != text:
        path.write_bytes(patched.encode("utf-8"))
    print("headless editor: m_DisableAudio 1 (ADTM batchmode deadlock workaround)", file=sys.stderr)


def _cmd_patch(args: argparse.Namespace) -> None:
    path = Path(args.project)
    text = _read_text(path)
    patched = patch_player_settings(text)
    assert_release_player_settings(patched)
    if patched != text:
        path.write_bytes(patched.encode("utf-8"))
    print(
        f"player settings: {COMPANY_NAME} / {PRODUCT_NAME} / {PACKAGE_ID} / IL2CPP ARM64",
        file=sys.stderr,
    )


def _cmd_check_keystore(_args: argparse.Namespace) -> None:
    fingerprint = check_keystore_material()
    _github_kv("cert_sha256", fingerprint)


def _cmd_verify(args: argparse.Namespace) -> None:
    size = verify_apk(Path(args.apk), args.bundle_version, min_bytes=args.min_bytes)
    mib = size / (1024 * 1024)
    print(f"APK ok: {size} bytes ({mib:.1f} MiB), ARM64 IL2CPP, v2 signing block, bundleVersion present")


def _cmd_render(args: argparse.Namespace) -> None:
    values = {name: os.environ[name] for name in NOTE_PLACEHOLDERS}
    rendered = render_notes(_read_text(Path(args.template)), values)
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(rendered, encoding="utf-8", newline="\n")
    print(f"wrote {out}", file=sys.stderr)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="OhhO VR release helpers")
    sub = parser.add_subparsers(dest="command", required=True)

    self_test_parser = sub.add_parser("self-test")
    self_test_parser.set_defaults(func=lambda _args: self_test())

    version_parser = sub.add_parser("version")
    version_parser.add_argument("--tag", required=True)
    version_parser.set_defaults(func=_cmd_version)

    audio_parser = sub.add_parser("disable-batchmode-audio")
    audio_parser.add_argument("--project", default="ProjectSettings/AudioManager.asset")
    audio_parser.set_defaults(func=_cmd_disable_batchmode_audio)

    patch_parser = sub.add_parser("patch-settings")
    patch_parser.add_argument("--project", default="ProjectSettings/ProjectSettings.asset")
    patch_parser.set_defaults(func=_cmd_patch)

    keystore_parser = sub.add_parser("check-keystore")
    keystore_parser.set_defaults(func=_cmd_check_keystore)

    verify_parser = sub.add_parser("verify-apk")
    verify_parser.add_argument("--apk", required=True)
    verify_parser.add_argument("--bundle-version", required=True)
    verify_parser.add_argument("--min-bytes", type=int, default=MIN_APK_BYTES)
    verify_parser.set_defaults(func=_cmd_verify)

    render_parser = sub.add_parser("render-notes")
    render_parser.add_argument("--template", default="docs/release-notes-template.md")
    render_parser.add_argument("--out", required=True)
    render_parser.set_defaults(func=_cmd_render)
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        args.func(args)
    except ReleaseError as exc:
        print(f"::error::{exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
