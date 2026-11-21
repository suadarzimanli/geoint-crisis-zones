"""Tests for fetch_acled.py (no network).  Run:  python -m unittest tools/test_fetch_acled.py"""
import csv
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import fetch_acled as fa  # noqa: E402

SCRIPT = Path(__file__).resolve().parent / "fetch_acled.py"


class DateTests(unittest.TestCase):
    def test_iso_and_written_dates(self):
        self.assertEqual(fa.iso_date("2024-12-13"), "2024-12-13")
        self.assertEqual(fa.iso_date("2024-12-13T00:00:00"), "2024-12-13")
        self.assertEqual(fa.iso_date("13 December 2024"), "2024-12-13")
        self.assertEqual(fa.iso_date("02 January 2020"), "2020-01-02")
        self.assertIsNone(fa.iso_date(""))
        self.assertIsNone(fa.iso_date("not a date"))


class FeatureTests(unittest.TestCase):
    def row(self, **kw):
        r = {k: "" for k in fa.FIELDS}
        r.update(event_id_cnty="POL1", event_date="2024-03-05", latitude="52.23", longitude="21.01", fatalities="0")
        r.update(kw)
        return r

    def test_valid_row_becomes_point_feature(self):
        f = fa.to_feature(self.row(fatalities="2", notes="  text  "))
        self.assertEqual(f["geometry"]["coordinates"], [21.01, 52.23])
        self.assertEqual(f["properties"]["fatalities"], 2)
        self.assertEqual(f["properties"]["notes"], "text")

    def test_invalid_rows_are_dropped(self):
        for bad in (dict(latitude=""), dict(latitude="0", longitude="0"), dict(latitude="95"),
                    dict(event_id_cnty=""), dict(event_date="soon")):
            self.assertIsNone(fa.to_feature(self.row(**bad)), bad)

    def test_written_dates_are_normalised(self):
        self.assertEqual(fa.to_feature(self.row(event_date="13 December 2024"))["properties"]["event_date"], "2024-12-13")


class CsvCommandTests(unittest.TestCase):
    def test_export_with_written_dates_is_filtered_and_converted(self):
        with tempfile.TemporaryDirectory() as d:
            src, out = Path(d) / "export.csv", Path(d) / "out.geojson"
            with open(src, "w", encoding="utf-8", newline="") as f:
                w = csv.DictWriter(f, ["event_id_cnty", "event_date", "latitude", "longitude", "admin2"])
                w.writeheader()
                w.writerow(dict(event_id_cnty="A", event_date="13 December 2024", latitude="52.2", longitude="21.0", admin2="Warszawa"))
                w.writerow(dict(event_id_cnty="B", event_date="2025-01-03", latitude="52.2", longitude="21.0", admin2="Warszawa"))
            r = subprocess.run([sys.executable, str(SCRIPT), "--csv", str(src), "--out", str(out)], capture_output=True, text=True)
            self.assertEqual(r.returncode, 0, r.stderr)
            feats = json.loads(out.read_text(encoding="utf-8"))["features"]
            self.assertEqual([f["properties"]["event_id_cnty"] for f in feats], ["A"])   # 2025 is outside the default period

    def test_non_acled_csv_is_rejected_clearly(self):
        with tempfile.TemporaryDirectory() as d:
            src = Path(d) / "other.csv"
            src.write_text("a,b\n1,2\n", encoding="utf-8")
            r = subprocess.run([sys.executable, str(SCRIPT), "--csv", str(src), "--out", str(Path(d) / "o.geojson")],
                               capture_output=True, text=True)
            self.assertNotEqual(r.returncode, 0)
            self.assertIn("not an ACLED export", r.stderr)


if __name__ == "__main__":
    unittest.main()
