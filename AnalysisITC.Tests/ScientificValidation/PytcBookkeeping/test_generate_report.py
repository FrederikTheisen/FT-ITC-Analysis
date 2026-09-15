"""Check that visual reports cannot silently mislabel or alter the scientific evidence."""
import json
from pathlib import Path
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

import generate_report as report


DIRECTORY = Path(__file__).resolve().parent


class ReportEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.reference = json.loads((DIRECTORY/"reference.json").read_text())
        self.comparisons = json.loads((DIRECTORY/"comparisons.json").read_text())

    def check_modified(self):
        with patch.object(report.json, "loads", side_effect=[self.reference, self.comparisons]):
            report.load_inputs(DIRECTORY)

    def test_frozen_evidence_is_consistent(self):
        _, _, results = report.load_inputs(DIRECTORY)
        self.assertEqual(len(results), 38)

    def test_stale_reference_hash_is_rejected(self):
        self.comparisons["ReferenceSha256"] = "stale"
        with self.assertRaisesRegex(ValueError, "different reference manifest"):
            self.check_modified()

    def test_changed_curve_cannot_reuse_old_pass_result(self):
        result = self.comparisons["Results"][0]
        result["ActualHeatsJoules"][0] += result["PeakHeatJoules"]
        with self.assertRaisesRegex(ValueError, "Recorded error disagrees"):
            self.check_modified()

    def test_wrong_production_model_is_rejected(self):
        result = next(r for r in self.comparisons["Results"] if r["Id"] == "two-c50-r10")
        result["FtItcModel"] = "SequentialBindingSites"
        with self.assertRaisesRegex(ValueError, "Wrong FT-ITC model"):
            self.check_modified()

    def test_missing_case_is_rejected(self):
        self.comparisons["Results"].pop()
        with self.assertRaisesRegex(ValueError, "case list differs"):
            self.check_modified()

    def test_wrong_pass_label_is_rejected(self):
        self.comparisons["Results"][0]["Passed"] = False
        with self.assertRaisesRegex(ValueError, "Recorded acceptance disagrees"):
            self.check_modified()

    def test_skipped_test_is_counted_from_result_not_vstest_counter(self):
        xml = '''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results><UnitTestResult testName="passing" outcome="Passed"/>
                   <UnitTestResult testName="skipped" outcome="NotExecuted"/></Results>
          <ResultSummary><Counters total="2" passed="1" failed="0" notExecuted="0"/></ResultSummary>
        </TestRun>'''
        with patch.object(report.ET, "parse", return_value=ET.ElementTree(ET.fromstring(xml))), \
             patch.object(report, "sha", return_value="test-hash"):
            result = report.trx_summary(Path("test.trx"))
        self.assertEqual(result["result_counts"], dict(total=2, passed=1, failed=0, skipped=1))


if __name__ == "__main__":
    unittest.main()
