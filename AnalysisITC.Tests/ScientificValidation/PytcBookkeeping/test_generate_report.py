"""Check that visual reports cannot silently mislabel or alter the scientific evidence."""
import json
from pathlib import Path
import shutil
import tempfile
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
        _, _, results, _ = report.load_inputs(DIRECTORY)
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

    def test_readme_counts_percentages_and_benchmarks_match_frozen_evidence(self):
        readme = (DIRECTORY/"README.md").read_text()
        results = {r["Id"]: r for r in self.comparisons["Results"]}
        passed = sum(r["Passed"] for r in results.values())
        self.assertIn(f"{passed}/{len(results)} cases meet the practical limit", readme)
        for model, label in (("one-site", "One-site"), ("two-site", "Two independent sites, one of each"),
                             ("competitive", "Competitive"), ("sequential", "Sequential, 2-4 steps")):
            rows = [results[c["id"]] for c in self.reference["cases"] if c["model"] == model]
            count = len(rows)
            maximum_percent = 100*max(r["ErrorFractionOfPeak"] for r in rows)
            expected = (f"| {label} | {count} | {maximum_percent:.6g} | "
                        f"{sum(r['Passed'] for r in rows)}/{count} | "
                        f"{sum(r['RoundoffGoalMet'] for r in rows)}/{count} |")
            self.assertIn(expected, readme)
        for filename, label in (("one-exothermic-performance.json", "One-site"),
                                ("sequential-2-performance.json", "Sequential 2")):
            data = json.loads((DIRECTORY/filename).read_text())
            for row in data["Measurements"]:
                self.assertIn(f"| {label} / {row['Method']} | {row['ObjectiveMilliseconds']:g} | "
                              f"{row['FitMilliseconds']:g} |", readme)

    def test_overview_includes_all_discrepancies_and_acceptance_line(self):
        results = {r["Id"]: r for r in self.comparisons["Results"]}
        with patch.object(report.plt.Figure, "savefig"), patch.object(report.plt, "close"):
            report.plot_overview(self.reference["cases"], results, .0001, Path("unused.png"))
            figure = report.plt.gcf()
        try:
            axis = figure.axes[0]
            lower, upper = axis.get_xlim()
            for result in results.values():
                self.assertGreater(100*result["ErrorFractionOfPeak"], lower)
                self.assertLess(100*result["ErrorFractionOfPeak"], upper)
            self.assertGreater(.01, lower)
            self.assertLess(.01, upper)
        finally:
            report.plt.close(figure)

    def test_overview_can_display_exact_zero_on_log_axis(self):
        case = self.reference["cases"][0]
        results = {case["id"]: {"ErrorFractionOfPeak": 0.0}}
        with patch.object(report.plt.Figure, "savefig"), patch.object(report.plt, "close"):
            report.plot_overview([case], results, .0001, Path("unused.png"))
            figure = report.plt.gcf()
        try:
            axis = figure.axes[0]
            lower, upper = axis.get_xlim()
            x = axis.collections[0].get_offsets()[0][0]
            self.assertGreater(x, lower)
            self.assertLess(x, upper)
            self.assertIn("exact zero", axis.get_xlabel())
        finally:
            report.plt.close(figure)

    def check_sensitivity(self, data):
        results = {r["Id"]: r for r in self.comparisons["Results"]}
        with patch.object(report.json, "loads", return_value=data):
            return report.load_sensitivity(DIRECTORY, self.reference, self.comparisons, results)

    def test_sensitivity_is_separate_native_evidence_with_a_matching_base(self):
        data = json.loads((DIRECTORY/"solver-sensitivity.json").read_text())
        _, case = self.check_sensitivity(data)
        self.assertEqual(case["id"], "two-realistic")
        self.assertEqual(len(data["scales"]), 15)

    def test_stale_sensitivity_provenance_is_rejected(self):
        data = json.loads((DIRECTORY/"solver-sensitivity.json").read_text())
        data["reference_sha256"] = "stale"
        with self.assertRaisesRegex(ValueError, "Stale sensitivity evidence"):
            self.check_sensitivity(data)

    def test_incomplete_sensitivity_curve_is_rejected(self):
        data = json.loads((DIRECTORY/"solver-sensitivity.json").read_text())
        for curve in data["native_heats_joules"]:
            curve.pop()
        with self.assertRaisesRegex(ValueError, "Incomplete or nonfinite"):
            self.check_sensitivity(data)

    def test_altered_native_sensitivity_base_is_rejected(self):
        data = json.loads((DIRECTORY/"solver-sensitivity.json").read_text())
        data["native_heats_joules"][data["scales"].index(1.0)][0] += 1e-6
        with self.assertRaisesRegex(ValueError, "native base curve differs"):
            self.check_sensitivity(data)

    def test_altered_ftitc_sensitivity_base_is_rejected(self):
        data = json.loads((DIRECTORY/"solver-sensitivity.json").read_text())
        data["ftitc_base_heats_joules"][0] += 1e-6
        with self.assertRaisesRegex(ValueError, "FT-ITC base curve differs"):
            self.check_sensitivity(data)

    def test_generation_preserves_other_figures_and_includes_sensitivity_in_both_reports(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)/"report"
            shutil.copytree(DIRECTORY, directory,
                            ignore=shutil.ignore_patterns("figures", "*.png", "__pycache__"))
            figures = directory/"figures"
            figures.mkdir()
            sentinel = figures/"two-site-user-diagnostic.png"
            sentinel.write_bytes(b"Keep this separately authored diagnostic.")
            # Exercise the complete document/evidence path without redrawing
            # every unchanged scientific panel in this lifecycle regression.
            def placeholder(*args):
                destination = next(arg for arg in args if isinstance(arg, Path))
                shutil.copyfile(DIRECTORY/"figures/forward-comparison-overview.png", destination)
            pdf = directory/"report.pdf"
            with patch.object(report, "plot_overview", side_effect=placeholder), \
                 patch.object(report, "plot_pairs", side_effect=placeholder), \
                 patch.object(report, "plot_sensitivity", side_effect=placeholder):
                report.generate(directory, pdf, "2026-09-16", None, None)
            self.assertEqual(sentinel.read_bytes(), b"Keep this separately authored diagnostic.")
            self.assertTrue((figures/"two-site-3-solver-sensitivity.png").is_file())
            md = (directory/"REPORT.md").read_text()
            text = "\n".join(page.extract_text() for page in report.PdfReader(pdf).pages)
            for content in (md, text):
                normalized = " ".join(content.split())
                self.assertIn("Exploratory concentration-sensitivity diagnostic", normalized)
                self.assertIn("does not change any acceptance result", normalized)
            self.assertIn("figures/two-site-3-solver-sensitivity.png", md)
            evidence = json.loads((directory/"report-evidence.json").read_text())
            self.assertEqual((evidence["passed"], evidence["failed"], evidence["required_passed"]), (28, 10, 24))
            self.assertEqual(evidence["sensitivity"]["native_curves"], 15)
            self.assertFalse(evidence["sensitivity"]["counts_as_forward_validation"])

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
