# Website integration instructions

The package is designed for publication below `/manual/` without coupling it to a particular static-site generator.

## Content contract

1. Parse `manual.yml` and publish the `pages` entries in exactly that order.
2. Map the `index` slug to `/manual/`; map every other slug to `/manual/{slug}/`.
3. Parse each page's YAML front matter. Display `title`, `summary`, and `last_verified`; use `nav_order` for local navigation. Keys below `_verification` are internal metadata and must not be rendered.
4. Resolve page links by slug and asset links relative to the package root. Do not expose `verification/` or `scripts/` as public content.
5. Render blockquotes beginning with bold `Note:`, `Caution:`, `Interpretation:`, `Recommendation:`, or `Platform note:` as accessible callouts. The text must remain meaningful when callout styling is absent.
6. Preserve heading IDs or generate stable, lowercase, hyphenated IDs so intra-page anchors remain valid.
7. Use the first heading as the page heading, the `summary` as search-description text, and the `last_verified` value as reader-visible freshness information.

## Assets

Copy `assets/` into the website's manual asset pipeline without renaming files. Keep captions in the page source and alt text in image syntax. Raster assets should not be upscaled. SVG diagrams may be themed, but their text, ordering, and arrow meaning must remain intact.

## Site acceptance

Before publishing, verify:

- ordered previous/next navigation and the `/manual/` landing route;
- usable local navigation at narrow and wide viewport sizes;
- search indexing of headings and summaries;
- internal links, heading anchors, and image URLs;
- redirects from any earlier manual URL chosen by the maintainer;
- product-site typography, code, table, callout, and print styling;
- the complete PDF is linked as the offline edition;
- the website repository now contains the authoritative source.

The project wiki and bundled application help remain independent and are not synchronized with this package.

## Updating the manual

When the maintainer initiates a freshness audit, compare application changes with the commit stored in `manual.yml` and in page `_verification` metadata. Re-run only the affected task scenarios, update the corresponding matrix records, and change a page's visible `last_verified` date only after all affected procedures on that page pass. There is no fixed audit cadence or release gate.

