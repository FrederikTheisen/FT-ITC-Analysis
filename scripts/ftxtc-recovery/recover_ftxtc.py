#!/usr/bin/env python3
"""Offline FTXTC 1.2–1.6 recovery. Python 3.9+, standard library only.

Run: python3 recover_ftxtc.py project.ftxtc [--output recovered_directory]
Without a path, enter it at the terminal prompt. See README.md alongside
this file, or --help. The original project and existing outputs are never changed.
Exit status: 0 complete, 1 partial, 2 failure (including unsupported format).
"""

import argparse
import collections
import csv
import hashlib
import itertools
import json
import math
import re
import stat
import struct
import sys
import tempfile
import zipfile
import zlib
from pathlib import Path


MAX_ENTRIES = 10000
MAX_ENTRY_BYTES = 512 * 1024 * 1024
MAX_PACKAGE_BYTES = 2 * 1024 * 1024 * 1024
# Add support deliberately; the frozen compatibility tests must cover every entry.
# Never derive this set from the application's current writer version at runtime.
SUPPORTED_SCHEMAS = frozenset({(1, 2), (1, 3), (1, 4), (1, 5), (1, 6)})
COMPONENT = re.compile(r"^(experiments|solutions|results)/[^/]+/(experiment|solution|result)\.json$")
KINDS = {"experiments": "experiment", "solutions": "solution", "results": "result"}
EXCLUDED = {"globalReplicates", "bootstrap", "bootstrapSolutions"}
ESTIMATE_FIELDS = ("value", "standardDeviation", "lower95", "upper95")
PARAMETER_COLUMNS = ["source", "parameter_id", "quantity", "value", "standard_deviation",
                     "lower95", "upper95", "unit", "locked", "is_missing", "notes"]
INDEX_COLUMNS = ["kind", "id", "name", "source_entry", "output_directory", "status",
                 "experiment_id", "member_solution_ids", "attached_solution_id", "parent_source", "issues"]

OUTPUT_README = """FTXTC recovered data
====================
Start with index.csv. Each numbered folder represents a saved object; stable IDs
and source entries distinguish objects with identical names. Read recovery_log.txt
and each metadata.txt header before using data. 'recovered' means extraction
succeeded, not that a saved scientific fit was valid. Saved isValid and uncertainty
diagnostics retain their original meaning; no analysis has been rerun.

CSV files are UTF-8 with a BOM, comma-separated, with a decimal point. Import them
as UTF-8 CSV if your spreadsheet uses another separator. Numerical values use
full Python floating-point precision, not display rounding. Blank means unavailable;
zero remains zero. Text cells that could be spreadsheet formulas are prefixed
with an apostrophe. Original strings remain in metadata.txt (escaped for clarity).

Thermograms: seconds, µW, °C. Baseline uncertainties describe the saved baseline,
not a newly estimated error on adjusted power. Adjusted power reproduces the
application's Float32 baseline subtraction and requires a completed baseline of
matching length. Unaligned or standalone baselines are in baseline.csv, indexed
by sample without invented times. Empty raw traces are normal for integrated-only
experiments. No baseline fitting, integration or buffer correction is performed.

Injections: seconds, µL, µM, °C, µJ. raw_peak_area is the saved integrated heat
before buffer subtraction, not integration of the unadjusted power trace.
corrected_peak_area is the separately saved corrected heat. Missing corrected
heat is not replaced with raw heat. All inclusion and integration states survive.

Parameters: energy in kJ/mol; heat capacity and entropy in kJ/(mol K); Kd in µM.
Original affinity coordinates are log10(Ka / (1/M)). Kd = 10^(-coordinate) M;
converted endpoints reverse order. No symmetric Kd SD is invented. Fitted and
reported rows are kept separately, including any discrepancy. Profile-likelihood
SDs may be equivalent scales, not Gaussian sampling SDs; consult saved calibration
and side outcomes. Bounds are exported only where recorded in profile metadata.
constraints.csv describes saved sharing rules; ordinary optimizer bounds are not
generally stored. Unknown parameter/advanced-analysis units remain unspecified.

metadata.txt preserves saved field paths and original values/units, including
unfamiliar fields. null means explicitly null; absent fields were not recorded.
Small convenience tables do not replace this complete non-refit metadata.
Stored advanced outputs are recovered as saved, including historical methods.
This tool does not migrate their scientific interpretation or recalculate them.

Reports and individual bootstrap/leave-one-out refits are intentionally excluded.
Their payloads are not integrity-checked. Broken ZIP-directory reconstruction and
truncated binary-table repair are not attempted. Keep the original project: this
recovery folder is not a replacement project or a complete archive of its contents.
"""


class RecoveryError(Exception):
    """An anticipated input/output error suitable for a short user message."""


def safe_path(value):
    return (isinstance(value, str) and bool(value) and not any(
        ord(c) < 32 or c in "\\:" for c in value)
        and all(part not in ("", ".", "..") for part in value.split("/")))


def number(value):
    try:
        return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)
    except OverflowError:
        return False


def finite_json_float(token):
    value = float(token)
    if not math.isfinite(value):
        raise ValueError("JSON number outside floating-point range")
    return value


def scaled(value, factor=1):
    if not number(value):
        return None
    result = value * factor
    return result if number(result) else None


def f32(value):
    return struct.unpack("<f", struct.pack("<f", value))[0]


def csv_cell(value):
    if value is None:
        return ""
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, str) and value.lstrip().startswith(("=", "+", "-", "@")):
        return "'" + value
    return value


def write_csv(path, columns, rows):
    with path.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=columns, extrasaction="ignore")
        writer.writeheader()
        for row in rows:
            writer.writerow({key: csv_cell(row.get(key)) for key in columns})


def flatten(value, prefix=""):
    """Readable, unambiguous field paths, preserving unknown fields and nulls."""
    if isinstance(value, dict) and value:
        for key, item in value.items():
            path = prefix + "[" + json.dumps(key, ensure_ascii=False) + "]"
            if key in EXCLUDED:
                yield path, "<individual refits intentionally excluded>"
            else:
                yield from flatten(item, path)
    elif isinstance(value, list) and value:
        for i, item in enumerate(value):
            yield from flatten(item, "%s[%d]" % (prefix, i))
    else:
        yield prefix, json.dumps(value, ensure_ascii=False, allow_nan=False)


def reject_duplicates(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate JSON property: " + key)
        result[key] = value
    return result


class Package:
    """Bounded per-entry reader. Large binary payloads spill to temporary disk."""

    def __init__(self, path):
        self.archive = zipfile.ZipFile(path)
        self.issues = []
        self._seen_issues = set()
        self.entries = {}
        self.declarations = {}
        self.manifest_ok = False
        self.root_ok = False
        self.read_paths = set()
        self.expanded = 0
        self.version = "unverifiable"
        infos = self.archive.infolist()
        if len(infos) > MAX_ENTRIES:
            self.archive.close()
            raise RecoveryError("ZIP exceeds the 10,000-entry limit.")
        counts = collections.Counter(info.filename for info in infos)
        total = 0
        for info in infos:
            name = info.filename
            if info.is_dir():
                continue
            if (not safe_path(name) or counts[name] != 1
                    or stat.S_ISLNK(info.external_attr >> 16)):
                self.warn(name, "Unsafe, duplicated, or symlink archive entry skipped.")
                continue
            total += info.file_size
            if total > MAX_PACKAGE_BYTES:
                self.archive.close()
                raise RecoveryError("ZIP exceeds the 2 GiB expanded-size limit.")
            if info.file_size > MAX_ENTRY_BYTES:
                self.warn(name, "Entry exceeds the 512 MiB expanded-size limit; skipped.")
                continue
            self.entries[name] = info

    def warn(self, path, message):
        pair = (str(path), message)
        if pair not in self._seen_issues:
            self._seen_issues.add(pair)
            self.issues.append(pair)

    def read(self, path):
        if not safe_path(path) or path not in self.entries:
            self.warn(str(path), "Entry missing, unsafe, or ambiguous; skipped.")
            return None
        info = self.entries[path]
        spool = tempfile.SpooledTemporaryFile(max_size=2 * 1024 * 1024)
        size = 0
        digest = hashlib.sha256()
        charge = path not in self.read_paths
        try:
            with self.archive.open(info) as source:
                while True:
                    chunk = source.read(min(1024 * 1024, MAX_ENTRY_BYTES - size + 1))
                    if not chunk:
                        break
                    size += len(chunk)
                    if size > MAX_ENTRY_BYTES or size > info.file_size:
                        raise ValueError("expanded entry length exceeds its limit/declaration")
                    if charge:
                        self.expanded += len(chunk)
                        if self.expanded > MAX_PACKAGE_BYTES:
                            raise RecoveryError("Actual expanded reads exceed the 2 GiB budget.")
                    digest.update(chunk)
                    spool.write(chunk)
            if size != info.file_size:
                raise ValueError("expanded entry length differs from ZIP declaration")
            self.read_paths.add(path)
            if path != "manifest.json":
                declared = self.declarations.get(path)
                if declared is None:
                    self.warn(path, "No usable manifest declaration; integrity unverified.")
                elif size != declared.get("length") or digest.hexdigest() != declared.get("sha256"):
                    self.warn(path, "Manifest length/checksum mismatch; readable content is UNVERIFIED.")
            spool.seek(0)
            return spool
        except (ValueError, OSError, EOFError, RuntimeError, NotImplementedError, zipfile.BadZipFile, zlib.error) as exc:
            spool.close()
            self.warn(path, "Unreadable entry: %s" % exc)
            return None
        except BaseException:
            spool.close()
            raise

    def json(self, path):
        stream = self.read(path)
        if stream is None:
            return None
        try:
            with stream:
                value = json.load(stream, object_pairs_hook=reject_duplicates,
                                  parse_float=finite_json_float,
                                  parse_constant=lambda token: (_ for _ in ()).throw(
                                      ValueError("nonfinite JSON number " + token)))
            if not isinstance(value, dict):
                raise ValueError("expected a JSON object")
            return value
        except (ValueError, UnicodeError, RecursionError) as exc:
            self.warn(path, "Invalid JSON: %s" % exc)
            return None

    def load_manifest(self):
        manifest = self.json("manifest.json")
        if manifest is None:
            return "project.json"
        major, minor = manifest.get("schemaMajor"), manifest.get("schemaMinor")
        if manifest.get("format") not in (None, "ftxtc"):
            raise RecoveryError("This archive declares a format other than ftxtc.")
        if type(major) is int and type(minor) is int:
            if (major, minor) not in SUPPORTED_SCHEMAS:
                supported = ", ".join("%d.%d" % version for version in sorted(SUPPORTED_SCHEMAS))
                raise RecoveryError("Unsupported FTXTC schema %s.%s; supported schemas are %s." % (major, minor, supported))
            self.version = "%s.%s" % (major, minor)
            self.manifest_ok = manifest.get("format") == "ftxtc"
        if not self.manifest_ok:
            self.warn("manifest.json", "Format/version metadata incomplete; version integrity unverifiable.")
        entries = manifest.get("entries")
        if not isinstance(entries, list):
            self.warn("manifest.json", "Missing/malformed payload declarations; integrity unverified.")
        else:
            duplicates = set()
            for item in entries:
                if not isinstance(item, dict) or not safe_path(item.get("path")):
                    self.warn("manifest.json", "Malformed payload declaration ignored.")
                    continue
                path = item["path"]
                if path in self.declarations:
                    duplicates.add(path)
                self.declarations[path] = item
            for path in duplicates:
                del self.declarations[path]
                self.warn(path, "Duplicate manifest declarations; integrity unverified.")
        root = manifest.get("root", "project.json")
        if not safe_path(root):
            self.warn("manifest.json", "Unsafe root reference; scanning standard component paths.")
            return "project.json"
        return root

    def matrix(self, path, scalar, columns):
        if not path:
            return None
        stream = self.read(path)
        if stream is None:
            return None
        try:
            header = stream.read(16)
            if len(header) != 16:
                raise ValueError("truncated FTXB header")
            magic, version, kind, layout, reserved, rows, cols = struct.unpack("<4sBBBBii", header)
            if (magic, version, kind, layout, reserved) != (b"FTXB", 1, scalar, 1, 0):
                raise ValueError("unsupported/invalid FTXB header")
            if rows < 0 or cols != columns:
                raise ValueError("invalid FTXB dimensions")
            row_struct = struct.Struct("<" + ("f" if scalar == 1 else "d") * cols)
            stream.seek(0, 2)
            if stream.tell() != 16 + rows * row_struct.size:
                raise ValueError("invalid FTXB payload length")
            stream.seek(16)
            return Matrix(stream, rows, row_struct)
        except ValueError as exc:
            stream.close()
            self.warn(path, str(exc) + "; table skipped.")
            return None


class Matrix:
    def __init__(self, stream, count, row_struct):
        self.stream, self.count, self.row_struct = stream, count, row_struct

    def __iter__(self):
        self.stream.seek(16)
        for _ in range(self.count):
            yield self.row_struct.unpack(self.stream.read(self.row_struct.size))

    def close(self):
        self.stream.close()


def objects(package, root_path):
    """Retain recognizable orphans as well as root-declared components."""
    project = package.json(root_path)
    package.root_ok = project is not None
    declarations = {}
    if project is not None:
        for plural, kind in KINDS.items():
            collection = project.get(plural)
            if not isinstance(collection, list):
                package.warn(root_path, "Missing/malformed %s collection; scanning component paths." % plural)
                package.root_ok = False
                continue
            for ref in collection:
                if not isinstance(ref, dict) or not safe_path(ref.get("metadata")):
                    package.warn(root_path, "Invalid %s reference ignored." % kind)
                    continue
                path = ref["metadata"]
                if path in declarations:
                    package.warn(path, "Repeated root reference; identity/relationships are ambiguous.")
                else:
                    declarations[path] = (kind, ref)
    for path in sorted(package.entries):
        match = COMPONENT.fullmatch(path)
        if match and KINDS[match[1]] == match[2] and path not in declarations:
            declarations[path] = (match[2], {})
            package.warn(path, "Not referenced by a usable project root; recovered by path, relationships unverified.")
        if re.fullmatch(r"experiments/[^/]+/(thermogram|baseline)\.ftxb", path):
            metadata_path = path.rsplit("/", 1)[0] + "/experiment.json"
            if metadata_path not in declarations and metadata_path not in package.entries:
                declarations[metadata_path] = ("experiment", {})
                package.warn(metadata_path, "Orphaned experiment tables found by path; metadata unavailable.")
    records = []
    for path, (kind, ref) in declarations.items():
        data = package.json(path)
        available = data is not None
        if data is None:
            matrix_paths = [ref.get(key) or path.rsplit("/", 1)[0] + "/" + key + ".ftxb"
                            for key in ("thermogram", "baseline")]
            if kind != "experiment" or not any(isinstance(p, str) and p in package.entries for p in matrix_paths):
                continue
            data = {}
            package.warn(path, "Experiment metadata unavailable; recovering tables only. No injections or completed-baseline state inferred.")
        if kind != "experiment" and data.get("schemaVersion", 1) != 1:
            package.warn(path, "Unknown component schema; saved fields retained without version-specific interpretation.")
        if available and ref.get("id") and ref["id"] != data.get("id"):
            package.warn(path, "Root ID differs from component ID; relationships unverified.")
        if not isinstance(data.get("id"), str) or not data["id"]:
            package.warn(path, "Missing/invalid object ID; relationships unverified.")
        records.append({"kind": kind, "path": path, "data": data, "ref": ref, "parent": "", "metadata_available": available})
    # Null comparison snapshots are saved ordinary solutions, not refits.
    for record in list(records):
        comparison = record["data"].get("nullComparison")
        if not isinstance(comparison, dict):
            continue
        members = comparison.get("members", [])
        if not isinstance(members, list):
            package.warn(record["path"], "Malformed Null comparison members retained in metadata only.")
            continue
        for index, member in enumerate(members):
            if isinstance(member, dict) and isinstance(member.get("solution"), dict):
                records.append({"kind": "null_solution", "path": record["path"],
                                "data": member["solution"], "ref": {},
                                "parent": record["path"] + "#nullComparison.members[%d].solution" % index})
    return project, records


def validate_relationships(package, records):
    by_id = collections.defaultdict(list)
    for record in records:
        identifier = record_id(record)
        if isinstance(identifier, str) and identifier and not record["parent"]:
            by_id[identifier].append(record)
    for identifier, matches in by_id.items():
        if len(matches) > 1:
            for record in matches:
                package.warn(record["path"], "Duplicate object ID %s; kept separately, references ambiguous." % identifier)
    for record in records:
        data, path = record["data"], record["path"]
        refs = []
        if record["kind"] in ("solution", "null_solution"):
            refs.append((data.get("experimentId"), "experiment"))
        elif record["kind"] == "experiment" and data.get("attachedSolutionId"):
            refs.append((data["attachedSolutionId"], "solution"))
        elif record["kind"] == "result":
            members = data.get("memberSolutionIds", [])
            if not isinstance(members, list):
                package.warn(path, "Malformed result membership; preserved as metadata.")
            else:
                refs.extend((member, "solution") for member in members)
        for identifier, kind in refs:
            matches = by_id.get(identifier, []) if isinstance(identifier, str) else []
            if len(matches) != 1 or matches[0]["kind"] != kind:
                package.warn(path, "Unresolved/ambiguous %s reference %r; no substitute selected." % (kind, identifier))


def record_id(record):
    # A root-declared ID remains usable when the component JSON was unreadable.
    # The source of that ID is explicitly disclosed in metadata.txt.
    if record.get("metadata_available") is False:
        return record["ref"].get("id")
    return record["data"].get("id")


def estimate(value, factor=1):
    if not isinstance(value, dict):
        return [None] * 4 + [None]
    missing = value.get("isMissing")
    values = [None] * 4 if missing is True else [scaled(value.get(key), factor) for key in ESTIMATE_FIELDS]
    return values + [missing]


def check_estimate(value, package, path, label):
    if value is None:
        return
    if not isinstance(value, dict):
        package.warn(path, "Malformed %s estimate; retained in metadata, unavailable in CSV." % label)
        return
    if "isMissing" in value and not isinstance(value["isMissing"], bool):
        package.warn(path, "%s has an invalid missing-value flag; values are unverified." % label)
    if value.get("isMissing") is not True:
        for key in ESTIMATE_FIELDS:
            if value.get(key) is not None and not number(value[key]):
                package.warn(path, "Invalid number in %s.%s; left blank in CSV." % (label, key))
        if number(value.get("standardDeviation")) and value["standardDeviation"] < 0:
            package.warn(path, "%s contains a negative saved SD; preserved as unverified." % label)


def unit_for_parameter(identifier):
    if not isinstance(identifier, str):
        return "unknown (saved scale)", 1
    if identifier.startswith(("affinity-log10-", "apparent-affinity-log10")):
        return "log10(Ka / (1/M))", 1
    if identifier.startswith(("enthalpy-", "gibbs-", "entropy-contribution-")) or identifier == "offset":
        return "kJ/mol", .001
    if identifier.startswith(("heat-capacity-", "entropy-")):
        return "kJ/(mol K)", .001
    if identifier.startswith("stoichiometry-") or identifier == "isomerization-equilibrium-constant":
        return "dimensionless", 1
    if identifier == "cis-isomer-population-percent":
        return "%", 1
    return "unknown (saved scale)", 1


def typed_rows(data, key, package, path):
    items = data.get(key, [])
    if not isinstance(items, list):
        package.warn(path, "Malformed %s; preserved in metadata only." % key)
        return
    for item in items:
        if isinstance(item, dict):
            yield item
        else:
            package.warn(path, "Malformed row in %s; preserved in metadata only." % key)


def parameter_rows(data, package, path):
    fitted = {}
    for key in ("fittedParameters", "globalParameters", "reportedParameters"):
        for item in typed_rows(data, key, package, path):
            identifier = item.get("id")
            unit, factor = unit_for_parameter(identifier)
            reported = key == "reportedParameters"
            if reported:
                check_estimate(item.get("estimate"), package, path, str(identifier))
            elif item.get("value") is not None and not number(item["value"]):
                package.warn(path, "Invalid number in %s; parameter value left blank in CSV." % identifier)
            values = estimate(item.get("estimate"), factor) if reported else [scaled(item.get("value"), factor), None, None, None, None]
            notes = ""
            if not reported and isinstance(identifier, str):
                fitted[identifier] = item.get("value")
            if reported and isinstance(identifier, str) and identifier in fitted:
                original = item.get("estimate")
                if isinstance(original, dict) and original.get("isMissing") is not True and original.get("value") != fitted[identifier]:
                    notes = "Saved fitted and reported centers differ; both retained."
                    package.warn(path, "%s: %s" % (identifier, notes))
            row = dict(zip(PARAMETER_COLUMNS, [key, identifier, identifier, *values[:4], unit,
                                              item.get("locked"), values[4], notes]))
            yield row
            if unit == "log10(Ka / (1/M))":
                def kd(value):
                    if not number(value):
                        return None
                    try:
                        result = 10.0 ** (-value) * 1e6
                        if not math.isfinite(result) or result <= 0:
                            raise OverflowError()
                        return result
                    except OverflowError:
                        package.warn(path, "%s Kd conversion outside floating-point range; left blank." % identifier)
                        return None
                yield {**row, "quantity": "Kd", "unit": "µM", "value": kd(values[0]),
                       "standard_deviation": None, "lower95": kd(values[3]), "upper95": kd(values[2]),
                       "notes": (notes + " Converted from saved affinity; no symmetric Kd SD inferred.").strip()}


INJECTION_FIELDS = {
    "id": ("injection_id", None), "included": ("included", None), "isIntegrated": ("is_integrated", None),
    "time": ("time_s", 1), "volume": ("volume_µL", 1e6), "delay": ("delay_s", 1),
    "duration": ("duration_s", 1), "filter": ("filter_saved_value", 1), "temperature": ("temperature_°C", 1),
    "integrationStartDelay": ("integration_start_delay_s", 1), "integrationEndOffset": ("integration_end_offset_s", 1),
    "actualCellConcentration": ("actual_cell_concentration_µM", 1e6),
    "actualTitrantConcentration": ("actual_titrant_concentration_µM", 1e6),
    "ratio": ("ratio", 1), "heatDirection": ("heat_direction", None),
}
HEAT_COLUMNS = [prefix + suffix for prefix in ("raw_peak_area", "corrected_peak_area")
                for suffix in ("_µJ", "_sd_µJ", "_lower95_µJ", "_upper95_µJ", "_is_missing")]
TRACE_COLUMNS = ["sample_index", "time_s", "raw_power_µW", "temperature_°C", "baseline_µW",
                 "baseline_sd_µW", "baseline_lower95_µW", "baseline_upper95_µW", "adjusted_power_µW"]


def export_experiment(record, directory, package):
    data, path, ref = record["data"], record["path"], record["ref"]
    def injections():
        for item in typed_rows(data, "injections", package, path):
            for key, (_, factor) in INJECTION_FIELDS.items():
                if factor is not None and item.get(key) is not None and not number(item[key]):
                    package.warn(path, "Invalid number in injections.%s; left blank in CSV." % key)
            for key in ("rawPeakArea", "correctedPeakArea"):
                check_estimate(item.get(key), package, path, key)
            row = {column: item.get(key) if factor is None else scaled(item.get(key), factor)
                   for key, (column, factor) in INJECTION_FIELDS.items()}
            row.update(zip(HEAT_COLUMNS, estimate(item.get("rawPeakArea"), 1e6) + estimate(item.get("correctedPeakArea"), 1e6)))
            yield row
    write_csv(directory / "injections.csv", [column for column, _ in INJECTION_FIELDS.values()] + HEAT_COLUMNS, injections())
    def matrix_path(key):
        declared = ref.get(key)
        if declared:
            if not safe_path(declared):
                package.warn(path, "Invalid %s matrix reference; table unavailable." % key)
                return None
            return declared
        guessed = path.rsplit("/", 1)[0] + "/" + key + ".ftxb"
        if guessed in package.entries:
            if ref:
                package.warn(path, "Unreferenced %s matrix recovered by conventional path." % key)
            return guessed
        return None
    raw_path, baseline_path = matrix_path("thermogram"), matrix_path("baseline")
    raw, baseline = None, None
    try:
        raw = package.matrix(raw_path, 1, 3)
        baseline = package.matrix(baseline_path, 2, 4)
        record["dependencies"] = [p for p in (raw_path, baseline_path) if isinstance(p, str)]
        aligned = raw is not None and baseline is not None and raw.count == baseline.count
        processor = data.get("processor")
        completed = isinstance(processor, dict) and processor.get("baselineCompleted") is True
        if completed and not aligned:
            package.warn(path, "Completed baseline missing or not aligned with raw trace; adjusted power unavailable.")
        elif raw is not None and baseline is not None and baseline.count and not aligned:
            package.warn(path, "Baseline and raw sample counts differ; baseline exported separately.")
        def finite(value):
            if not number(value):
                package.warn(path, "Nonfinite trace/baseline values left blank in CSV.")
                return None
            return value
        def trace_rows():
            base_rows = iter(baseline) if aligned else itertools.repeat((None,) * 4)
            for index, (point, base) in enumerate(zip(raw, base_rows)):
                point = tuple(finite(v) for v in point)
                base = tuple(finite(v) for v in base) if aligned else base
                adjusted = None
                if completed and aligned and point[1] is not None and base[0] is not None:
                    try:
                        adjusted = finite(f32(point[1] - f32(base[0])))
                    except OverflowError:
                        package.warn(path, "Adjusted power outside Float32 range; left blank.")
                yield dict(zip(TRACE_COLUMNS, [index, point[0], scaled(point[1], 1e6), point[2],
                                                *(scaled(v, 1e6) for v in base), scaled(adjusted, 1e6)]))
        if raw is not None:
            write_csv(directory / "thermogram.csv", TRACE_COLUMNS, trace_rows())
        if baseline is not None and not aligned:
            cols = ["sample_index"] + TRACE_COLUMNS[4:8]
            write_csv(directory / "baseline.csv", cols,
                      (dict(zip(cols, [i, *(scaled(finite(v), 1e6) for v in row)])) for i, row in enumerate(baseline)))
    finally:
        for matrix in (raw, baseline):
            if matrix is not None:
                matrix.close()


def advanced_rows(value, package, path, prefix=""):
    if not isinstance(value, dict):
        return
    if "value" in value or "isMissing" in value:
        check_estimate(value, package, path, prefix)
        key = prefix.rsplit(".", 1)[-1]
        # Only mappings whose saved meaning is known; leave unfamiliar quantities untouched.
        unit, factor = {"spolarRecord.hydrationEntropy": ("kJ/(mol K)", .001),
                        "spolarRecord.conformationalEntropy": ("kJ/(mol K)", .001),
                        "spolarRecord.residueEstimate": ("residues", 1),
                        "spolarRecord.referenceTemperature": ("°C", 1),
                        "protonation.bindingEnthalpy": ("kJ/mol", .001),
                        "protonation.protonationChange": ("dimensionless", 1),
                        "electrostatics.ionicStrengthFit.kd0": ("µM", 1e6)}.get(prefix, ("unknown (saved scale)", 1))
        values = estimate(value, factor)
        yield dict(zip(PARAMETER_COLUMNS, ["advancedAnalyses", prefix, key, *values[:4], unit, None, values[4], "Saved output; no scientific migration applied."]))
    else:
        for key, item in value.items():
            yield from advanced_rows(item, package, path, prefix + "." + key if prefix else key)


def export_analysis(record, directory, package):
    data, path = record["data"], record["path"]
    write_csv(directory / "parameters.csv", PARAMETER_COLUMNS, parameter_rows(data, package, path))
    constraints = list(typed_rows(data, "constraints", package, path))
    if "constraints" in data:
        write_csv(directory / "constraints.csv", ["parameterId", "constraint"], constraints)
    profile = data.get("profile")
    if isinstance(profile, dict):
        def coordinates():
            for item in typed_rows(profile, "coordinates", package, path):
                unit, factor = unit_for_parameter(item.get("parameterId"))
                row = {k: item.get(k) for k in ("parameterId", "scope", "experimentId", "index")}
                row["unit"] = unit
                row.update({k: scaled(item.get(k), factor) for k in ("bestValue", "lowerBound", "upperBound")})
                for side in ("lower", "upper"):
                    state = item.get(side)
                    if isinstance(state, dict):
                        row[side + "_outcome"] = state.get("outcome")
                        row[side + "_endpoint"] = scaled(state.get("endpoint"), factor)
                yield row
        write_csv(directory / "profile_bounds.csv", ["parameterId", "scope", "experimentId", "index", "unit",
                  "bestValue", "lowerBound", "upperBound", "lower_outcome", "lower_endpoint", "upper_outcome", "upper_endpoint"], coordinates())
    if isinstance(data.get("advancedAnalyses"), dict):
        write_csv(directory / "advanced_parameters.csv", PARAMETER_COLUMNS, advanced_rows(data["advancedAnalyses"], package, path))


def new_directory(base):
    for suffix in itertools.count():
        candidate = base if suffix == 0 else base.with_name(base.name + "_%d" % suffix)
        try:
            candidate.mkdir(parents=True, exist_ok=False)
            return candidate
        except FileExistsError:
            continue


def object_directory(record, ordinal):
    name = record["data"].get("name") or record_id(record) or "unnamed"
    # Numeric prefix guarantees uniqueness, including case-insensitive file systems.
    slug = re.sub(r"[^\w.-]+", "_", str(name), flags=re.UNICODE).strip("._")[:60] or "unnamed"
    while len(slug.encode("utf-8")) > 160:
        slug = slug[:-1]
    return "%04d_%s_%s" % (ordinal, record["kind"], slug)


def recover(source, output=None, progress=print):
    """Return (exit_code, output_path); raises RecoveryError for fatal input errors."""
    source = Path(source).expanduser().resolve()
    try:
        package = Package(source)
    except (OSError, zipfile.BadZipFile) as exc:
        raise RecoveryError("Cannot open project ZIP: %s" % exc) from exc
    destination = None
    try:
        root_path = package.load_manifest()
        project, records = objects(package, root_path)
        if not records and (not package.root_ok or package.issues):
            raise RecoveryError("No readable experiments or analysis components found. Reports are intentionally excluded. "
                                + "; ".join("%s: %s" % issue for issue in package.issues[:3]))
        validate_relationships(package, records)
        base = Path(output).expanduser().absolute() if output else source.with_name(source.stem + "_recovered")
        destination = new_directory(base)
        (destination / "README.txt").write_text(OUTPUT_README, encoding="utf-8")
        progress("Recovering %d objects to %s" % (len(records), destination))
        for i, record in enumerate(records, 1):
            directory = destination / object_directory(record, i)
            directory.mkdir()
            record["directory"] = directory.name
            progress("[%d/%d] %s" % (i, len(records), directory.name))
            if record["kind"] == "experiment":
                export_experiment(record, directory, package)
            else:
                export_analysis(record, directory, package)
        if project is not None:
            # Project structure only; reports are intentionally not recovered.
            with (destination / "project_metadata.txt").open("w", encoding="utf-8") as stream:
                stream.write("Saved project root (report references excluded). Original units/values.\n")
                for path, value in flatten({k: v for k, v in project.items() if k != "reports"}):
                    stream.write(path + " = " + value + "\n")
        root_issues = [message for path, message in package.issues if path in ("manifest.json", root_path)]
        if not package.manifest_ok or not package.root_ok:
            root_issues.append("Package version/root relationships could not be fully verified.")
        index = []
        for record in records:
            data = record["data"]
            paths = [record["path"]] + record.get("dependencies", [])
            issues = root_issues + ["%s: %s" % (p, msg) for p, msg in package.issues if p in paths]
            status = "unverified/partial" if issues else "recovered"
            with (destination / record["directory"] / "metadata.txt").open("w", encoding="utf-8") as stream:
                stream.write("Source entry: %s\nEmbedded location: %s\nSchema: %s\nRecovery status: %s\n" %
                             (record["path"], record["parent"] or "not embedded", package.version, status))
                for issue in issues:
                    stream.write("WARNING: " + issue + "\n")
                if record.get("metadata_available") is False:
                    stream.write("Component JSON unavailable. Any ID in index.csv comes only from the saved root reference.\n")
                if record["ref"]:
                    stream.write("\nSaved root reference:\n")
                    for path, value in flatten(record["ref"]):
                        stream.write(path + " = " + value + "\n")
                stream.write("\nSaved field paths and original values/units (not lab-unit conversions):\n")
                for path, value in flatten(data):
                    stream.write(path + " = " + value + "\n")
            members = data.get("memberSolutionIds", [])
            index.append(dict(zip(INDEX_COLUMNS, [record["kind"], record_id(record), data.get("name"),
                         record["path"], record["directory"], status, data.get("experimentId"),
                         json.dumps(members, ensure_ascii=False), data.get("attachedSolutionId"), record["parent"],
                         " | ".join(issues)])))
        write_csv(destination / "index.csv", INDEX_COLUMNS, index)
        with (destination / "recovery_log.txt").open("w", encoding="utf-8") as stream:
            stream.write("Source: %s\nSchema: %s\nRecovered objects: %d\n" % (source, package.version, len(records)))
            stream.write("Reports and individual refits intentionally excluded; their payloads were not checked.\n")
            for path, message in package.issues:
                stream.write("%s: %s\n" % (path, message))
            if not package.issues:
                stream.write("No recovery issues found in the recovered content.\n")
        code = 1 if package.issues else 0
        progress(("Partial recovery" if code else "Recovery complete") + ": " + str(destination))
        return code, destination
    except OSError as exc:
        if destination is not None:
            try:
                (destination / "RECOVERY_INCOMPLETE.txt").write_text(
                    "Recovery stopped before completion: %s\nExisting output may be incomplete.\n" % exc, encoding="utf-8")
            except OSError:
                pass
        raise RecoveryError("File operation failed: %s. Use --output with a writable directory. %s" %
                            (exc, "Partial output: " + str(destination) if destination else "")) from exc
    finally:
        package.archive.close()


def prompt_for_file():
    try:
        return input("Path to .ftxtc file (or Enter to cancel): ").strip().strip('"').strip("'")
    except EOFError:
        return ""


def main(argv=None):
    # Redirected Windows terminals may use a code page lacking a project name's characters.
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(errors="backslashreplace")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("project", nargs="?", help="Path to a schema 1.2–1.6 .ftxtc project")
    parser.add_argument("--output", type=Path, help="New output directory (numbered suffix if it already exists)")
    args = parser.parse_args(argv)
    try:
        project = args.project or prompt_for_file()
        if not project:
            print("No file selected; nothing changed.")
            return 0
        code, _ = recover(project, args.output)
        return code
    except (RecoveryError, MemoryError, RecursionError) as exc:
        print("Recovery failed: %s" % (str(exc) or "Insufficient resources to read this project."), file=sys.stderr)
        return 2
    except KeyboardInterrupt:
        print("Recovery cancelled; any output already written may be incomplete.", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
