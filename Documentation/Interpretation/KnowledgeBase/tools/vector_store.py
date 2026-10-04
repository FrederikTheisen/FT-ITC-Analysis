#!/usr/bin/env python3
"""Create, fill and test an OpenAI vector store from the built knowledge base.

Needs the official SDK (`pip install openai`) and OPENAI_API_KEY. It only ever creates stores and
uploads files; it never deletes or modifies existing stores, so the current production store is
untouched until you change VectorStoreId in the service configuration.

    python3 tools/build_kb.py
    python3 tools/vector_store.py create --name "ft-itc-kb-2026-10"
    python3 tools/vector_store.py upload --store-id vs_...
    python3 tools/vector_store.py probe  --store-id vs_...          # real hybrid ranker, same queries
    python3 tools/vector_store.py search --store-id vs_... --query "low c-value n and dH correlated"

Upload writes build/upload_manifest.json (entry id -> OpenAI file_id). The service already logs the
file_id of every retrieved file, so that manifest tells you which entries shaped an interpretation.
Re-running upload for the same store resumes: entries already in the manifest are skipped.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def client(args):
    try:
        from openai import OpenAI
    except ImportError:
        sys.exit("The OpenAI SDK is required: pip install openai")
    return OpenAI(api_key=args.api_key) if args.api_key else OpenAI()


def load_manifest(build: Path) -> list[dict]:
    path = build / "manifest.json"
    if not path.exists():
        sys.exit(f"{path} not found; run tools/build_kb.py first")
    return json.loads(path.read_text(encoding="utf-8"))


def cmd_create(args) -> None:
    store = client(args).vector_stores.create(name=args.name, description=args.description or None)
    print(store.id)
    print(f"created vector store '{args.name}'. Upload with: tools/vector_store.py upload --store-id {store.id}", file=sys.stderr)


def cmd_upload(args) -> None:
    build = Path(args.build)
    entries = load_manifest(build)
    state_path = build / "upload_manifest.json"
    state = {"store_id": args.store_id, "entries": {}}
    if state_path.exists():
        previous = json.loads(state_path.read_text(encoding="utf-8"))
        if previous.get("store_id") == args.store_id:
            state = previous
        elif not args.force_new_manifest:
            sys.exit(f"{state_path} belongs to {previous.get('store_id')}; pass --force-new-manifest to start over for {args.store_id}")
    todo = [e for e in entries if e["id"] not in state["entries"]]
    print(f"{len(entries)} entries, {len(entries) - len(todo)} already uploaded, {len(todo)} to upload")
    if args.dry_run or not todo:
        return
    chunking = {"type": "static", "static": {"max_chunk_size_tokens": args.max_chunk_tokens, "chunk_overlap_tokens": args.chunk_overlap_tokens}}
    c = client(args)
    for start in range(0, len(todo), args.batch_size):
        part = todo[start:start + args.batch_size]
        files = []
        for e in part:
            data = (build / e["file"]).read_bytes()
            uploaded = c.files.create(file=(Path(e["file"]).name, data), purpose="assistants")
            files.append({
                "file_id": uploaded.id,
                "attributes": {"entry_id": e["id"], "kind": e["kind"], "basis": e["basis"], "status": e["status"]},
                "chunking_strategy": chunking,
            })
            state["entries"][e["id"]] = uploaded.id
        batch = c.vector_stores.file_batches.create_and_poll(args.store_id, files=files)
        counts = batch.file_counts
        print(f"batch {start // args.batch_size + 1}: status={batch.status} completed={counts.completed} failed={counts.failed} in_progress={counts.in_progress}")
        if counts.failed:
            print("  some files failed; inspect them in the OpenAI dashboard and re-run upload after fixing", file=sys.stderr)
        state["updated_at"] = dt.datetime.now(dt.timezone.utc).isoformat()
        state_path.write_text(json.dumps(state, indent=1), encoding="utf-8")  # checkpoint after every batch
    print(f"wrote {state_path}")


def entry_id_from_filename(name: str) -> str:
    return Path(name).stem


def cmd_search(args) -> None:
    results = client(args).vector_stores.search(args.store_id, query=args.query, max_num_results=args.k, rewrite_query=args.rewrite)
    for rank, r in enumerate(results.data, 1):
        first = (r.content[0].text if r.content else "").strip().splitlines()[0:1]
        print(f"{rank:2d}  {r.score:6.3f}  {entry_id_from_filename(r.filename):42s} {first[0][:80] if first else ''}")


def cmd_probe(args) -> None:
    c = client(args)
    rows = []
    for line in Path(args.queries).read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.startswith("#"):
            continue
        query, _, expected = line.partition("\t")
        rows.append((query.strip(), [e.strip() for e in expected.split(",") if e.strip()]))
    hits, misses, ranks = 0, [], []
    for query, expected in rows:
        res = c.vector_stores.search(args.store_id, query=query, max_num_results=args.k, rewrite_query=args.rewrite)
        top = []
        for r in res.data:
            eid = entry_id_from_filename(r.filename)
            if eid not in top:
                top.append(eid)
        found = [top.index(e) + 1 for e in expected if e in top]
        if found:
            hits += 1
            ranks.append(min(found))
        else:
            misses.append((query, expected, top))
        time.sleep(args.pause)
    print(f"live recall@{args.k}: {hits}/{len(rows)}; rank-1 hits: {sum(1 for r in ranks if r == 1)}")
    for query, expected, top in misses:
        print(f"  - {query}\n    expected: {', '.join(expected)}\n    got: {', '.join(top[:args.k])}")
    sys.exit(0 if not misses else 2)


def cmd_status(args) -> None:
    store = client(args).vector_stores.retrieve(args.store_id)
    c = store.file_counts
    print(f"{store.id} '{store.name}': status={store.status} files total={c.total} completed={c.completed} in_progress={c.in_progress} failed={c.failed}")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--api-key", help="defaults to OPENAI_API_KEY")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("create", help="create a new, empty vector store")
    p.add_argument("--name", required=True)
    p.add_argument("--description")
    p.set_defaults(fn=cmd_create)

    p = sub.add_parser("upload", help="upload build/entries/*.md (one file per entry) and attach them with attributes")
    p.add_argument("--store-id", required=True)
    p.add_argument("--build", default=str(ROOT / "build"))
    p.add_argument("--batch-size", type=int, default=100)
    p.add_argument("--max-chunk-tokens", type=int, default=800)
    p.add_argument("--chunk-overlap-tokens", type=int, default=400)
    p.add_argument("--dry-run", action="store_true")
    p.add_argument("--force-new-manifest", action="store_true")
    p.set_defaults(fn=cmd_upload)

    p = sub.add_parser("search", help="run one query against the store")
    p.add_argument("--store-id", required=True)
    p.add_argument("--query", required=True)
    p.add_argument("-k", type=int, default=8)
    p.add_argument("--rewrite", action="store_true", help="let the API rewrite the query (the service does not)")
    p.set_defaults(fn=cmd_search)

    p = sub.add_parser("probe", help="run tools/probe_queries.tsv against the live store and report recall@k")
    p.add_argument("--store-id", required=True)
    p.add_argument("--queries", default=str(Path(__file__).with_name("probe_queries.tsv")))
    p.add_argument("-k", type=int, default=8)
    p.add_argument("--rewrite", action="store_true")
    p.add_argument("--pause", type=float, default=0.2, help="seconds between requests")
    p.set_defaults(fn=cmd_probe)

    p = sub.add_parser("status", help="show file counts of a store")
    p.add_argument("--store-id", required=True)
    p.set_defaults(fn=cmd_status)

    args = ap.parse_args()
    args.fn(args)


if __name__ == "__main__":
    main()
