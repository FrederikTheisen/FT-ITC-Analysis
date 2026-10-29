"""Frozen backward-compatibility contract; never regenerate fixtures in tests."""

import csv
import hashlib
import json
import re
import tempfile
import unittest
import zipfile
from pathlib import Path

import recover_ftxtc as recovery


FIXTURES = Path(__file__).resolve().parent / "fixtures"
CATALOG = FIXTURES / "compatibility.json"
# Keep this historical floor even when the current writer advances.
PUBLIC_BASELINE_VERSIONS = {(1, 2), (1, 3), (1, 4), (1, 5), (1, 6)}
IDENTITY_COLUMNS = ("kind", "id", "source_entry", "parent_source")


def read_table(path):
    with path.open(encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        return reader.fieldnames, list(reader)


def table_fingerprint(rows, columns):
    # Compare values, column identities and row order, not CSV quoting/newlines/BOM.
    # Additional columns are permitted without changing the existing contract.
    values = [[row[column] for column in columns] for row in rows]
    encoded = json.dumps(values, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def metadata_fingerprint(path):
    # Source field paths/values are part of recovery; introductory prose is not.
    content = path.read_text(encoding="utf-8")
    marker = "Saved field paths and original values/units (not lab-unit conversions):\n"
    fields = content.split(marker, 1)[1]
    return hashlib.sha256(fields.encode("utf-8")).hexdigest()


def assert_recovery_matches(test, output, expected):
    _, index = read_table(output / "index.csv")
    actual = {tuple(row[key] for key in IDENTITY_COLUMNS): row for row in index}
    test.assertEqual(len(actual), len(index), "Recovered identities must remain unambiguous")
    test.assertEqual(set(actual), {tuple(obj["identity"]) for obj in expected["objects"]})
    for obj in expected["objects"]:
        row = actual[tuple(obj["identity"])]
        for key, value in obj["index_values"].items():
            test.assertEqual(row[key], value, "%s: index.%s changed" % (obj["identity"], key))
        directory = output / row["output_directory"]
        for name, contract in obj["tables"].items():
            columns, rows = read_table(directory / name)
            test.assertTrue(set(contract["columns"]).issubset(columns), "%s lost columns" % name)
            test.assertEqual(len(rows), contract["rows"], "%s row count changed" % name)
            test.assertEqual(table_fingerprint(rows, contract["columns"]), contract["values_sha256"],
                             "%s: %s saved values changed" % (obj["identity"], name))
        test.assertEqual(metadata_fingerprint(directory / "metadata.txt"), obj["metadata_sha256"],
                         "%s: saved metadata changed" % obj["identity"])


class FrozenCompatibilityTests(unittest.TestCase):
    def setUp(self):
        # Missing corpus is a failure, not a silently skipped compatibility check.
        self.catalog = json.loads(CATALOG.read_text(encoding="utf-8"))

    def test_supported_versions_have_frozen_cases_and_keep_public_history(self):
        versions = [tuple(case["schema"]) for case in self.catalog["cases"]]
        files = [case["file"] for case in self.catalog["cases"]]
        self.assertEqual(len(files), len(set(files)))
        self.assertEqual(set(versions), recovery.SUPPORTED_SCHEMAS,
                         "Add a frozen archive and reviewed expected recovery for each new supported schema.")
        self.assertTrue(PUBLIC_BASELINE_VERSIONS.issubset(recovery.SUPPORTED_SCHEMAS),
                        "Do not drop recovery support for earlier public FTXTC versions.")
        self.assertEqual({case["file"] for case in self.catalog["cases"]},
                         {path.name for path in FIXTURES.glob("*.ftxtc")})

    def test_frozen_archives_unchanged_and_genuinely_versioned(self):
        for case in self.catalog["cases"]:
            with self.subTest(schema=case["schema"]):
                source = FIXTURES / case["file"]
                self.assertEqual(hashlib.sha256(source.read_bytes()).hexdigest(), case["source_sha256"],
                                 "Historical fixture changed. Preserve it; add a separate case instead.")
                with zipfile.ZipFile(source) as archive:
                    manifest = json.loads(archive.read("manifest.json"))
                    self.assertEqual([manifest["schemaMajor"], manifest["schemaMinor"]], case["schema"])
                    project = json.loads(archive.read(manifest["root"]))
                    self.assertEqual(project["projectSchemaVersion"], case["project_schema"])
                    # Historical root layouts must stay historical, not modern data relabelled.
                    if tuple(case["schema"]) <= (1, 4):
                        self.assertNotIn("contentOrder", project)
                    if tuple(case["schema"]) <= (1, 5):
                        self.assertNotIn("reports", project)

    def test_all_frozen_recoveries_preserve_saved_outputs(self):
        for case in self.catalog["cases"]:
            with self.subTest(schema=case["schema"]), tempfile.TemporaryDirectory() as temp:
                source = FIXTURES / case["file"]
                code, output = recovery.recover(source, Path(temp) / "recovered", progress=lambda _: None)
                self.assertEqual(code, case["exit_code"], (output / "recovery_log.txt").read_text())
                assert_recovery_matches(self, output, case)
                self.assertEqual(hashlib.sha256(source.read_bytes()).hexdigest(), case["source_sha256"])

    def test_regression_check_detects_changed_scientific_values(self):
        case = next(case for case in self.catalog["cases"] if case["schema"] == [1, 5])
        with tempfile.TemporaryDirectory() as temp:
            _, output = recovery.recover(FIXTURES / case["file"], Path(temp) / "out", progress=lambda _: None)
            assert_recovery_matches(self, output, case)
            _, index = read_table(output / "index.csv")
            directory = output / next(row["output_directory"] for row in index if row["kind"] == "experiment")
            columns, rows = read_table(directory / "thermogram.csv")
            rows[0]["raw_power_µW"] = "123.0"
            with (directory / "thermogram.csv").open("w", encoding="utf-8", newline="") as stream:
                writer = csv.DictWriter(stream, fieldnames=columns)
                writer.writeheader()
                writer.writerows(rows)
            with self.assertRaisesRegex(AssertionError, "saved values changed"):
                assert_recovery_matches(self, output, case)

    def test_current_writer_schema_is_explicitly_supported_and_frozen(self):
        repository = Path(__file__).resolve().parents[2]
        writer = repository / "AnalysisITC.Core/DataExport/FTXTCFormat.cs"
        if not writer.is_file():
            self.skipTest("Owning repository unavailable; standalone frozen compatibility checks still run")
        source = writer.read_text(encoding="utf-8-sig")
        values = []
        for key in ("SchemaMajor", "SchemaMinor", "ProjectSchemaVersion"):
            match = re.search(r"\bconst int " + key + r"\s*=\s*(\d+)\s*;", source)
            self.assertIsNotNone(match, "Cannot read writer version; update the compatibility guard explicitly.")
            values.append(int(match[1]))
        current = tuple(values[:2])
        self.assertIn(current, recovery.SUPPORTED_SCHEMAS,
                      "FTXTC writer advanced: update the recovery script and add a frozen fixture; keep all older cases.")
        case = next(case for case in self.catalog["cases"] if tuple(case["schema"]) == current)
        self.assertEqual(case["project_schema"], values[2], "Project schema changed; review recovery compatibility.")
        manifest = json.loads((repository / "Documentation/Schemas/FTXTC/manifest.schema.json").read_text())
        self.assertEqual(current, tuple(manifest["properties"][key]["const"] for key in ("schemaMajor", "schemaMinor")))


if __name__ == "__main__":
    unittest.main()
