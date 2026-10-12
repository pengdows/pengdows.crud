#!/usr/bin/env python3
"""Regression test for tools/record-integration-run.py: the doc it keeps is UTF-8 with LF line endings whatever
the host's locale. On Windows the default encoding is cp1252, so reading the doc mangled the "·" separator, no
existing cell matched, every database fell back to "not recorded", and the file was rewritten with CRLF.

    python tools/test_record_integration_run.py
"""
import importlib.util
import pathlib
import tempfile
import unittest

SCRIPT = pathlib.Path(__file__).with_name("record-integration-run.py")
spec = importlib.util.spec_from_file_location("record_integration_run", SCRIPT)
recorder = importlib.util.module_from_spec(spec)
spec.loader.exec_module(recorder)

CELL = "147 pass, 0 fail, 0 skip · 2026-10-12 15:11Z · 0d031db1"


class RecorderEncodingTests(unittest.TestCase):
    def test_existing_cells_survive_a_round_trip_with_lf_endings(self):
        with tempfile.TemporaryDirectory() as folder:
            doc = pathlib.Path(folder) / "integration-status.md"
            cells = {("Sqlite", "net8.0"): (147, 0, 0, "2026-10-12 15:11", "0d031db1")}
            doc.write_bytes(recorder.render(cells).encode("utf-8"))

            read_back = recorder.read_doc(doc)
            self.assertEqual(cells, read_back)

            recorder.write_doc(doc, recorder.render(read_back))
            raw = doc.read_bytes()
            self.assertIn(CELL.encode("utf-8"), raw)
            self.assertNotIn(b"\r\n", raw)


if __name__ == "__main__":
    unittest.main()
