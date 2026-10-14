#!/usr/bin/env python3
"""Unit tests for scripts/bump-quest-version.py and the sideload release contract."""

from __future__ import annotations

import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts" / "bump-quest-version.py"

spec = importlib.util.spec_from_file_location("bump_quest_version", SCRIPT)
if spec is None or spec.loader is None:
    raise RuntimeError(f"cannot load {SCRIPT}")
bump = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = bump
spec.loader.exec_module(bump)

REQUIRED_SECRETS = (
    "UNITY_LICENSE",
    "UNITY_EMAIL",
    "UNITY_PASSWORD",
    "ANDROID_KEYSTORE_BASE64",
    "ANDROID_KEYSTORE_PASS",
    "ANDROID_KEYALIAS_NAME",
    "ANDROID_KEYALIAS_PASS",
)
PROHIBITED_PERMISSIONS = (
    "android.permission.RECORD_AUDIO",
    "android.permission.CAMERA",
    "android.permission.ACCESS_FINE_LOCATION",
    "android.permission.ACCESS_COARSE_LOCATION",
    "android.permission.WRITE_EXTERNAL_STORAGE",
    "android.permission.READ_EXTERNAL_STORAGE",
    "android.permission.READ_PHONE_STATE",
    "android.permission.QUERY_ALL_PACKAGES",
    "android.permission.SYSTEM_ALERT_WINDOW",
    "android.permission.REQUEST_INSTALL_PACKAGES",
)


FIXTURE = """  tvOSBundleVersion: 1.0
  bundleVersion: 1.0
  AndroidBundleVersionCode: 1
"""


def key_value(text: str, key: str) -> str:
    return bump.read_key(text.splitlines(keepends=True), key)


class BumpQuestVersionTests(unittest.TestCase):
    def test_patch_bumps_version_and_code_and_leaves_tvos(self):
        updated, before, after = bump.apply_bump(FIXTURE, part="patch")
        self.assertEqual(before.bundle_version, "1.0")
        self.assertEqual(before.bundle_code, 1)
        self.assertEqual(after.bundle_version, "1.0.1")
        self.assertEqual(after.bundle_code, 2)
        self.assertEqual(key_value(updated, "tvOSBundleVersion"), "1.0")
        self.assertEqual(key_value(updated, "bundleVersion"), "1.0.1")
        self.assertEqual(key_value(updated, "AndroidBundleVersionCode"), "2")

    def test_second_patch(self):
        once, _, _ = bump.apply_bump(FIXTURE, part="patch")
        twice, _, after = bump.apply_bump(once, part="patch")
        self.assertEqual(after.bundle_version, "1.0.2")
        self.assertEqual(after.bundle_code, 3)
        self.assertEqual(key_value(twice, "tvOSBundleVersion"), "1.0")

    def test_minor_and_major(self):
        _, _, minor = bump.apply_bump(FIXTURE, part="minor")
        self.assertEqual(minor.bundle_version, "1.1.0")
        text = "  bundleVersion: 1.2.3\n  AndroidBundleVersionCode: 4\n"
        _, _, major = bump.apply_bump(text, part="major")
        self.assertEqual(major.bundle_version, "2.0.0")
        self.assertEqual(major.bundle_code, 5)

    def test_set_version_and_explicit_code(self):
        _, _, after = bump.apply_bump(FIXTURE, set_version="2.3.4", bundle_code=10)
        self.assertEqual(after.bundle_version, "2.3.4")
        self.assertEqual(after.bundle_code, 10)

    def test_set_requires_three_components(self):
        with self.assertRaises(bump.VersionError):
            bump.apply_bump(FIXTURE, set_version="1.2")

    def test_bundle_code_must_increase(self):
        with self.assertRaises(bump.VersionError):
            bump.apply_bump(FIXTURE, part="patch", bundle_code=1)

    def test_keep_code_requires_a_version_change(self):
        _, _, after = bump.apply_bump(FIXTURE, part="patch", keep_code=True)
        self.assertEqual(after.bundle_code, 1)
        already = "  bundleVersion: 1.0.0\n  AndroidBundleVersionCode: 1\n"
        with self.assertRaises(bump.VersionError):
            bump.apply_bump(already, set_version="1.0.0", keep_code=True)

    def test_crlf_is_preserved(self):
        text = FIXTURE.replace("\n", "\r\n")
        updated, _, _ = bump.apply_bump(text, part="patch")
        self.assertTrue(all(line.endswith("\r\n") for line in updated.splitlines(keepends=True)))
        self.assertEqual(key_value(updated, "bundleVersion"), "1.0.1")

    def test_duplicate_or_missing_key_fails(self):
        with self.assertRaises(bump.VersionError):
            bump.apply_bump("  bundleVersion: 1.0\n  bundleVersion: 1.0\n  AndroidBundleVersionCode: 1\n")
        with self.assertRaises(bump.VersionError):
            bump.apply_bump("  bundleVersion: 1.0\n")

    def test_leading_zero_component_is_rejected(self):
        text = "  bundleVersion: 1.01\n  AndroidBundleVersionCode: 1\n"
        with self.assertRaises(bump.VersionError):
            bump.apply_bump(text, part="patch")

    def test_real_project_settings_round_trip_in_memory(self):
        path = ROOT / "ProjectSettings" / "ProjectSettings.asset"
        original = path.read_text(encoding="utf-8")
        current = bump.read_quest_version(original)
        self.assertGreaterEqual(current.bundle_code, 1)
        updated, before, after = bump.apply_bump(original, part="patch")
        self.assertEqual(before.bundle_version, current.bundle_version)
        self.assertEqual(after.bundle_code, current.bundle_code + 1)
        self.assertEqual(key_value(original, "tvOSBundleVersion"), key_value(updated, "tvOSBundleVersion"))
        self.assertEqual(path.read_text(encoding="utf-8"), original)

    def test_cli_dry_run_does_not_write(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "ProjectSettings.asset"
            path.write_text(FIXTURE, encoding="utf-8")
            before = path.read_bytes()
            proc = subprocess.run(
                [sys.executable, str(SCRIPT), "--project", str(path), "--patch", "--dry-run"],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(proc.returncode, 0, proc.stderr)
            self.assertIn("bundleVersion: 1.0 -> 1.0.1", proc.stdout)
            self.assertIn("tag: v1.0.1", proc.stdout)
            self.assertEqual(path.read_bytes(), before)

    def test_cli_patch_writes_the_new_version(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "ProjectSettings.asset"
            path.write_bytes(FIXTURE.encode("utf-8"))
            proc = subprocess.run(
                [sys.executable, str(SCRIPT), "--project", str(path), "--patch"],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(proc.returncode, 0, proc.stderr)
            text = path.read_text(encoding="utf-8")
            self.assertEqual(key_value(text, "bundleVersion"), "1.0.1")
            self.assertEqual(key_value(text, "AndroidBundleVersionCode"), "2")
            self.assertEqual(key_value(text, "tvOSBundleVersion"), "1.0")
            self.assertFalse(path.with_name(path.name + ".tmp").exists())

    def test_cli_print_current_and_invalid_set(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "ProjectSettings.asset"
            path.write_text(FIXTURE, encoding="utf-8")
            printed = subprocess.run(
                [sys.executable, str(SCRIPT), "--project", str(path), "--print-current", "--print-code"],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(printed.returncode, 0, printed.stderr)
            self.assertEqual(printed.stdout.splitlines(), ["1.0", "1"])
            invalid = subprocess.run(
                [sys.executable, str(SCRIPT), "--project", str(path), "--set", "nope"],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertNotEqual(invalid.returncode, 0)
            self.assertEqual(path.read_text(encoding="utf-8"), FIXTURE)


class SideloadReleaseContractTests(unittest.TestCase):
    def test_release_workflow_reuses_gameci_and_names_secrets(self):
        workflow = (ROOT / ".github" / "workflows" / "quest-release.yml").read_text(encoding="utf-8")
        tests = (ROOT / ".github" / "workflows" / "unity-tests.yml").read_text(encoding="utf-8")
        image = "unityci/editor:ubuntu-6000.5.2f1-android-3"
        self.assertIn(image, tests)
        self.assertIn(image, workflow)
        self.assertIn("unityVersion: 6000.5.2f1", workflow)
        self.assertIn("game-ci/unity-builder@v4", workflow)
        self.assertIn("androidExportType: androidPackage", workflow)
        self.assertIn("versioning: Custom", workflow)
        self.assertIn('tags:', workflow)
        self.assertIn('"v*"', workflow)
        self.assertNotIn("pull_request:", workflow)
        for name in REQUIRED_SECRETS:
            self.assertIn(f"secrets.{name}", workflow)
        self.assertNotIn("BEGIN CERTIFICATE", workflow)

    def test_sideload_doc_names_the_same_secrets(self):
        doc = (ROOT / "docs" / "distribution" / "sideload.md").read_text(encoding="utf-8")
        for name in REQUIRED_SECRETS:
            self.assertIn(name, doc)
        self.assertIn("scripts/bump-quest-version.py", doc)

    def test_manifest_declares_internet_and_no_unused_dangerous_permissions(self):
        manifest = (ROOT / "Assets" / "Plugins" / "Android" / "AndroidManifest.xml").read_text(encoding="utf-8")
        settings = (ROOT / "ProjectSettings" / "ProjectSettings.asset").read_text(encoding="utf-8")
        self.assertIn("android.permission.INTERNET", manifest)
        self.assertIn("com.oculus.permission.HAND_TRACKING", manifest)
        for permission in PROHIBITED_PERMISSIONS:
            self.assertNotIn(permission, manifest)
        self.assertIn("\n  ForceInternetPermission: 1\n", settings)

    def test_distribution_docs_exist(self):
        for relative in (
            "docs/distribution/sideload.md",
            "docs/distribution/horizon-store-checklist.md",
            "docs/distribution/privacy-policy.md",
        ):
            self.assertTrue((ROOT / relative).is_file(), relative)


if __name__ == "__main__":
    unittest.main()
