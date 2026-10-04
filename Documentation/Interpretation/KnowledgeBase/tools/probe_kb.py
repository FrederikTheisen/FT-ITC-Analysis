#!/usr/bin/env python3
"""Offline retrieval smoke test for the built knowledge base.

The production service retrieves with OpenAI file_search (hybrid semantic +
keyword ranking over ~800-token chunks, top 8 per call). This script cannot
reproduce the semantic half, so it is a *lower-bound proxy*: it scores each
built entry with BM25 on its full text and checks whether the entries a
question *should* surface appear in the top K. A miss here means the entry's
title/keywords do not carry the vocabulary a model is likely to query with;
fix the entry's title, 'matches:' line or opening sentence and re-run.

    python3 tools/build_kb.py && python3 tools/probe_kb.py
    python3 tools/probe_kb.py --query "Tris buffer enthalpy depends on buffer" -v
    python3 tools/probe_kb.py -k 8 --show-misses

probe_queries.tsv format (tab-separated, '#' comments allowed):
    <query>\t<expected id>[,<expected id>...]     any one expected id in the top K = hit
Standard library only.
"""
from __future__ import annotations

import argparse
import json
import math
import re
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
STOP = set("a an and are as at be by for from has have in is it its of on or that the this to was were with which when where into than then these those not no".split())


def tokenize(text: str) -> list[str]:
    text = text.lower().replace("δ", "delta ").replace("−", "-").replace("–", "-").replace("µ", "u").replace("μ", "u")
    toks = re.findall(r"[a-z]+[0-9]*|[0-9]+(?:\.[0-9]+)?", text)
    out = []
    for t in toks:
        if t in STOP:
            continue
        for suffix in ("ing", "ed", "es", "s", "ly"):
            if len(t) > len(suffix) + 3 and t.endswith(suffix) and not t.endswith("ss"):
                t = t[: -len(suffix)]
                break
        out.append(t)
    return out


class BM25:
    def __init__(self, docs: dict[str, str], k1: float = 1.4, b: float = 0.75):
        self.k1, self.b = k1, b
        self.tf = {i: Counter(tokenize(t)) for i, t in docs.items()}
        self.len = {i: sum(c.values()) for i, c in self.tf.items()}
        self.avg = sum(self.len.values()) / max(len(self.len), 1)
        df: Counter = Counter()
        for c in self.tf.values():
            df.update(c.keys())
        n = len(docs)
        self.idf = {t: math.log(1 + (n - d + 0.5) / (d + 0.5)) for t, d in df.items()}

    def rank(self, query: str) -> list[tuple[str, float]]:
        q = tokenize(query)
        scores = {}
        for doc, tf in self.tf.items():
            s = 0.0
            for t in q:
                f = tf.get(t, 0)
                if not f:
                    continue
                s += self.idf.get(t, 0) * f * (self.k1 + 1) / (f + self.k1 * (1 - self.b + self.b * self.len[doc] / self.avg))
            if s:
                scores[doc] = s
        return sorted(scores.items(), key=lambda kv: -kv[1])


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--build", default=str(ROOT / "build"))
    ap.add_argument("--queries", default=str(Path(__file__).with_name("probe_queries.tsv")))
    ap.add_argument("--query", help="ad-hoc query; prints the ranking and exits")
    ap.add_argument("-k", type=int, default=8, help="results per query (service default is 8)")
    ap.add_argument("-v", "--verbose", action="store_true")
    ap.add_argument("--show-misses", action="store_true", help="print the top results for each missed query")
    args = ap.parse_args()

    build = Path(args.build)
    manifest = json.loads((build / "manifest.json").read_text(encoding="utf-8"))
    titles = {m["id"]: m["title"] for m in manifest}
    docs = {m["id"]: (build / m["file"]).read_text(encoding="utf-8") for m in manifest}
    index = BM25(docs)

    if args.query:
        for rank, (doc, score) in enumerate(index.rank(args.query)[: args.k], 1):
            print(f"{rank:2d}  {score:6.2f}  {doc:42s} {titles[doc][:80]}")
        return 0

    rows = []
    for line in Path(args.queries).read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.startswith("#"):
            continue
        query, _, expected = line.partition("\t")
        rows.append((query.strip(), [e.strip() for e in expected.split(",") if e.strip()]))

    unknown = sorted({e for _, exp in rows for e in exp if e not in docs})
    if unknown:
        print("expected ids not present in build (typo, or entry removed):", ", ".join(unknown), file=sys.stderr)
    hits = 0
    misses = []
    for query, expected in rows:
        ranking = index.rank(query)
        top = [d for d, _ in ranking[: args.k]]
        found = [top.index(e) + 1 for e in expected if e in top]
        ok = bool(found)
        hits += ok
        if not ok:
            misses.append((query, expected, top))
        if args.verbose:
            print(("HIT  " if ok else "MISS ") + f"rank={min(found) if found else '-'}  {query}")
    print(f"recall@{args.k} (any expected entry in top {args.k}): {hits}/{len(rows)}")
    if misses:
        print("\nmisses:")
        for query, expected, top in misses:
            print(f"  - {query}\n    expected: {', '.join(expected)}")
            if args.show_misses:
                for d in top:
                    print(f"      got {d}: {titles[d][:70]}")
    return 0 if hits == len(rows) else 2


if __name__ == "__main__":
    sys.exit(main())
