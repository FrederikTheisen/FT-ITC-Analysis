"""Run with: python3 -m unittest discover -s scripts/ftxtc-recovery -v"""

import copy
import csv
import hashlib
import json
import struct
import subprocess
import sys
import tempfile
import unittest
import warnings
import zipfile
from pathlib import Path
from unittest import mock

import recover_ftxtc as recovery


EXPERIMENT = "experiments/000000/experiment.json"
RAW = "experiments/000000/thermogram.ftxb"
BASELINE = "experiments/000000/baseline.ftxb"
SOLUTION = "solutions/000000/solution.json"
RESULT = "results/000000/result.json"


def matrix(kind, rows, columns):
    values = [value for row in rows for value in row]
    return struct.pack("<4sBBBBii", b"FTXB", 1, kind, 1, 0, len(rows), columns) + struct.pack(
        "<" + ("f" if kind == 1 else "d") * len(values), *values)


def fwe(value, sd=0, lower=None, upper=None, missing=False):
    return {"value": value, "standardDeviation": sd, "lower95": value if lower is None else lower,
            "upper95": value if upper is None else upper, "isMissing": missing}


def fixture():
    """Independent numerical examples; no production writer/codec generates these."""
    return {
        "project.json": {"projectSchemaVersion": 4,
                         "experiments": [{"id": "e1", "metadata": EXPERIMENT, "thermogram": RAW, "baseline": BASELINE}],
                         "solutions": [{"id": "s1", "experimentId": "e1", "metadata": SOLUTION}],
                         "results": [{"id": "r1", "metadata": RESULT}], "reports": []},
        EXPERIMENT: {"id": "e1", "name": "Zoë 李明 / ../CON", "cellConcentration": fwe(3e-6, 1e-7),
                     "cellVolume": 200e-6, "attachedSolutionId": "s1", "processor": {"baselineCompleted": True},
                     "segments": [{"firstInjectionId": 0, "initialCellConcentration": 3e-6}],
                     "attributes": [{"key": "buffer-subtraction", "stringValue": "missing-reference",
                                     "bufferReferenceSnapshot": {"referenceName": "blank", "points": [{"heat": fwe(1e-6)}]}}],
                     "unknownFutureField": {"nested": [None, False, 0, "a\nb"]},
                     "injections": [{"id": 0, "included": False, "isIntegrated": True, "volume": 2e-6,
                                     "time": 10, "delay": 100, "duration": 4, "temperature": 25,
                                     "actualCellConcentration": 3e-6, "actualTitrantConcentration": 6e-6,
                                     "ratio": 2, "integrationStartDelay": 1, "integrationEndOffset": 60,
                                     "rawPeakArea": fwe(-4e-6, 1e-6, -6e-6, -2e-6),
                                     "correctedPeakArea": fwe(-5e-6)},
                                    {"id": 1, "included": True, "isIntegrated": False,
                                     "rawPeakArea": fwe(0, missing=True)}]},
        # First adjusted value must be 0: baseline rounds to 1 in Float32.
        RAW: matrix(1, [[0, 1, 25], [10, 4, 26]], 3),
        BASELINE: matrix(2, [[0.99999998, 0, 0.9, 1.1], [1.25, .125, 1, 1.5]], 4),
        SOLUTION: {"schemaVersion": 1, "id": "s1", "experimentId": "e1", "modelId": "one-set-of-sites",
                   "modelSchemaVersion": 1, "heatMethod": "legacy", "weighted": True,
                   "isValid": False, "errorMethod": "profile-likelihood", "parameterBoundaryHit": True,
                   "fittedParameters": [{"id": "affinity-log10-1", "value": 6, "locked": False},
                                        {"id": "enthalpy-1", "value": -30000, "locked": True}],
                   "reportedParameters": [{"id": "affinity-log10-1", "estimate": fwe(6, .5, 5, 7)},
                                          {"id": "enthalpy-1", "estimate": fwe(-30000, 1000, -32000, -28000)},
                                          {"id": "offset", "estimate": fwe(0)}],
                   "profile": {"calibration": "unweighted-f-calibrated-rss", "coordinates": [
                       {"parameterId": "enthalpy-1", "scope": "local", "experimentId": "e1", "index": 0,
                        "bestValue": -30000, "lowerBound": -100000, "upperBound": 0,
                        "lower": {"outcome": "endpoint-found", "endpoint": -32000},
                        "upper": {"outcome": "search-exhausted"}}]}},
        RESULT: {"id": "r1", "name": "result", "memberSolutionIds": ["s1"], "modelId": "one-set-of-sites",
                 "constraints": [{"parameterId": "enthalpy-1", "constraint": "same-for-all"}],
                 "globalParameters": [{"id": "gibbs-1", "value": -35000, "locked": False}],
                 "globalReplicates": [{"index": 0, "globalParameters": [{"id": "gibbs-1", "value": 12345}]}],
                 "advancedAnalyses": {"protonation": {"schemaVersion": 1, "bindingEnthalpy": fwe(-20000, 1000),
                                                      "protonationChange": fwe(1.5, .1)}}},
    }


def write_archive(path, payloads, minor=6, manifest_change=None, extra_entries=()):
    entries = {name: value if isinstance(value, bytes) else json.dumps(value, ensure_ascii=False).encode()
               for name, value in payloads.items()}
    manifest = {"format": "ftxtc", "schemaMajor": 1, "schemaMinor": minor, "root": "project.json",
                "entries": [{"path": name, "length": len(value), "sha256": hashlib.sha256(value).hexdigest(),
                             "mediaType": "application/json" if name.endswith(".json") else "application/x-ftxb"}
                            for name, value in entries.items()]}
    if manifest_change:
        manifest_change(manifest)
    with warnings.catch_warnings():
        warnings.simplefilter("ignore", UserWarning)
        with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, value in entries.items():
                archive.writestr(name, value)
            if "manifest.json" not in entries:
                archive.writestr("manifest.json", json.dumps(manifest))
            for name, value in extra_entries:
                archive.writestr(name, value)


def read_csv(path):
    with path.open(encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


class RecoveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "project.ftxtc"
        self.payloads = fixture()

    def extract(self, **kwargs):
        write_archive(self.source, self.payloads, **kwargs)
        self.code, self.out = recovery.recover(self.source, progress=lambda _: None)
        self.index = read_csv(self.out / "index.csv")
        return self.code

    def folder(self, kind):
        return self.out / next(item["output_directory"] for item in self.index if item["kind"] == kind)

    def test_exact_trace_subtraction_and_lab_units(self):
        self.assertEqual(self.extract(), 0)
        rows = read_csv(self.folder("experiment") / "thermogram.csv")
        self.assertEqual(float(rows[0]["adjusted_power_µW"]), 0)
        self.assertEqual(float(rows[1]["adjusted_power_µW"]), 2750000)
        self.assertEqual(float(rows[1]["raw_power_µW"]), 4000000)
        self.assertEqual(float(rows[1]["baseline_sd_µW"]), 125000)
        self.assertEqual(float(rows[1]["time_s"]), 10)
        self.assertEqual(float(rows[1]["temperature_°C"]), 26)

    def test_injections_preserve_missing_zero_and_exclusions(self):
        self.extract()
        first, second = read_csv(self.folder("experiment") / "injections.csv")
        for column, expected in {"volume_µL": 2, "actual_cell_concentration_µM": 3,
                                 "actual_titrant_concentration_µM": 6, "raw_peak_area_µJ": -4,
                                 "raw_peak_area_sd_µJ": 1, "corrected_peak_area_µJ": -5}.items():
            self.assertAlmostEqual(float(first[column]), expected)
        self.assertEqual(first["included"], "false")
        self.assertEqual(first["corrected_peak_area_sd_µJ"], "0.0")
        self.assertEqual(second["raw_peak_area_µJ"], "")
        self.assertEqual(second["raw_peak_area_is_missing"], "true")
        self.assertEqual(second["corrected_peak_area_µJ"], "")
        self.assertEqual(second["corrected_peak_area_is_missing"], "")
        self.assertEqual(second["is_integrated"], "false")

    def test_parameter_centers_errors_kd_and_bounds(self):
        self.extract()
        rows = read_csv(self.folder("solution") / "parameters.csv")
        kd = next(row for row in rows if row["source"] == "reportedParameters" and row["quantity"] == "Kd")
        self.assertEqual(float(kd["value"]), 1)
        self.assertAlmostEqual(float(kd["lower95"]), .1)
        self.assertEqual(float(kd["upper95"]), 10)
        self.assertEqual(kd["standard_deviation"], "")
        enthalpy = next(row for row in rows if row["source"] == "fittedParameters" and row["parameter_id"] == "enthalpy-1")
        self.assertEqual(float(enthalpy["value"]), -30)
        self.assertEqual(enthalpy["locked"], "true")
        profile = read_csv(self.folder("solution") / "profile_bounds.csv")[0]
        self.assertEqual(float(profile["lowerBound"]), -100)
        self.assertEqual(float(profile["upperBound"]), 0)
        self.assertEqual(profile["upper_endpoint"], "")
        self.assertEqual(profile["upper_outcome"], "search-exhausted")
        text = (self.folder("solution") / "metadata.txt").read_text()
        self.assertIn('["isValid"] = false', text)
        self.assertIn('["parameterBoundaryHit"] = true', text)

    def test_global_constraints_advanced_and_no_refits(self):
        self.extract()
        folder = self.folder("result")
        global_row = read_csv(folder / "parameters.csv")[0]
        self.assertEqual(float(global_row["value"]), -35)
        self.assertEqual(global_row["standard_deviation"], "")
        self.assertEqual(read_csv(folder / "constraints.csv")[0]["constraint"], "same-for-all")
        advanced = read_csv(folder / "advanced_parameters.csv")
        self.assertEqual(float(advanced[0]["value"]), -20)
        self.assertEqual(float(advanced[0]["standard_deviation"]), 1)
        text = (folder / "metadata.txt").read_text()
        self.assertIn("individual refits intentionally excluded", text)
        self.assertNotIn("12345", text)
        self.assertFalse(list(self.out.rglob("*.json")))

    def test_unknown_metadata_tandem_and_buffer_snapshot_survive(self):
        self.extract()
        text = (self.folder("experiment") / "metadata.txt").read_text()
        self.assertIn('["unknownFutureField"]["nested"][0] = null', text)
        self.assertIn('["unknownFutureField"]["nested"][2] = 0', text)
        self.assertIn('"a\\nb"', text)
        self.assertIn('["segments"][0]["firstInjectionId"] = 0', text)
        self.assertIn('["bufferReferenceSnapshot"]["referenceName"] = "blank"', text)

    def test_manifest_version_acceptance(self):
        # Wire-shape compatibility is covered by the separately frozen corpus.
        for minor in range(2, 7):
            with self.subTest(minor=minor):
                self.assertEqual(self.extract(minor=minor), 0)

    def test_unsupported_schema_rejected_without_output(self):
        next_minor = max(minor for major, minor in recovery.SUPPORTED_SCHEMAS if major == 1) + 1
        for minor in (0, 1, next_minor):
            write_archive(self.source, self.payloads, minor=minor)
            with self.assertRaisesRegex(recovery.RecoveryError, "Unsupported FTXTC"):
                recovery.recover(self.source)
        self.assertFalse((self.root / "project_recovered").exists())

    def test_integrated_only_and_unprocessed_raw(self):
        self.payloads[RAW] = matrix(1, [], 3)
        self.payloads[BASELINE] = matrix(2, [], 4)
        self.payloads[EXPERIMENT]["processor"] = None
        self.assertEqual(self.extract(), 0)
        self.assertEqual(read_csv(self.folder("experiment") / "thermogram.csv"), [])
        self.payloads[RAW] = matrix(1, [[1, 2, 3]], 3)
        self.assertEqual(self.extract(), 0)
        row = read_csv(self.folder("experiment") / "thermogram.csv")[0]
        self.assertEqual(row["adjusted_power_µW"], "")

    def test_baseline_mismatch_keeps_both_tables(self):
        self.payloads[BASELINE] = matrix(2, [[1, 0, 1, 1]], 4)
        self.assertEqual(self.extract(), 1)
        folder = self.folder("experiment")
        self.assertEqual(len(read_csv(folder / "baseline.csv")), 1)
        for row in read_csv(folder / "thermogram.csv"):
            self.assertEqual(row["adjusted_power_µW"], "")
            self.assertEqual(row["baseline_µW"], "")

    def test_missing_trace_preserves_injections_and_standalone_baseline(self):
        del self.payloads[RAW]
        self.assertEqual(self.extract(), 1)
        folder = self.folder("experiment")
        self.assertFalse((folder / "thermogram.csv").exists())
        self.assertEqual(len(read_csv(folder / "injections.csv")), 2)
        self.assertEqual(len(read_csv(folder / "baseline.csv")), 2)

    def test_checksum_and_length_mismatch_mark_output(self):
        for field, value in (("sha256", "0" * 64), ("length", 1)):
            with self.subTest(field=field):
                def damage(manifest):
                    next(entry for entry in manifest["entries"] if entry["path"] == RAW)[field] = value
                self.assertEqual(self.extract(manifest_change=damage), 1)
                self.assertIn("UNVERIFIED", (self.folder("experiment") / "metadata.txt").read_text())
                self.assertEqual(len(read_csv(self.folder("experiment") / "thermogram.csv")), 2)

    def test_malformed_json_component_does_not_drop_dependents(self):
        self.payloads[EXPERIMENT] = b"{broken"
        self.assertEqual(self.extract(), 1)
        self.assertEqual([row["kind"] for row in self.index], ["experiment", "solution", "result"])
        folder = self.folder("experiment")
        self.assertIn("Component JSON unavailable", (folder / "metadata.txt").read_text())
        self.assertEqual(self.index[0]["id"], "e1")
        self.assertEqual(len(read_csv(folder / "thermogram.csv")), 2)
        self.assertTrue(all(row["adjusted_power_µW"] == "" for row in read_csv(folder / "thermogram.csv")))

    def test_missing_experiment_and_root_metadata_preserves_orphan_tables(self):
        del self.payloads[EXPERIMENT]
        self.payloads["project.json"] = b"damaged"
        self.assertEqual(self.extract(), 1)
        self.assertTrue((self.folder("experiment") / "thermogram.csv").exists())
        self.assertIn("Unresolved/ambiguous", (self.folder("solution") / "metadata.txt").read_text())

    def test_missing_manifest_or_project_scans_components(self):
        for path in ("manifest.json", "project.json"):
            with self.subTest(path=path):
                self.payloads = fixture()
                self.payloads[path] = b"not JSON"
                self.assertEqual(self.extract(), 1)
                self.assertEqual(len(self.index), 3)
                self.assertTrue(all(row["status"] == "unverified/partial" for row in self.index))
                self.assertEqual(len(read_csv(self.folder("experiment") / "thermogram.csv")), 2)

    def test_damaged_matrix_variants_do_not_stop_other_outputs(self):
        original = self.payloads[RAW]
        for bad in (original[:8], original[:-1], b"WRNG" + original[4:],
                    matrix(2, [[1, 2, 3]], 3), matrix(1, [[1, 2]], 2),
                    original[:8] + struct.pack("<ii", -1, 3) + original[16:]):
            with self.subTest(header=bad[:16]):
                self.payloads[RAW] = bad
                self.assertEqual(self.extract(), 1)
                self.assertFalse((self.folder("experiment") / "thermogram.csv").exists())
                self.assertTrue((self.folder("solution") / "parameters.csv").exists())

    def test_nonfinite_binary_values_are_blank(self):
        self.payloads[RAW] = matrix(1, [[1, float("nan"), 25], [2, float("inf"), 25]], 3)
        self.assertEqual(self.extract(), 1)
        for row in read_csv(self.folder("experiment") / "thermogram.csv"):
            self.assertEqual(row["raw_power_µW"], "")
            self.assertEqual(row["adjusted_power_µW"], "")

    def test_unsafe_and_duplicate_paths_skipped(self):
        self.assertEqual(self.extract(extra_entries=[("../outside.txt", b"unsafe"), (RAW, self.payloads[RAW])]), 1)
        self.assertFalse((self.root / "outside.txt").exists())
        self.assertFalse((self.folder("experiment") / "thermogram.csv").exists())
        self.assertTrue((self.folder("solution") / "parameters.csv").exists())

    def test_duplicate_ids_preserved_and_reference_is_ambiguous(self):
        duplicate = copy.deepcopy(self.payloads[EXPERIMENT])
        duplicate["name"] = "duplicate"
        self.payloads["experiments/000001/experiment.json"] = duplicate
        self.assertEqual(self.extract(), 1)
        self.assertEqual(sum(row["kind"] == "experiment" for row in self.index), 2)
        self.assertIn("ambiguous experiment reference", (self.folder("solution") / "metadata.txt").read_text())

    def test_embedded_null_solution_and_unattached_solution(self):
        null = {"id": "null1", "experimentId": "e1", "modelId": "offset",
                "fittedParameters": [{"id": "offset", "value": 1000, "locked": False}]}
        self.payloads[RESULT]["nullComparison"] = {"members": [{"solution": null}]}
        self.payloads["solutions/000001/solution.json"] = {**null, "id": "orphan"}
        self.assertEqual(self.extract(), 1)  # Unreferenced component is explicitly identified.
        null_folder = self.folder("null_solution")
        self.assertEqual(float(read_csv(null_folder / "parameters.csv")[0]["value"]), 1)
        self.assertIn("#nullComparison.members[0].solution", (null_folder / "metadata.txt").read_text())
        self.assertTrue(any(row["id"] == "orphan" for row in self.index))

    def test_conflicting_parameter_centers_are_retained(self):
        self.payloads[SOLUTION]["reportedParameters"][0]["estimate"]["value"] = 6.5
        self.assertEqual(self.extract(), 1)
        rows = read_csv(self.folder("solution") / "parameters.csv")
        values = [float(row["value"]) for row in rows if row["quantity"] == "affinity-log10-1"]
        self.assertEqual(values, [6, 6.5])
        self.assertIn("centers differ", (self.folder("solution") / "metadata.txt").read_text())

    def test_kd_extreme_values_leave_blank_without_stopping(self):
        self.payloads[SOLUTION]["fittedParameters"][0]["value"] = -1000
        self.payloads[SOLUTION]["reportedParameters"][0]["estimate"] = fwe(-1000)
        self.assertEqual(self.extract(), 1)
        rows = read_csv(self.folder("solution") / "parameters.csv")
        self.assertTrue(all(row["value"] == "" for row in rows if row["quantity"] == "Kd"))

    def test_source_unchanged_and_output_collisions(self):
        write_archive(self.source, self.payloads)
        before = self.source.read_bytes()
        target = self.root / "chosen"
        target.mkdir()
        (target / "keep.txt").write_text("keep")
        for expected in ("chosen_1", "chosen_2"):
            code, output = recovery.recover(self.source, target, progress=lambda _: None)
            self.assertEqual(code, 0)
            self.assertEqual(output.name, expected)
        self.assertEqual(before, self.source.read_bytes())
        self.assertEqual((target / "keep.txt").read_text(), "keep")

    def test_archive_resource_limits(self):
        write_archive(self.source, self.payloads)
        with mock.patch.object(recovery, "MAX_ENTRIES", 2):
            with self.assertRaisesRegex(recovery.RecoveryError, "entry limit"):
                recovery.recover(self.source)
        with mock.patch.object(recovery, "MAX_PACKAGE_BYTES", 100):
            with self.assertRaisesRegex(recovery.RecoveryError, "expanded-size limit"):
                recovery.recover(self.source)
        self.payloads[RAW] = matrix(1, [[1, 2, 3]] * 500, 3)
        write_archive(self.source, self.payloads)
        with mock.patch.object(recovery, "MAX_ENTRY_BYTES", 5000):
            code, output = recovery.recover(self.source, progress=lambda _: None)
            self.assertEqual(code, 1)
            self.assertIn("Entry exceeds", (output / "recovery_log.txt").read_text())

    def test_duplicate_json_keys_do_not_choose_a_value(self):
        self.payloads[SOLUTION] = b'{"id":"s1","id":"s2"}'
        self.assertEqual(self.extract(), 1)
        self.assertFalse(any(row["kind"] == "solution" for row in self.index))

    def test_malformed_nested_collections_preserved(self):
        self.payloads[EXPERIMENT]["injections"] = [None, "bad", {"id": 2}]
        self.payloads[SOLUTION]["fittedParameters"] = "bad"
        self.payloads[RESULT]["memberSolutionIds"] = {"bad": True}
        self.assertEqual(self.extract(), 1)
        self.assertEqual(len(read_csv(self.folder("experiment") / "injections.csv")), 1)
        self.assertIn('"bad"', (self.folder("solution") / "metadata.txt").read_text())

    def test_report_and_refit_damage_is_intentionally_ignored(self):
        self.payloads["reports/000000/report.json"] = b"broken"
        self.payloads["solutions/000000/bootstrap.json"] = b"broken"
        self.assertEqual(self.extract(), 0)
        self.assertEqual(len(self.index), 3)

    def test_report_only_project_is_not_a_recovery_failure(self):
        self.payloads = {"project.json": {"experiments": [], "solutions": [], "results": [],
                                         "reports": [{"metadata": "reports/000000/report.json", "id": "report"}]},
                         "reports/000000/report.json": {"id": "report", "name": "report"}}
        self.assertEqual(self.extract(), 0)
        self.assertEqual(self.index, [])

    def test_numeric_metadata_damage_is_flagged_or_isolated(self):
        self.payloads[SOLUTION]["reportedParameters"][0]["estimate"]["standardDeviation"] = "invalid"
        self.payloads[EXPERIMENT]["injections"][0]["volume"] = {"invalid": 1}
        self.assertEqual(self.extract(), 1)
        self.assertEqual(read_csv(self.folder("experiment") / "injections.csv")[0]["volume_µL"], "")
        self.assertIn("Invalid number", (self.folder("solution") / "metadata.txt").read_text())
        self.payloads[SOLUTION] = b'{"id":"s1","value":1e9999}'
        self.assertEqual(self.extract(), 1)
        self.assertFalse(any(row["kind"] == "solution" for row in self.index))

    def test_corrupt_compressed_payload_skipped(self):
        write_archive(self.source, self.payloads)
        with zipfile.ZipFile(self.source) as archive:
            info = archive.getinfo(RAW)
            offset = info.header_offset
        blob = bytearray(self.source.read_bytes())
        name_length, extra_length = struct.unpack_from("<HH", blob, offset + 26)
        payload = offset + 30 + name_length + extra_length
        blob[payload] ^= 0xff
        self.source.write_bytes(blob)
        code, output = recovery.recover(self.source, progress=lambda _: None)
        self.assertEqual(code, 1)
        self.assertIn("Unreadable entry", (output / "recovery_log.txt").read_text())
        self.assertTrue(list(output.glob("*solution*/parameters.csv")))

    def test_unknown_advanced_quantity_not_given_known_field_units(self):
        self.payloads[RESULT]["advancedAnalyses"] = {"futureAnalysis": {"bindingEnthalpy": fwe(100)}}
        self.extract()
        row = read_csv(self.folder("result") / "advanced_parameters.csv")[0]
        self.assertEqual(float(row["value"]), 100)
        self.assertEqual(row["unit"], "unknown (saved scale)")

    def test_entry_and_package_limit_boundaries(self):
        self.payloads[RAW] = matrix(1, [[1, 2, 3]] * 500, 3)
        write_archive(self.source, self.payloads)
        with zipfile.ZipFile(self.source) as archive:
            count = len(archive.infolist())
            total = sum(info.file_size for info in archive.infolist())
            largest = max(info.file_size for info in archive.infolist())
        for setting, limit in (("MAX_ENTRIES", count), ("MAX_PACKAGE_BYTES", total), ("MAX_ENTRY_BYTES", largest)):
            with self.subTest(setting=setting), mock.patch.object(recovery, setting, limit):
                package = recovery.Package(self.source)
                package.archive.close()
        for setting, limit in (("MAX_ENTRIES", count - 1), ("MAX_PACKAGE_BYTES", total - 1)):
            with self.subTest(setting=setting), mock.patch.object(recovery, setting, limit):
                with self.assertRaises(recovery.RecoveryError):
                    recovery.Package(self.source)

    def test_missing_manifest_scans_without_checksum_claim(self):
        with zipfile.ZipFile(self.source, "w") as archive:
            for path, value in self.payloads.items():
                archive.writestr(path, value if isinstance(value, bytes) else json.dumps(value))
        code, output = recovery.recover(self.source, progress=lambda _: None)
        self.assertEqual(code, 1)
        self.assertEqual(len(read_csv(output / "index.csv")), 3)
        self.assertIn("No usable manifest declaration", (output / "recovery_log.txt").read_text())

    def test_csv_formula_text_escaped_but_original_preserved(self):
        self.payloads[EXPERIMENT]["name"] = "=SUM(1,2)"
        self.assertEqual(self.extract(), 0)
        self.assertEqual(self.index[0]["name"], "'=SUM(1,2)")
        self.assertIn('"=SUM(1,2)"', (self.folder("experiment") / "metadata.txt").read_text())

    def test_unwritable_output_has_actionable_message(self):
        write_archive(self.source, self.payloads)
        with mock.patch.object(recovery, "new_directory", side_effect=PermissionError("denied")):
            with self.assertRaisesRegex(recovery.RecoveryError, "--output with a writable directory"):
                recovery.recover(self.source)

    def test_cli_exit_codes_and_isolated_python(self):
        script = str(Path(recovery.__file__).resolve())
        write_archive(self.source, self.payloads)
        command = [sys.executable, "-I", "-S", script, str(self.source)]
        result = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("Recovery complete", result.stdout)
        del self.payloads[RAW]
        write_archive(self.source, self.payloads)
        self.assertEqual(subprocess.run(command, capture_output=True).returncode, 1)
        self.source.write_bytes(b"not a zip")
        result = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(result.returncode, 2)
        self.assertNotIn("Traceback", result.stderr)

    def test_terminal_prompt_and_cancellation(self):
        with mock.patch("builtins.input", return_value='"a b.ftxtc"'):
            self.assertEqual(recovery.prompt_for_file(), "a b.ftxtc")
        with mock.patch("builtins.input", side_effect=EOFError):
            self.assertEqual(recovery.prompt_for_file(), "")
        with mock.patch.object(recovery, "prompt_for_file", return_value=""), mock.patch("builtins.print"):
            self.assertEqual(recovery.main([]), 0)

    def test_no_argument_cli_uses_terminal_input(self):
        write_archive(self.source, self.payloads)
        command = [sys.executable, "-I", "-S", str(Path(recovery.__file__).resolve())]
        result = subprocess.run(command, input=str(self.source) + "\n", capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("Path to .ftxtc file", result.stdout)
        self.assertIn("Recovery complete", result.stdout)
        result = subprocess.run(command, input="\n", capture_output=True, text=True)
        self.assertEqual(result.returncode, 0)
        self.assertIn("nothing changed", result.stdout)


class RepositoryFixtureTests(unittest.TestCase):
    def test_existing_projects_counts_numbers_and_source_hashes(self):
        fixtures = Path(__file__).resolve().parents[2] / "AnalysisITC.Tests"
        if not fixtures.is_dir():
            self.skipTest("Repository fixtures not present alongside standalone script")
        paths = sorted(fixtures.rglob("*.ftxtc"))
        self.assertTrue(paths)
        for source in paths:
            with self.subTest(project=str(source)), tempfile.TemporaryDirectory() as temp:
                before = hashlib.sha256(source.read_bytes()).hexdigest()
                code, output = recovery.recover(source, Path(temp) / "recovered", progress=lambda _: None)
                index = read_csv(output / "index.csv")
                with zipfile.ZipFile(source) as archive:
                    project = json.loads(archive.read("project.json"))
                    for plural, kind in recovery.KINDS.items():
                        self.assertEqual(sum(row["kind"] == kind for row in index), len(project[plural]))
                    log = (output / "recovery_log.txt").read_text()
                    if source.relative_to(fixtures).as_posix() == "SolutionIdentityTracingTest/deleted.ftxtc":
                        # This fixture intentionally retains two fits of a deleted experiment.
                        self.assertEqual(code, 1, log)
                        self.assertEqual(log.count("Unresolved/ambiguous experiment reference"), 2)
                        self.assertIn("ed628781-b14e-4ae8-b1a0-848eb650638d", log)
                    else:
                        self.assertEqual(code, 0, log)
                    for row in index:
                        folder = output / row["output_directory"]
                        data = json.loads(archive.read(row["source_entry"]))
                        if row["kind"] == "experiment":
                            injections = read_csv(folder / "injections.csv")
                            self.assertEqual(len(injections), len(data["injections"]))
                            for saved, recovered in zip(data["injections"], injections):
                                self.assertAlmostEqual(float(recovered["volume_µL"]), saved["volume"] * 1000000)
                                for key, column in (("rawPeakArea", "raw_peak_area_µJ"), ("correctedPeakArea", "corrected_peak_area_µJ")):
                                    estimate = saved.get(key)
                                    if estimate and not estimate.get("isMissing"):
                                        self.assertEqual(float(recovered[column]), estimate["value"] * 1000000)
                            ref = next(e for e in project["experiments"] if e["metadata"] == row["source_entry"])
                            blob = archive.read(ref["thermogram"])
                            expected_count = struct.unpack_from("<i", blob, 8)[0]
                            traces = read_csv(folder / "thermogram.csv")
                            self.assertEqual(len(traces), expected_count)
                            if expected_count:
                                time, power, temperature = struct.unpack_from("<fff", blob, 16)
                                self.assertEqual(float(traces[0]["time_s"]), time)
                                self.assertEqual(float(traces[0]["raw_power_µW"]), power * 1000000)
                                self.assertEqual(float(traces[0]["temperature_°C"]), temperature)
                        elif row["kind"] == "solution":
                            values = read_csv(folder / "parameters.csv")
                            for parameter in data["fittedParameters"]:
                                recovered = next(p for p in values if p["source"] == "fittedParameters" and p["quantity"] == parameter["id"])
                                self.assertEqual(recovered["locked"], str(parameter["locked"]).lower())
                                # Exact inverse scaling for affinity/stoichiometry; energy scale is independently specified.
                                factor = .001 if parameter["id"].startswith(("enthalpy-", "gibbs-", "heat-capacity-", "entropy-")) or parameter["id"] == "offset" else 1
                                self.assertEqual(float(recovered["value"]), parameter["value"] * factor)
                self.assertEqual(before, hashlib.sha256(source.read_bytes()).hexdigest())


if __name__ == "__main__":
    unittest.main()
