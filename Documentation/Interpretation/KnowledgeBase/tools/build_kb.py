#!/usr/bin/env python3
"""Validate the ITC knowledge base and emit one retrieval-ready file per entry.

Source of truth:  entries/*.md   (human-edited, many entries per file)
                  sources.json   (registry of citable literature)
Output:           build/entries/<id>.md   one self-contained chunk per file
                  build/manifest.json     id -> file, kind, basis, sources, size
                  build/KB_FULL.md        all entries concatenated (for reading/diffing)

Why one file per entry: the interpretation service retrieves with OpenAI
file_search, which splits every file on token count (default 800 tokens, 400
overlap) without regard to structure. An entry that fits in one file below the
chunk size is always retrieved whole, and the file_id the service already logs
identifies exactly which entry the model saw.

Usage:
    python3 tools/build_kb.py            # validate + build
    python3 tools/build_kb.py --check    # validate only (non-zero exit on errors)
    python3 tools/build_kb.py --only-reviewed
    python3 tools/build_kb.py --stats
Standard library only.
"""
from __future__ import annotations

import argparse
import json
import math
import re
import shutil
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# kind -> (id prefix, label used in the emitted title)
KINDS = {
    "playbook":   ("pb-", "Playbook"),
    "concept":    ("cn-", "Concept"),
    "reference":  ("rf-", "Reference"),
    "design":     ("ds-", "Design"),
    "diagnostic": ("dx-", "Diagnostic"),
    "system":     ("sy-", "System"),
    "method":     ("me-", "Method"),
    "model":      ("md-", "FT-ITC model"),
    "precedent":  ("pr-", "Precedent"),
    "literature": ("lr-", "Literature rule"),
    "family":     ("sf-", "Related studies"),
}
BASES = {
    "general": "general ITC knowledge (background; not a citable source)",
    "mixed": "general ITC knowledge, partly supported by the sources listed under Sources",
    "literature": "published sources listed under Sources",
}
STATUSES = {"draft", "reviewed"}
HEADER_KEYS = {"id", "kind", "topics", "basis", "status", "matches", "cite", "reading", "legacy_id"}
SOURCE_ONLY_KEYS = {"reading", "legacy_id", "status"}  # never emitted

ID_RE = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
LABEL_RE = re.compile(r"^([A-Z][A-Za-z0-9 /&'’()\-–,.]{1,60}):(?:\s|$)")

WARN_TOKENS = 560   # estimator is deliberately conservative (~20-30% above real BPE counts)
ERROR_TOKENS = 700  # file_search default chunk is 800 tokens; keep real margin
MIN_BODY_LABELS = {"diagnostic": 3, "concept": 1, "design": 1, "system": 2, "method": 1, "model": 1, "playbook": 1}


# A model may query with the symbol or with the words, so the emitted keyword line carries both
# forms. Expansion happens at build time so source files stay clean.
VOCABULARY = [  # (symbol, words that already count as the spelled-out form, text added when missing)
    ("ΔCp", ["heat capacity"], "heat capacity change"),
    ("ΔH", ["enthalpy"], "binding enthalpy"),
    ("TΔS", ["entropy"], "entropy contribution"),
    ("ΔG", ["free energy", "gibbs"], "free energy of binding"),
    ("Kd", ["affinity", "dissociation constant"], "dissociation constant, affinity"),
]


def vocabulary_additions(text: str) -> list[str]:
    low = text.lower()
    added: list[str] = []
    for symbol, detect, words in VOCABULARY:
        has_symbol = symbol.lower() in low
        has_words = any(w in low for w in detect)
        if has_symbol and not has_words:
            added.append(words)
        elif has_words and not has_symbol:
            added.append(symbol)
    return added


def estimate_tokens(text: str) -> int:
    """Conservative BPE-ish estimate without a tokenizer dependency."""
    pieces = re.findall(r"[A-Za-z]+|\d+|[^\sA-Za-z\d]", text)
    by_pieces = sum(math.ceil(len(p) / 4.5) if p[0].isalpha() else 1 for p in pieces)
    return int(max(by_pieces, len(text) / 3.8))


def load_sources() -> dict[str, dict]:
    sources = json.loads((ROOT / "sources.json").read_text(encoding="utf-8"))
    out: dict[str, dict] = {}
    for s in sources:
        if s["id"] in out:
            raise SystemExit(f"sources.json: duplicate id {s['id']}")
        out[s["id"]] = s
    return out


def format_citation(source: dict) -> str:
    title = source["title"].rstrip(". ")
    return f"\"{title}\" ({source['journal']}, {source['year']})"


class Entry:
    def __init__(self, path: Path, line: int, title: str):
        self.path, self.line, self.title = path, line, title
        self.header: dict[str, str] = {}
        self.body = ""

    @property
    def id(self) -> str:
        return self.header.get("id", "")

    @property
    def kind(self) -> str:
        return self.header.get("kind", "")

    def where(self) -> str:
        return f"{self.path.name}:{self.line} [{self.id or self.title[:40]}]"


def parse_file(path: Path) -> list[Entry]:
    entries: list[Entry] = []
    current: Entry | None = None
    in_header = False
    body_lines: list[str] = []

    def finish() -> None:
        if current is not None:
            current.body = "\n".join(body_lines).strip("\n").rstrip()

    for number, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if raw.startswith("## "):
            finish()
            current = Entry(path, number, raw[3:].strip())
            entries.append(current)
            in_header, body_lines = True, []
            continue
        if current is None:
            continue  # file-level commentary before the first entry
        if in_header:
            if not raw.strip():
                in_header = False
                continue
            m = re.match(r"^([a-z_]+):\s*(.*)$", raw)
            if m and m.group(1) in HEADER_KEYS:
                current.header[m.group(1)] = m.group(2).strip()
                continue
            in_header = False  # first non-header line starts the body
        body_lines.append(raw)
    finish()
    return entries


def split_list(value: str) -> list[str]:
    return [item.strip() for item in value.split(",") if item.strip()]


def validate(entries: list[Entry], sources: dict[str, dict]) -> tuple[list[str], list[str]]:
    errors: list[str] = []
    warnings: list[str] = []
    seen: dict[str, Entry] = {}
    for e in entries:
        where = e.where()
        unknown = set(e.header) - HEADER_KEYS
        if unknown:
            errors.append(f"{where}: unknown header keys {sorted(unknown)}")
        for key in ("id", "kind", "basis", "status", "matches"):
            if not e.header.get(key):
                errors.append(f"{where}: missing header '{key}'")
        if not e.id:
            continue
        if not ID_RE.match(e.id):
            errors.append(f"{where}: id must be lowercase-kebab-case")
        if e.id in seen:
            errors.append(f"{where}: duplicate id (also {seen[e.id].where()})")
        seen[e.id] = e
        if e.kind not in KINDS:
            errors.append(f"{where}: unknown kind '{e.kind}'")
        elif not e.id.startswith(KINDS[e.kind][0]):
            errors.append(f"{where}: id for kind '{e.kind}' must start with '{KINDS[e.kind][0]}'")
        if e.header.get("basis") not in BASES:
            errors.append(f"{where}: basis must be one of {sorted(BASES)}")
        if e.header.get("status") not in STATUSES:
            errors.append(f"{where}: status must be one of {sorted(STATUSES)}")
        cites = split_list(e.header.get("cite", ""))
        for c in cites:
            if c not in sources:
                errors.append(f"{where}: cite '{c}' is not in sources.json")
        if len(cites) > 6 and e.kind not in ("literature", "family"):
            warnings.append(f"{where}: {len(cites)} citations; the Sources line gets long and dilutes the entry (aim for <= 6)")
        basis = e.header.get("basis")
        if basis in ("literature", "mixed") and not cites:
            errors.append(f"{where}: basis '{basis}' requires cite")
        if basis == "general" and cites:
            errors.append(f"{where}: basis 'general' must not carry cite (use 'mixed')")
        if not e.body.strip():
            errors.append(f"{where}: empty body")
        if len(e.title) < 12 or len(e.title) > 150:
            warnings.append(f"{where}: title length {len(e.title)} (aim for a situation-style title of 12-150 chars)")
        labels = [m.group(1) for line in e.body.splitlines() if (m := LABEL_RE.match(line))]
        need = MIN_BODY_LABELS.get(e.kind, 0)
        if len(labels) < need:
            warnings.append(f"{where}: only {len(labels)} labelled paragraphs ('Label: text'); {e.kind} entries normally have >= {need}")
        if re.search(r"https?://", e.body):
            warnings.append(f"{where}: body contains a URL; the interpretation guidance omits links")
        if re.search(r"\b(you must|you should|always say|never say|do not say)\b", e.body, re.I):
            warnings.append(f"{where}: imperative phrasing; retrieved text is treated as evidence, write it as knowledge")
        tokens = estimate_tokens(render(e, sources))
        if tokens > ERROR_TOKENS:
            errors.append(f"{where}: ~{tokens} tokens exceeds {ERROR_TOKENS}; split the entry")
        elif tokens > WARN_TOKENS:
            warnings.append(f"{where}: ~{tokens} tokens (long for a single retrieval unit)")
    return errors, warnings


def render(e: Entry, sources: dict[str, dict]) -> str:
    label = KINDS[e.kind][1] if e.kind in KINDS else e.kind
    basis = BASES.get(e.header.get("basis", ""), "")
    if e.kind == "family":
        basis = "published studies listed below; the overview synthesizes their findings"
    lines = [f"# [{label}] {e.title}", ""]
    lines.append(f"Basis: {basis}.")
    topics = e.header.get("topics", "").strip()
    if topics:
        lines.append(f"Topics: {topics}.")
    matches = e.header.get("matches", "").strip().rstrip(".")
    extra = vocabulary_additions(e.title + " " + matches + " " + e.body)
    if extra:
        matches += "; " + ", ".join(extra)
    lines.append(f"Also matches: {matches}.")
    lines += ["", e.body.strip()]
    cites = split_list(e.header.get("cite", ""))
    if cites and e.kind != "family":  # family hubs cite each member study inline
        lines += ["", "Sources: " + "; ".join(format_citation(sources[c]) for c in cites if c in sources) + "."]
    return "\n".join(lines).rstrip() + "\n"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--check", action="store_true", help="validate only, write nothing")
    ap.add_argument("--only-reviewed", action="store_true", help="emit only entries with status: reviewed")
    ap.add_argument("--out", default=str(ROOT / "build"), help="output directory (default: ./build)")
    ap.add_argument("--stats", action="store_true", help="print size/kind statistics")
    ap.add_argument("--strict", action="store_true", help="treat warnings as errors")
    args = ap.parse_args()

    sources = load_sources()
    entries: list[Entry] = []
    for path in sorted((ROOT / "entries").glob("*.md")):
        entries += parse_file(path)

    errors, warnings = validate(entries, sources)
    for w in warnings:
        print("warning:", w, file=sys.stderr)
    for err in errors:
        print("ERROR:", err, file=sys.stderr)
    print(f"{len(entries)} entries, {len(errors)} errors, {len(warnings)} warnings")
    if errors or (args.strict and warnings):
        return 1

    selected = [e for e in entries if not args.only_reviewed or e.header["status"] == "reviewed"]
    sizes = {e.id: estimate_tokens(render(e, sources)) for e in selected}

    if args.stats or args.check:
        kinds = Counter(e.kind for e in selected)
        basis = Counter(e.header["basis"] for e in selected)
        status = Counter(e.header["status"] for e in selected)
        print("by kind:", dict(sorted(kinds.items())))
        print("by basis:", dict(sorted(basis.items())))
        print("by status:", dict(sorted(status.items())))
        values = sorted(sizes.values())
        if values:
            print(f"tokens/entry: min {values[0]}, median {values[len(values)//2]}, max {values[-1]}, total {sum(values)}")
        cited = {c for e in selected for c in split_list(e.header.get("cite", ""))}
        print(f"registry sources cited by at least one entry: {len(cited)} of {len(sources)}")
    if args.check:
        return 0

    out = Path(args.out)
    if out.exists():
        shutil.rmtree(out)
    (out / "entries").mkdir(parents=True)
    manifest = []
    full = []
    for e in selected:
        text = render(e, sources)
        (out / "entries" / f"{e.id}.md").write_text(text, encoding="utf-8")
        full.append(text)
        manifest.append({
            "id": e.id,
            "file": f"entries/{e.id}.md",
            "kind": e.kind,
            "title": e.title,
            "basis": e.header["basis"],
            "status": e.header["status"],
            "sources": split_list(e.header.get("cite", "")),
            "legacy_id": e.header.get("legacy_id"),
            "estimated_tokens": sizes[e.id],
        })
    (out / "manifest.json").write_text(json.dumps(manifest, indent=1, ensure_ascii=False), encoding="utf-8")
    (out / "KB_FULL.md").write_text("\n\n---\n\n".join(full), encoding="utf-8")
    print(f"wrote {len(selected)} files to {out}/entries")
    return 0


if __name__ == "__main__":
    sys.exit(main())
