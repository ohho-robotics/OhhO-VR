"""Unit tests for tools/beta_triage.py. The markdown fixtures are fake."""

from __future__ import annotations

import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

import beta_triage  # noqa: E402

SAMPLES = ROOT / "tools" / "testdata" / "beta_feedback"


def _form(**overrides: str) -> str:
    fields = {
        "tester_id": "test",
        "date": "2026-10-14",
        "build": "rc-fake",
        "sim": "local",
        "completed_first_drive": "yes",
        "time_to_first_drive_s": "30",
        "time_to_first_drive_source": "headset-log",
        "crashes": "none",
        "crash_count": "0",
        "comfort": "fine",
        "comfort_notes": "",
        "confusion": "none",
        "notes": "fake",
    }
    fields.update(overrides)
    lines = []
    for key, value in fields.items():
        lines.append(f"{key}: {value}")
    return "\n".join(lines) + "\n"


class ParseAndClassifyTests(unittest.TestCase):
    def test_sample_directory_priorities(self):
        items, errors = beta_triage.triage_paths([SAMPLES])
        self.assertEqual(errors, [])
        got = {(item.session.tester_id, item.priority) for item in items}
        self.assertEqual(
            got,
            {
                ("ada", "P1"),
                ("bea", "P1"),
                ("dax", "P2"),
                ("eve", "P2"),
                ("finn", "P2"),
                ("gia", "P3"),
            },
        )
        text = beta_triage.render(items, errors)
        self.assertLess(text.index("## P1"), text.index("## P2"))
        self.assertLess(text.index("## P2"), text.index("## P3"))
        self.assertIn("ada (crash.md):", text)
        self.assertIn("crash_count=1", text)
        self.assertIn("bea (never-drove.md):", text)
        self.assertIn("did not complete first drive", text)
        self.assertIn("dax (confusion.md):", text)
        self.assertIn("confusion=2", text)
        self.assertIn("finn (slow.md):", text)
        self.assertIn("time_to_first_drive_s=181 > 180", text)
        self.assertIn("eve (github-mild.md):", text)
        self.assertIn("comfort=mild", text)
        self.assertIn("gia (clean.md):", text)

    def test_crash_text_with_zero_count_is_p1(self):
        session = beta_triage.parse_feedback(
            _form(crashes="black screen", crash_count="0"), "mem"
        )
        priority, reasons = beta_triage.classify(session)
        self.assertEqual(priority, "P1")
        self.assertTrue(any(reason.startswith("crashes=") for reason in reasons))

    def test_quit_after_a_drive_is_p1(self):
        session = beta_triage.parse_feedback(_form(comfort="quit"), "mem")
        self.assertEqual(beta_triage.classify(session)[0], "P1")

    def test_exactly_180_seconds_is_p3(self):
        session = beta_triage.parse_feedback(_form(time_to_first_drive_s="180"), "mem")
        self.assertEqual(beta_triage.classify(session)[0], "P3")

    def test_just_over_180_is_p2(self):
        session = beta_triage.parse_feedback(_form(time_to_first_drive_s="180.01"), "mem")
        self.assertEqual(beta_triage.classify(session)[0], "P2")

    def test_missing_time_on_a_clean_drive_stays_p3(self):
        text = _form(time_to_first_drive_s="", time_to_first_drive_source="")
        session = beta_triage.parse_feedback(text, "mem")
        priority, reasons = beta_triage.classify(session)
        self.assertEqual(priority, "P3")
        self.assertIn("time_to_first_drive_s missing", reasons)

    def test_not_reached_is_p1_even_if_completed_says_yes(self):
        session = beta_triage.parse_feedback(
            _form(time_to_first_drive_source="not-reached"), "mem"
        )
        self.assertEqual(beta_triage.classify(session)[0], "P1")

    def test_missing_tester_id_is_a_parse_error(self):
        text = _form(tester_id="")
        with self.assertRaises(beta_triage.FeedbackParseError):
            beta_triage.parse_feedback(text, "bad.md")

    def test_negative_time_is_a_parse_error(self):
        with self.assertRaises(beta_triage.FeedbackParseError):
            beta_triage.parse_feedback(_form(time_to_first_drive_s="-1"), "bad.md")

    def test_notes_do_not_become_keys(self):
        text = _form(notes="") + "comfort: quit\nthis line stays in the notes\n"
        # _form already emitted notes: fake before we replace. Build explicitly.
        text = "\n".join(
            [
                "tester_id: notes-user",
                "completed_first_drive: yes",
                "crashes: none",
                "crash_count: 0",
                "comfort: fine",
                "sim: local",
                "notes: first line",
                "comfort: quit",
            ]
        )
        session = beta_triage.parse_feedback(text, "mem")
        self.assertEqual(session.comfort, "fine")
        self.assertIn("comfort: quit", session.notes)

    def test_cli_on_sample_dir_exits_zero(self):
        proc = subprocess.run(
            [sys.executable, str(ROOT / "tools" / "beta_triage.py"), str(SAMPLES)],
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertEqual(proc.returncode, 0, proc.stderr)
        self.assertIn("## P1 (2)", proc.stdout)
        self.assertIn("## P2 (3)", proc.stdout)
        self.assertIn("## P3 (1)", proc.stdout)

    def test_cli_parse_error_exits_one(self):
        proc = subprocess.run(
            [sys.executable, str(ROOT / "tools" / "beta_triage.py"), str(SAMPLES / "clean.md"), "-"],
            check=False,
            capture_output=True,
            text=True,
        )
        # A missing path is an error. argparse accepts "-" as a path.
        self.assertEqual(proc.returncode, 1)
        self.assertIn("Parse errors", proc.stdout)


if __name__ == "__main__":
    unittest.main()
