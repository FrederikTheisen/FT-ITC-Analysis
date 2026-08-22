#!/usr/bin/env python3
"""Verify the generated manual PDF's structure, navigation, text, and metadata."""

from __future__ import annotations

import argparse
from pathlib import Path

import pdfplumber
from pypdf import PdfReader


DEFAULT_PDF = Path(__file__).resolve().parents[3] / "output" / "pdf" / "FT-ITC-Analysis-User-Manual.pdf"


def flatten_outline(items) -> list:
    flattened = []
    for item in items:
        if isinstance(item, list):
            flattened.extend(flatten_outline(item))
        else:
            flattened.append(item)
    return flattened


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pdf", nargs="?", type=Path, default=DEFAULT_PDF)
    args = parser.parse_args()
    path = args.pdf.resolve()
    if not path.is_file():
        raise SystemExit(f"PDF does not exist: {path}")

    reader = PdfReader(str(path))
    assert 35 <= len(reader.pages) <= 80, f"unexpected page count: {len(reader.pages)}"
    metadata = reader.metadata
    assert metadata.title == "FT-ITC Analysis User Manual"
    assert metadata.subject == "User manual for FT-ITC Analysis"
    assert "ReportLab" in (metadata.creator or "")

    outline = flatten_outline(reader.outline)
    assert len(outline) >= 80, f"too few bookmarks: {len(outline)}"

    link_count = 0
    external_count = 0
    internal_count = 0
    for page in reader.pages:
        width = float(page.mediabox.width)
        height = float(page.mediabox.height)
        assert abs(width - 595.276) < 1 and abs(height - 841.89) < 1, "non-A4 page found"
        for annotation_reference in page.get("/Annots", []):
            annotation = annotation_reference.get_object()
            if annotation.get("/Subtype") != "/Link":
                continue
            link_count += 1
            action = annotation.get("/A")
            if action and action.get("/URI"):
                external_count += 1
            elif annotation.get("/Dest") is not None or (action and action.get("/S") == "/GoTo"):
                internal_count += 1

    with pdfplumber.open(str(path)) as pdf:
        extracted = [(page.extract_text() or "").strip() for page in pdf.pages]
    assert all(len(text) >= 25 for text in extracted), "one or more pages have no meaningful extractable text"
    complete_text = "\n".join(extracted)
    for required in (
        "Contents",
        "Last verified: 2026-08-22",
        "Processing thermograms",
        "Two-Sets-Of-Sites",
        "Multiple-experiment analysis",
        "Uncertainty glossary",
        "References and further information",
        "Generated: 2026-08-22",
    ):
        assert required in complete_text, f"missing extracted text: {required}"
    assert external_count >= 5, f"too few external links: {external_count}"
    assert internal_count >= 12, f"too few internal links: {internal_count}"

    print(
        f"Verified {len(reader.pages)} A4 pages, {len(outline)} bookmarks, "
        f"{internal_count} internal links, {external_count} external links, "
        f"and {sum(len(text) for text in extracted):,} extracted characters."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
