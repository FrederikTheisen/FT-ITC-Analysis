# FT-ITC Analysis user manual content package

This directory is the integration-ready source package for the user manual. It is written for ITC practitioners who use FT-ITC Analysis; it is not an implementation guide.

## Package contents

- `manual.yml` defines the manual identity, page order, public slugs, and verification baseline.
- `pages/` contains ordered CommonMark pages with portable YAML front matter.
- `assets/` contains publication assets and their reusable metadata.
- `verification/` is an unpublished evidence record. Do not copy this directory into the public site.
- `scripts/build_pdf.py` creates the complete A4 PDF from the ordered CommonMark source.
- `INTEGRATION.md` specifies the website-facing content contract and acceptance checks.

The source was verified against FT-ITC Analysis 1.4.3 at commit `7a19b583468b4b087e130e4b27c8140cd428339a`. The application, shared core, and tests were treated as authoritative; the project wiki and bundled help were used as supporting sources.

## Build the PDF

From the repository root, run:

```sh
python3 -m pip install -r Documentation/UserManual/requirements.txt
python3 Documentation/UserManual/scripts/build_pdf.py
python3 Documentation/UserManual/scripts/verify_pdf.py
```

The default output is `output/pdf/FT-ITC-Analysis-User-Manual.pdf`. The script accepts `--output PATH` and `--source PATH` when the package is moved.

## Editorial conventions

- UI labels are bold and match the application.
- Menu paths use `>` as a platform-neutral separator.
- Procedures are expressed as goals and actions, not screen coordinates.
- `Note`, `Caution`, `Interpretation`, `Recommendation`, and `Platform note` are blockquote callouts that require no framework-specific markup.
- Scientific recommendations are explicitly distinguished from application behavior.
- Avalonia is the canonical screenshot source. The described task must also be checked in the native macOS application before publication.
