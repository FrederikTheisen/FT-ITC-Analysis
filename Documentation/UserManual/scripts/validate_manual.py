#!/usr/bin/env python3
"""Validate the portable FT-ITC Analysis manual content contract."""

from __future__ import annotations

import csv
import re
import sys
from datetime import date
from pathlib import Path
from urllib.parse import urlparse

from PIL import Image

from build_pdf import SOURCE_ROOT, parse_manifest, parse_page, slugify


REQUIRED_FRONT_MATTER = {"title", "summary", "slug", "nav_order", "last_verified", "_verification.product_version", "_verification.commit"}
PLACEHOLDERS = re.compile(r"\b(TODO|TBD|FIXME|PLACEHOLDER)\b|lorem ipsum", re.MULTILINE)
LINK_PATTERN = re.compile(r"(?<!!)\[([^\]]+)\]\(([^)]+)\)")
IMAGE_PATTERN = re.compile(r"!\[([^\]]*)\]\(([^)]+)\)")
HEADING_PATTERN = re.compile(r"^(#{1,6})\s+(.+)$", re.MULTILINE)


def fail(errors: list[str], message: str) -> None:
    errors.append(message)


def main() -> int:
    root = SOURCE_ROOT
    errors: list[str] = []
    manifest = parse_manifest(root / "manual.yml")
    items = manifest.get("pages", [])
    if len(items) != 12:
        fail(errors, f"manual.yml contains {len(items)} pages; expected 12")

    page_paths = [root / item["file"] for item in items]
    pages_by_name = {}
    pages_by_slug = {}
    external_urls: set[str] = set()

    for expected_order, (item, path) in enumerate(zip(items, page_paths), start=1):
        if not path.is_file():
            fail(errors, f"missing page: {path}")
            continue
        page = parse_page(path)
        pages_by_name[path.name] = page
        missing = REQUIRED_FRONT_MATTER - set(page.metadata)
        if missing:
            fail(errors, f"{path.name}: missing front matter {sorted(missing)}")
        if page.metadata.get("slug") != item.get("slug"):
            fail(errors, f"{path.name}: slug differs from manual.yml")
        if page.metadata.get("slug") in pages_by_slug:
            fail(errors, f"duplicate slug: {page.metadata.get('slug')}")
        pages_by_slug[page.metadata.get("slug", "")] = page
        if page.metadata.get("nav_order") != str(expected_order):
            fail(errors, f"{path.name}: nav_order is not {expected_order}")
        try:
            date.fromisoformat(page.metadata.get("last_verified", ""))
        except ValueError:
            fail(errors, f"{path.name}: invalid last_verified date")
        if page.metadata.get("_verification.product_version") != manifest.get("product_version"):
            fail(errors, f"{path.name}: product version differs from manifest")
        if page.metadata.get("_verification.commit") != manifest.get("verification_commit"):
            fail(errors, f"{path.name}: commit differs from manifest")
        if PLACEHOLDERS.search(page.body):
            fail(errors, f"{path.name}: contains placeholder text")
        if "lower-left" in page.body.lower() or "upper-right" in page.body.lower():
            fail(errors, f"{path.name}: contains layout-dependent wording")
        if ":::" in page.body:
            fail(errors, f"{path.name}: contains framework-specific directive")
        headings = HEADING_PATTERN.findall(page.body)
        if not headings or len(headings[0][0]) != 1:
            fail(errors, f"{path.name}: first heading is not H1")
        if headings and headings[0][1].strip() != page.metadata.get("title"):
            fail(errors, f"{path.name}: H1 differs from title front matter")

    for path, page in [(path, pages_by_name.get(path.name)) for path in page_paths]:
        if page is None:
            continue
        heading_ids = {slugify(title) for _, title in HEADING_PATTERN.findall(page.body)}
        for label, target in LINK_PATTERN.findall(page.body):
            if target.startswith(("https://", "http://")):
                parsed = urlparse(target)
                if not parsed.netloc:
                    fail(errors, f"{path.name}: malformed external link {target}")
                external_urls.add(target)
                continue
            target_file, _, fragment = target.partition("#")
            if target_file:
                destination = (path.parent / target_file).resolve()
                if not destination.is_file():
                    destination_page = pages_by_slug.get(Path(target_file).stem)
                    if destination_page is None:
                        fail(errors, f"{path.name}: missing internal target {target}")
                        continue
                else:
                    destination_page = pages_by_name.get(destination.name)
            else:
                destination_page = page
            if fragment and destination_page is not None:
                destination_ids = {slugify(title) for _, title in HEADING_PATTERN.findall(destination_page.body)}
                if slugify(fragment) not in destination_ids:
                    fail(errors, f"{path.name}: missing heading anchor {target}")
        lines = page.body.splitlines()
        for index, line in enumerate(lines):
            match = IMAGE_PATTERN.fullmatch(line.strip())
            if not match:
                continue
            alt, target = match.groups()
            if not alt.strip():
                fail(errors, f"{path.name}:{index + 1}: image has empty alt text")
            asset = (path.parent / target).resolve()
            if not asset.is_file():
                fail(errors, f"{path.name}:{index + 1}: missing image {target}")
            if index + 2 >= len(lines) or not lines[index + 2].strip().startswith("*"):
                fail(errors, f"{path.name}:{index + 1}: image is not followed by a caption")

    asset_manifest = root / "assets" / "assets.yml"
    if not asset_manifest.is_file():
        fail(errors, "missing assets/assets.yml")
    screenshot = root / "assets" / "analysis-result-workspace.png"
    if screenshot.is_file():
        with Image.open(screenshot) as image:
            if image.width < 1200 or image.height < 700:
                fail(errors, "canonical screenshot is below publication resolution")

    matrix_path = root / "verification" / "feature-matrix.csv"
    with matrix_path.open(newline="", encoding="utf-8") as handle:
        rows = list(csv.DictReader(handle))
    if len(rows) < 60:
        fail(errors, f"verification matrix has only {len(rows)} rows")
    for row in rows:
        if row["status"] not in {"verified", "conditional", "outside_scope"}:
            fail(errors, f"matrix {row['id']}: invalid status {row['status']}")
        if row["status"] != "outside_scope" and row["page"] not in pages_by_slug:
            fail(errors, f"matrix {row['id']}: unknown public page {row['page']}")
        try:
            date.fromisoformat(row["last_verified"])
        except ValueError:
            fail(errors, f"matrix {row['id']}: invalid verification date")

    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1

    print(f"Validated {len(page_paths)} pages, {len(rows)} feature rows, {len(external_urls)} unique external links, and all referenced assets.")
    for url in sorted(external_urls):
        print(f"EXTERNAL {url}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
