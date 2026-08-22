# Audit and acceptance record

## Product baseline

| Item | Recorded value |
| --- | --- |
| Product version | 1.4.3 |
| Verification commit | `7a19b583468b4b087e130e4b27c8140cd428339a` |
| Commit subject | `additional tests` |
| Commit date | 2026-08-21 |
| Verification date | 2026-08-22 |
| Host | macOS 15.7.4 (24G517), arm64 |
| .NET SDK | 10.0.302 |

## Evidence hierarchy used

1. Running Avalonia build and native macOS build.
2. Shared core and interface source at the recorded commit.
3. Automated reader, processing, fitting, result, export, and persistence tests.
4. Repository README and current wiki checkout.
5. Bundled help text.

Claims that differed between an older secondary source and current source or UI were written to the current behavior. Platform names were removed unless task completion truly differs.

## Cross-platform editorial review

- Procedures name controls or goals instead of screen positions.
- The four experiment views are named **Overview**, **Process Data**, **Analyze Data**, and **Final Figure**.
- Avalonia is the screenshot source; the native application uses the same core behaviors and corresponding task controls.
- Installation and print services have explicit platform notes because the operating system affects completion.
- Integrated-heat imports are explicitly separated from thermogram processing.

## Content acceptance

- [x] Twelve ordered pages and stable route slugs.
- [x] Required public front matter and hidden verification metadata.
- [x] All matrix features mapped to a page or marked outside scope.
- [x] Framework-neutral callouts and CommonMark content.
- [x] Screenshot captions and alt text.
- [x] Integration and freshness instructions.
- [x] Automated structure and link validation recorded after build.
- [x] PDF render, page inspection, extraction, links, bookmarks, and metadata recorded after build.

The final verification log records commands, results, and the known two-site benchmark caveat.
