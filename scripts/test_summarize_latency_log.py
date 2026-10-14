"""Unit tests for the latency log summary and the JPEG capture marker."""

import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import jpeg_capture_stamp
import summarize_latency_log

ROOT = Path(__file__).resolve().parents[1]


class SummarizeTests(unittest.TestCase):
    def test_pass_row_and_non_hardware_banner(self):
        text = summarize_latency_log.summarize({
            "hardware_run": False,
            "summary": [{
                "segment": "input_to_command",
                "n": 4,
                "incomplete": 0,
                "p50_ms": 12,
                "p95_ms": 18,
                "budget_p95_ms": 25,
                "verdict": "pass",
            }],
        })
        self.assertIn("not a field measurement", text)
        self.assertIn("| input_to_command | 25 | 4 | 0 | 12 | 18 | pass |", text)
        self.assertIn("| command_to_motion |  | 0 | 0 |  |  | not_measured |", text)

    def test_hardware_log_has_no_banner(self):
        text = summarize_latency_log.summarize({
            "hardware_run": True,
            "summary": [],
        })
        self.assertNotIn("not a field measurement", text)
        self.assertIn("| glass_to_glass |  | 0 | 0 |  |  | not_measured |", text)


class JpegStampTests(unittest.TestCase):
    def test_com_segment_carries_marker(self):
        segment = jpeg_capture_stamp.jpeg_com_segment(1700000001234)
        self.assertTrue(segment.startswith(b"\xff\xfe"))
        length = int.from_bytes(segment[2:4], "big")
        payload = segment[4:]
        self.assertEqual(length, len(payload) + 2)
        self.assertEqual(payload, b"OHHO_CAP:1700000001234")

    def test_negative_stamp_rejected(self):
        with self.assertRaises(ValueError):
            jpeg_capture_stamp.jpeg_com_segment(-1)


class BudgetTemplateTests(unittest.TestCase):
    def test_template_matches_budget_file_and_is_unmeasured(self):
        budgets = json.loads((ROOT / "docs" / "latency-budgets.json").read_text(encoding="utf-8"))
        template = json.loads((ROOT / "docs" / "latency-results.template.json").read_text(encoding="utf-8"))
        self.assertFalse(template["hardware_run"])
        seen = set()
        for row in template["rows"]:
            profile = row["profile"]
            segment = row["segment"]
            seen.add((profile, segment))
            self.assertEqual(budgets["profiles"][profile][segment], row["budget_p95_ms"])
            self.assertIsNone(row["measured_p50_ms"])
            self.assertIsNone(row["measured_p95_ms"])
            self.assertIsNone(row["n"])
            self.assertEqual(row["verdict"], "not_measured")
        self.assertEqual(len(seen), 15)
        for profile, segments in budgets["profiles"].items():
            for segment in segments:
                self.assertIn((profile, segment), seen)


if __name__ == "__main__":
    unittest.main()
