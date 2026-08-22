#!/usr/bin/env python3
"""Build the FT-ITC Analysis A4 user manual from the web-first CommonMark source."""

from __future__ import annotations

import argparse
import html
import re
from dataclasses import dataclass
from datetime import date
from pathlib import Path
from typing import Iterable

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfbase import pdfmetrics
from reportlab.platypus import (
    BaseDocTemplate,
    Flowable,
    HRFlowable,
    Image,
    KeepTogether,
    ListFlowable,
    ListItem,
    PageBreak,
    PageTemplate,
    Paragraph,
    Spacer,
    Table,
    TableStyle,
    Frame,
)
from reportlab.platypus.tableofcontents import TableOfContents


SOURCE_ROOT = Path(__file__).resolve().parents[1]
REPOSITORY_ROOT = SOURCE_ROOT.parents[1]
DEFAULT_OUTPUT = REPOSITORY_ROOT / "output" / "pdf" / "FT-ITC-Analysis-User-Manual.pdf"

BLUE = colors.HexColor("#376F95")
INK = colors.HexColor("#152935")
MUTED = colors.HexColor("#526773")
PALE_BLUE = colors.HexColor("#EEF5F9")
PALE_GRAY = colors.HexColor("#F5F7F8")
RULE = colors.HexColor("#C8D5DC")
CAUTION = colors.HexColor("#FFF4E5")
INTERPRET = colors.HexColor("#F1F7ED")


@dataclass
class PageSource:
    path: Path
    metadata: dict[str, str]
    body: str


def parse_scalar(value: str) -> str:
    value = value.strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
        return value[1:-1]
    return value


def parse_manifest(path: Path) -> dict:
    """Parse the deliberately small manual.yml contract without an external YAML dependency."""
    result: dict = {"pages": []}
    current_page: dict[str, str] | None = None
    for raw in path.read_text(encoding="utf-8").splitlines():
        stripped = raw.strip()
        if not stripped or stripped.startswith("#") or stripped in {"manual:", "pages:"}:
            continue
        if stripped.startswith("- file:"):
            current_page = {"file": parse_scalar(stripped.split(":", 1)[1])}
            result["pages"].append(current_page)
            continue
        if ":" not in stripped:
            continue
        key, value = stripped.split(":", 1)
        if current_page is not None and raw.startswith("      "):
            current_page[key.strip()] = parse_scalar(value)
        elif raw.startswith("  ") and not raw.startswith("    "):
            result[key.strip()] = parse_scalar(value)
    return result


def parse_page(path: Path) -> PageSource:
    text = path.read_text(encoding="utf-8")
    if not text.startswith("---\n"):
        raise ValueError(f"Missing front matter: {path}")
    _, front, body = text.split("---\n", 2)
    metadata: dict[str, str] = {}
    in_hidden = False
    for raw in front.splitlines():
        if raw.strip() == "_verification:":
            in_hidden = True
            continue
        if ":" not in raw:
            continue
        key, value = raw.strip().split(":", 1)
        metadata[("_verification." if in_hidden and raw.startswith("  ") else "") + key] = parse_scalar(value)
    return PageSource(path=path, metadata=metadata, body=body.strip())


def slugify(value: str) -> str:
    value = re.sub(r"[^a-zA-Z0-9]+", "-", value).strip("-").lower()
    return value or "section"


def inline_markup(text: str, page_slugs: dict[str, str]) -> str:
    escaped = html.escape(text, quote=False)

    def link(match: re.Match[str]) -> str:
        label, target = match.group(1), html.unescape(match.group(2))
        if target.endswith(".md") or ".md#" in target:
            filename, _, fragment = target.partition("#")
            destination = page_slugs.get(Path(filename).name, slugify(Path(filename).stem))
            if fragment:
                destination = f"{destination}-{slugify(fragment)}"
            href = f"#{destination}"
        else:
            href = html.escape(target, quote=True)
        return f'<link href="{href}" color="#27648A"><u>{label}</u></link>'

    escaped = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", link, escaped)
    escaped = re.sub(r"`([^`]+)`", r'<font name="Courier">\1</font>', escaped)
    escaped = re.sub(r"\*\*([^*]+)\*\*", r"<b>\1</b>", escaped)
    escaped = re.sub(r"(?<!\*)\*([^*]+)\*(?!\*)", r"<i>\1</i>", escaped)
    return escaped


class BookmarkParagraph(Paragraph):
    def __init__(self, text: str, style: ParagraphStyle, bookmark: str, level: int):
        super().__init__(text, style)
        self.bookmark_name = bookmark
        self.toc_level = level


class Callout(Flowable):
    def __init__(self, paragraph: Paragraph, background: colors.Color = PALE_BLUE):
        super().__init__()
        self.paragraph = paragraph
        self.background = background
        self.width = 0
        self.height = 0

    def wrap(self, avail_width: float, avail_height: float) -> tuple[float, float]:
        self.width = avail_width
        _, paragraph_height = self.paragraph.wrap(avail_width - 16 * mm, avail_height)
        self.height = paragraph_height + 7 * mm
        return avail_width, self.height

    def draw(self) -> None:
        self.canv.setFillColor(self.background)
        self.canv.setStrokeColor(RULE)
        self.canv.roundRect(0, 0, self.width, self.height, 2.5 * mm, fill=1, stroke=1)
        self.canv.setFillColor(BLUE)
        self.canv.rect(0, 0, 2.2 * mm, self.height, fill=1, stroke=0)
        self.paragraph.drawOn(self.canv, 8 * mm, 3.5 * mm)


class WorkflowDiagram(Flowable):
    labels = [
        ("Open", "raw data, heats, or project"),
        ("Check details", "concentrations and metadata"),
        ("Process", "baseline and integration"),
        ("Fit", "model and uncertainty"),
        ("Review", "curve, residuals, validity"),
        ("Preserve", "save and export"),
    ]

    def __init__(self):
        super().__init__()
        self.width = 0
        self.height = 39 * mm

    def wrap(self, avail_width: float, avail_height: float) -> tuple[float, float]:
        self.width = avail_width
        return avail_width, self.height

    def draw(self) -> None:
        gap = 2.3 * mm
        box_width = (self.width - gap * 5) / 6
        box_height = 30 * mm
        y = 4.5 * mm
        for i, (title, subtitle) in enumerate(self.labels):
            x = i * (box_width + gap)
            self.canv.setFillColor(PALE_BLUE)
            self.canv.setStrokeColor(BLUE)
            self.canv.roundRect(x, y, box_width, box_height, 2 * mm, fill=1, stroke=1)
            self.canv.setFillColor(BLUE)
            self.canv.setFont("Helvetica-Bold", 8)
            self.canv.drawString(x + 2.5 * mm, y + box_height - 4.5 * mm, str(i + 1))
            self.canv.setFillColor(INK)
            self.canv.setFont("Helvetica-Bold", 7.4)
            self.canv.drawCentredString(x + box_width / 2, y + 17 * mm, title)
            self.canv.setFillColor(MUTED)
            self.canv.setFont("Helvetica", 5.8)
            words = subtitle.split()
            lines: list[str] = []
            current = ""
            for word in words:
                candidate = f"{current} {word}".strip()
                if self.canv.stringWidth(candidate, "Helvetica", 5.8) < box_width - 4 * mm:
                    current = candidate
                else:
                    lines.append(current)
                    current = word
            if current:
                lines.append(current)
            for line_index, line in enumerate(lines[:2]):
                self.canv.drawCentredString(x + box_width / 2, y + (11 - line_index * 3.2) * mm, line)
            if i < len(self.labels) - 1:
                start_x = x + box_width
                arrow_y = y + box_height / 2
                self.canv.setStrokeColor(BLUE)
                self.canv.line(start_x, arrow_y, start_x + gap - 1 * mm, arrow_y)
                self.canv.setFillColor(BLUE)
                self.canv.line(start_x + gap - 2.2 * mm, arrow_y + 1.2 * mm, start_x + gap - 1 * mm, arrow_y)
                self.canv.line(start_x + gap - 2.2 * mm, arrow_y - 1.2 * mm, start_x + gap - 1 * mm, arrow_y)


class ManualDocTemplate(BaseDocTemplate):
    def __init__(self, filename: str, *, manual_title: str, version: str, **kwargs):
        super().__init__(filename, **kwargs)
        self.manual_title = manual_title
        self.version = version
        frame = Frame(
            self.leftMargin,
            self.bottomMargin,
            self.width,
            self.height,
            leftPadding=0,
            rightPadding=0,
            topPadding=0,
            bottomPadding=0,
            id="body",
        )
        self.addPageTemplates(PageTemplate(id="manual", frames=[frame], onPage=self.draw_page))

    def draw_page(self, canvas, doc) -> None:
        canvas.saveState()
        canvas.setStrokeColor(RULE)
        canvas.setLineWidth(0.5)
        canvas.line(self.leftMargin, A4[1] - 16 * mm, A4[0] - self.rightMargin, A4[1] - 16 * mm)
        canvas.line(self.leftMargin, 15 * mm, A4[0] - self.rightMargin, 15 * mm)
        canvas.setFillColor(MUTED)
        canvas.setFont("Helvetica", 7.5)
        canvas.drawString(self.leftMargin, A4[1] - 12.5 * mm, f"{self.manual_title}  |  version {self.version}")
        canvas.drawString(self.leftMargin, 10.5 * mm, "ft-itc.org/manual/")
        canvas.drawRightString(A4[0] - self.rightMargin, 10.5 * mm, f"Page {doc.page}")
        canvas.restoreState()

    def afterFlowable(self, flowable: Flowable) -> None:
        bookmark = getattr(flowable, "bookmark_name", None)
        if not bookmark:
            return
        level = getattr(flowable, "toc_level", 0)
        text = flowable.getPlainText()
        self.canv.bookmarkPage(bookmark)
        self.canv.addOutlineEntry(text, bookmark, level=level, closed=level > 0)
        self.notify("TOCEntry", (level, text, self.page, bookmark))


def make_styles() -> dict[str, ParagraphStyle]:
    sample = getSampleStyleSheet()
    body = ParagraphStyle(
        "ManualBody",
        parent=sample["BodyText"],
        fontName="Helvetica",
        fontSize=9.2,
        leading=13.1,
        textColor=INK,
        spaceAfter=2.4 * mm,
        allowWidows=0,
        allowOrphans=0,
    )
    return {
        "body": body,
        "title": ParagraphStyle("ManualTitle", parent=body, fontName="Helvetica-Bold", fontSize=27, leading=31, textColor=INK, alignment=TA_CENTER, spaceAfter=5 * mm),
        "subtitle": ParagraphStyle("ManualSubtitle", parent=body, fontSize=12, leading=17, textColor=MUTED, alignment=TA_CENTER, spaceAfter=4 * mm),
        "h1": ParagraphStyle("ManualH1", parent=body, fontName="Helvetica-Bold", fontSize=20, leading=24, textColor=INK, spaceBefore=1 * mm, spaceAfter=5 * mm, keepWithNext=True),
        "h2": ParagraphStyle("ManualH2", parent=body, fontName="Helvetica-Bold", fontSize=14, leading=18, textColor=BLUE, spaceBefore=4.5 * mm, spaceAfter=2.2 * mm, keepWithNext=True),
        "h3": ParagraphStyle("ManualH3", parent=body, fontName="Helvetica-Bold", fontSize=11.2, leading=14.2, textColor=INK, spaceBefore=3.5 * mm, spaceAfter=1.5 * mm, keepWithNext=True),
        "caption": ParagraphStyle("ManualCaption", parent=body, fontSize=7.6, leading=10, textColor=MUTED, alignment=TA_CENTER, spaceBefore=1 * mm, spaceAfter=3.5 * mm),
        "callout": ParagraphStyle("ManualCallout", parent=body, fontSize=8.6, leading=12, spaceAfter=0),
        "meta": ParagraphStyle("ManualMeta", parent=body, fontSize=8.4, leading=12, textColor=MUTED, alignment=TA_CENTER),
        "toc_title": ParagraphStyle("TOCTitle", parent=body, fontName="Helvetica-Bold", fontSize=19, leading=24, textColor=INK, spaceAfter=6 * mm),
        "table": ParagraphStyle("ManualTable", parent=body, fontSize=7.2, leading=9.2, spaceAfter=0),
        "table_head": ParagraphStyle("ManualTableHead", parent=body, fontName="Helvetica-Bold", fontSize=7.2, leading=9.2, textColor=colors.white, spaceAfter=0),
    }


def paragraph_flow(text: str, styles: dict[str, ParagraphStyle], slugs: dict[str, str]) -> Paragraph:
    return Paragraph(inline_markup(" ".join(text.splitlines()), slugs), styles["body"])


def table_flow(rows: list[list[str]], styles: dict[str, ParagraphStyle], slugs: dict[str, str], width: float) -> Table:
    count = max(len(row) for row in rows)
    normalized = [row + [""] * (count - len(row)) for row in rows]
    data = []
    for row_index, row in enumerate(normalized):
        style = styles["table_head"] if row_index == 0 else styles["table"]
        data.append([Paragraph(inline_markup(cell.strip(), slugs), style) for cell in row])
    weights = []
    for column in range(count):
        longest = max(len(re.sub(r"\s+", " ", row[column])) for row in normalized)
        weights.append(max(10, min(36, longest)))
    total = sum(weights)
    column_widths = [width * weight / total for weight in weights]
    table = Table(data, colWidths=column_widths, repeatRows=1, hAlign="LEFT")
    table.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), BLUE),
        ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("GRID", (0, 0), (-1, -1), 0.35, RULE),
        ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, PALE_GRAY]),
        ("LEFTPADDING", (0, 0), (-1, -1), 4),
        ("RIGHTPADDING", (0, 0), (-1, -1), 4),
        ("TOPPADDING", (0, 0), (-1, -1), 3.5),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 3.5),
    ]))
    return table


def parse_markdown(page: PageSource, styles: dict[str, ParagraphStyle], slugs: dict[str, str], content_width: float) -> list[Flowable]:
    flows: list[Flowable] = []
    lines = page.body.splitlines()
    i = 0
    heading_counts: dict[str, int] = {}
    page_slug = page.metadata["slug"]
    while i < len(lines):
        raw = lines[i]
        line = raw.strip()
        if not line:
            i += 1
            continue
        heading = re.match(r"^(#{1,3})\s+(.+)$", line)
        if heading:
            level = len(heading.group(1)) - 1
            title = heading.group(2).strip()
            base = page_slug if level == 0 else f"{page_slug}-{slugify(title)}"
            count = heading_counts.get(base, 0)
            heading_counts[base] = count + 1
            bookmark = base if count == 0 else f"{base}-{count + 1}"
            flows.append(BookmarkParagraph(inline_markup(title, slugs), styles[f"h{level + 1}"], bookmark, level))
            i += 1
            continue
        image_match = re.match(r"^!\[([^]]*)\]\(([^)]+)\)$", line)
        if image_match:
            asset = (page.path.parent / image_match.group(2)).resolve()
            if asset.suffix.lower() == ".svg" and asset.name == "workflow.svg":
                flows.extend([Spacer(1, 1.5 * mm), WorkflowDiagram(), Spacer(1, 1.5 * mm)])
            elif asset.exists():
                image = Image(str(asset))
                max_width = content_width
                max_height = 118 * mm
                scale = min(max_width / image.imageWidth, max_height / image.imageHeight)
                image.drawWidth = image.imageWidth * scale
                image.drawHeight = image.imageHeight * scale
                image.hAlign = "CENTER"
                flows.extend([Spacer(1, 1.5 * mm), image, Spacer(1, 1 * mm)])
            i += 1
            continue
        if line.startswith("|"):
            rows: list[list[str]] = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                cells = [cell.strip() for cell in lines[i].strip().strip("|").split("|")]
                if not all(re.fullmatch(r":?-{3,}:?", cell) for cell in cells):
                    rows.append(cells)
                i += 1
            if rows:
                flows.extend([table_flow(rows, styles, slugs, content_width), Spacer(1, 3 * mm)])
            continue
        if line.startswith("> "):
            content = line[2:]
            i += 1
            while i < len(lines) and lines[i].strip().startswith("> "):
                content += " " + lines[i].strip()[2:]
                i += 1
            background = CAUTION if content.startswith("**Caution:") else INTERPRET if content.startswith("**Interpretation:") else PALE_BLUE
            flows.extend([Callout(Paragraph(inline_markup(content, slugs), styles["callout"]), background), Spacer(1, 2.5 * mm)])
            continue
        list_match = re.match(r"^([-*]|\d+\.)\s+(.+)$", line)
        if list_match:
            ordered = list_match.group(1)[0].isdigit()
            items: list[ListItem] = []
            while i < len(lines):
                item_match = re.match(r"^([-*]|\d+\.)\s+(.+)$", lines[i].strip())
                if not item_match or item_match.group(1)[0].isdigit() != ordered:
                    break
                content = item_match.group(2)
                i += 1
                while i < len(lines) and lines[i].strip() and not re.match(r"^(#{1,3})\s+|^([-*]|\d+\.)\s+|^> |^\| |^!\[", lines[i].strip()):
                    content += " " + lines[i].strip()
                    i += 1
                items.append(ListItem(Paragraph(inline_markup(content, slugs), styles["body"]), leftIndent=3 * mm))
            flows.append(ListFlowable(items, bulletType="1" if ordered else "bullet", start="1", leftIndent=6 * mm, bulletFontName="Helvetica", bulletFontSize=8.5, spaceAfter=2.5 * mm))
            continue
        if line.startswith("```"):
            code_lines = []
            i += 1
            while i < len(lines) and not lines[i].strip().startswith("```"):
                code_lines.append(lines[i])
                i += 1
            i += 1
            flows.append(Callout(Paragraph(f'<font name="Courier">{html.escape(chr(10).join(code_lines)).replace(chr(10), "<br/>")}</font>', styles["callout"]), PALE_GRAY))
            continue
        paragraph_lines = [line]
        i += 1
        while i < len(lines) and lines[i].strip():
            candidate = lines[i].strip()
            if re.match(r"^(#{1,3})\s+|^([-*]|\d+\.)\s+|^> |^\| |^!\[|^```", candidate):
                break
            paragraph_lines.append(candidate)
            i += 1
        paragraph_text = " ".join(paragraph_lines)
        paragraph = Paragraph(inline_markup(paragraph_text, slugs), styles["caption"] if paragraph_text.startswith("*") and paragraph_text.endswith("*") else styles["body"])
        flows.append(paragraph)
    return flows


def title_page(manifest: dict, pages: list[PageSource], styles: dict[str, ParagraphStyle]) -> list[Flowable]:
    verified_dates = sorted({page.metadata["last_verified"] for page in pages})
    verified = verified_dates[-1]
    return [
        Spacer(1, 42 * mm),
        Paragraph(manifest["title"], styles["title"]),
        Paragraph("Complete user edition", styles["subtitle"]),
        Spacer(1, 6 * mm),
        HRFlowable(width="62%", thickness=1.2, color=BLUE, hAlign="CENTER"),
        Spacer(1, 10 * mm),
        Paragraph(f"FT-ITC Analysis {manifest['product_version']}", styles["subtitle"]),
        Paragraph(f"Content last verified: {verified}<br/>Generated: {date.today().isoformat()}", styles["meta"]),
        Spacer(1, 22 * mm),
        Paragraph("For ITC practitioners using the native macOS or cross-platform desktop application", styles["subtitle"]),
        Spacer(1, 32 * mm),
        Paragraph("Web-first source: ft-itc.org/manual/", styles["meta"]),
        PageBreak(),
    ]


def build(source_root: Path, output: Path) -> None:
    manifest = parse_manifest(source_root / "manual.yml")
    pages = [parse_page(source_root / item["file"]) for item in manifest["pages"]]
    slugs = {page.path.name: page.metadata["slug"] for page in pages}
    styles = make_styles()
    output.parent.mkdir(parents=True, exist_ok=True)

    doc = ManualDocTemplate(
        str(output),
        manual_title=manifest["title"],
        version=manifest["product_version"],
        pagesize=A4,
        leftMargin=20 * mm,
        rightMargin=20 * mm,
        topMargin=23 * mm,
        bottomMargin=21 * mm,
        title=manifest["title"],
        author="FT-ITC Analysis project",
        subject="User manual for FT-ITC Analysis",
        creator="Dedicated ReportLab renderer from CommonMark source",
        keywords="FT-ITC Analysis, ITC, calorimetry, user manual",
    )

    story: list[Flowable] = title_page(manifest, pages, styles)
    story.append(Paragraph("Contents", styles["toc_title"]))
    toc = TableOfContents()
    toc.levelStyles = [
        ParagraphStyle("TOC1", fontName="Helvetica-Bold", fontSize=10, leading=14, textColor=INK, leftIndent=0, firstLineIndent=0, spaceBefore=2),
        ParagraphStyle("TOC2", fontName="Helvetica", fontSize=8, leading=10, textColor=MUTED, leftIndent=12, firstLineIndent=0),
        ParagraphStyle("TOC3", fontName="Helvetica", fontSize=7.2, leading=9, textColor=MUTED, leftIndent=24, firstLineIndent=0),
    ]
    story.extend([toc, PageBreak()])

    for page_index, page in enumerate(pages):
        if page_index:
            story.append(PageBreak())
        chapter = parse_markdown(page, styles, slugs, doc.width)
        if chapter:
            first = chapter.pop(0)
            verified = Paragraph(f"Last verified: {page.metadata['last_verified']}", styles["meta"])
            story.extend([first, verified, Spacer(1, 3 * mm)])
        story.extend(chapter)

    doc.multiBuild(story)
    print(f"Wrote {output}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=SOURCE_ROOT)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    build(args.source.resolve(), args.output.resolve())


if __name__ == "__main__":
    main()
