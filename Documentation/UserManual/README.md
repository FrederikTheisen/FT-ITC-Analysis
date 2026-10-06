# Manual source

Read `manual.yml` for chapter order and `assets/assets.yml` for the publication image inventory.

## Screenshot assets

- The manual source includes the reviewed screenshots used by the website. Keep source references and `assets/assets.yml` aligned with the actual files.
- Application screenshots must come from real application captures or images explicitly approved by the user. Do not generate or edit UI placeholders to illustrate controls that have not been captured.
- The reviewed `fitting-injection-inclusion.webp`, `analysis-result-summary.webp`, `final-figure-workspace.webp`, and `analysis-result-exporter.webp` replace retired stale PNGs. Do not restore the old same-name `.png` files or use them during publication.
- PNG and WebP are both valid screenshot formats. Preserve reviewed images without resizing, recompressing, redrawing, or substituting them during a text-only manual sync. Match the website HTML dimensions to the images actually published.
- Inspect proposed screenshot replacements visually. A matching filename, different dimensions, or a newer timestamp does not establish that an image is current or correct.
- When a screenshot is approved in the website repository, update its authoritative manual-source counterpart and inventory so future source syncs cannot bring back an older image.
- Publish only assets marked `publication_status: publish`; held images are excluded from publication.
