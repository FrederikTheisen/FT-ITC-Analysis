"""Exercise fresh-run orchestration and report rejection without invoking a compiler."""
import contextlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import run_validation as runner
from report_results import hash_file, report

DIRECTORY = Path(__file__).resolve().parent


class ValidationWorkflowTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.repository = self.root/"repository"
        for name in runner.MODEL_SOURCES + ["AnalysisITC.Core.Tests/AnalysisITC.Core.Tests.csproj"]:
            path = self.repository/name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("original build input")
        for name in ("ForwardPrecision", "PytcBookkeeping", "TwoSiteStoichiometry"):
            (self.repository/"AnalysisITC.Tests/ScientificValidation"/name).mkdir(parents=True)
        precision = json.loads((DIRECTORY.parent/"PytcBookkeeping/precision-reference.json").read_text())
        self.predictions = []
        for name in ("PytcBookkeeping", "TwoSiteStoichiometry"):
            cases = json.loads((DIRECTORY.parent/name/"reference.json").read_text())["cases"]
            self.predictions.append({"Results": [dict(Id=case["id"], ActualHeatsJoules=list(
                precision["runs"][0]["curves"][case["id"]])) for case in cases]})
        self.output = self.root/"runs"
        self.change_during = None
        self.fail_during = None
        self.omit_predictions = False
        self.summary = 'outcome="Completed"><Counters total="185" executed="185" passed="185" failed="0"'
        self.commands = []
        self.addCleanup(patch.stopall)
        patch.object(runner.subprocess, "run", side_effect=self.dotnet).start()
        patch("plot_forward_curves.plot_curves", side_effect=self.plot).start()

    @staticmethod
    def plot(directory, *_):
        for name in ("forward-curves.png", "forward-curves.pdf"):
            (directory/name).write_bytes(b"orchestration test placeholder")

    def dotnet(self, command, **options):
        self.commands.append(command)
        phase = command[1]
        if self.fail_during == phase:
            raise subprocess.CalledProcessError(1, command)
        if phase == "build":
            build = Path(command[command.index("--output")+1])
            build.mkdir()
            for name in ("AnalysisITC.Core.dll", "AnalysisITC.Core.Tests.dll"):
                (build/name).write_bytes(b"fixture assembly")
        else:
            self.assertEqual(Path(command[2]), self.run_directory/"build/AnalysisITC.Core.Tests.dll")
            self.assertEqual(command[command.index("--filter")+1], runner.FILTER)
            if not self.omit_predictions:
                for variable, payload in zip(("FTITC_PYTC_RESULTS", "FTITC_TWO_SITE_RESULTS"), self.predictions):
                    destination = Path(options["env"][variable])
                    self.assertEqual(destination.parent, self.run_directory)
                    destination.mkdir()
                    (destination/"comparisons.json").write_text(json.dumps(payload))
            (self.run_directory/"forward.trx").write_text(
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                '<ResultSummary '+self.summary+'/></ResultSummary></TestRun>')
        if self.change_during == phase:
            (self.repository/runner.MODEL_SOURCES[0]).write_text("changed while running")

    @property
    def run_directory(self):
        return next(self.output.glob("run-*"))

    def validate(self):
        with contextlib.redirect_stdout(io.StringIO()):
            return runner.validate(self.output, self.repository)

    def assert_no_report(self):
        self.assertFalse((self.run_directory/"report").exists())
        self.assertEqual([], list(self.run_directory.glob("report-*")))

    def test_build_test_report_uses_fresh_outputs_and_captured_build(self):
        stale = self.output/"old-results"
        stale.mkdir(parents=True)
        (stale/"comparisons.json").write_text("unusable old predictions")
        with patch.dict(os.environ, FTITC_PYTC_RESULTS=str(stale), FTITC_TWO_SITE_RESULTS=str(stale)):
            run = self.validate()
        self.assertEqual(["build", "test"], [command[1] for command in self.commands])
        self.assertIn("--no-incremental", self.commands[0])
        self.assertEqual("unusable old predictions", (stale/"comparisons.json").read_text())
        verification = json.loads((run/"report/verification.json").read_text())
        self.assertEqual({name: hash_file(self.repository/name) for name in runner.MODEL_SOURCES},
                         verification["production_source_sha256"])
        self.assertEqual(hash_file(run/"build/AnalysisITC.Core.dll"),
                         verification["build"]["assemblies_sha256"]["AnalysisITC.Core.dll"])
        self.assertEqual(52, len(json.loads((run/"report/comparisons.json").read_text())["results"]))
        self.assertIn("All 52 tests **PASS**", (run/"report/README.md").read_text())

    def test_build_failure_stops_before_tests_or_report(self):
        self.fail_during = "build"
        with self.assertRaises(subprocess.CalledProcessError):
            self.validate()
        self.assertEqual(1, len(self.commands))
        self.assert_no_report()

    def test_test_failure_stops_before_report(self):
        self.fail_during = "test"
        with self.assertRaises(subprocess.CalledProcessError):
            self.validate()
        self.assert_no_report()

    def test_missing_prediction_files_do_not_reuse_old_outputs(self):
        self.omit_predictions = True
        with self.assertRaises(FileNotFoundError):
            self.validate()
        self.assert_no_report()

    def test_missing_case_prevents_report(self):
        self.predictions[0]["Results"].pop()
        with self.assertRaisesRegex(AssertionError, "52 cases"):
            self.validate()
        self.assert_no_report()

    def test_duplicate_case_prevents_report(self):
        self.predictions[0]["Results"].append(self.predictions[0]["Results"][0])
        with self.assertRaisesRegex(AssertionError, "52 cases"):
            self.validate()
        self.assert_no_report()

    def test_numerical_failure_prevents_report(self):
        self.predictions[0]["Results"][0]["ActualHeatsJoules"][0] += 1e-6
        with self.assertRaisesRegex(AssertionError, "Floating-point mismatch"):
            self.validate()
        self.assert_no_report()

    def test_no_executed_tests_prevents_report(self):
        self.summary = 'outcome="Completed"><Counters total="0" executed="0" passed="0" failed="0"'
        with self.assertRaisesRegex(AssertionError, "Not all tests passed"):
            self.validate()
        self.assert_no_report()

    def test_aborted_run_prevents_report_even_with_successful_counters(self):
        self.summary = self.summary.replace('outcome="Completed"', 'outcome="Aborted"')
        with self.assertRaisesRegex(AssertionError, "did not complete"):
            self.validate()
        self.assert_no_report()

    def test_source_change_during_build_stops_before_tests(self):
        self.change_during = "build"
        with self.assertRaisesRegex(RuntimeError, "Build inputs changed"):
            self.validate()
        self.assertEqual(1, len(self.commands))
        self.assert_no_report()

    def test_source_change_during_tests_prevents_report(self):
        self.change_during = "test"
        with self.assertRaisesRegex(RuntimeError, "Build inputs changed"):
            self.validate()
        self.assert_no_report()

    def test_source_change_during_rendering_discards_staged_report(self):
        def changed_report(*arguments):
            report(*arguments)
            (self.repository/runner.MODEL_SOURCES[0]).write_text("changed during rendering")
        with patch.object(runner, "report", side_effect=changed_report):
            with self.assertRaisesRegex(RuntimeError, "Build inputs changed"):
                self.validate()
        self.assert_no_report()

    def test_assembly_replacement_during_tests_prevents_report(self):
        original = self.dotnet
        def replaced_assembly(command, **options):
            original(command, **options)
            if command[1] == "test":
                (self.run_directory/"build/AnalysisITC.Core.dll").write_bytes(b"another build")
        with patch.object(runner.subprocess, "run", side_effect=replaced_assembly):
            with self.assertRaisesRegex(RuntimeError, "Built assemblies changed"):
                self.validate()
        self.assert_no_report()


class ReportCommandTests(unittest.TestCase):
    def test_report_only_command_refuses_saved_result_arguments(self):
        result = subprocess.run([sys.executable, str(DIRECTORY/"report_results.py"),
                                 "--original-results", "old.json", "--stoichiometry-results", "old.json",
                                 "--trx", "old.trx"], capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("run_validation.py", result.stderr)


if __name__ == "__main__":
    unittest.main()
