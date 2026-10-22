# Manual source

Read `manual.yml` for chapter order and `assets/assets.yml` for the publication image inventory.

## Mathematics

Keep prose in Markdown and write mathematical symbols and expressions in LaTeX math mode throughout equations, prose, and tables:

- Use `$...$` for inline math, for example `$K_{\mathrm{d}}$`.
- Use `$$` on separate lines for display equations. Keep the `>` prefix on every line when an equation belongs to a Calculation callout.
- Use ordinary LaTeX commands such as `\frac`, `\sqrt`, `\sum`, `\int`, `\ln`, `\hat`, and `\bar`. Do not introduce custom macros or package dependencies.
- Keep variable indices italic (`$r_i$`) and descriptive subscripts upright (`$q_{i,\mathrm{raw}}$`, `$K_{\mathrm{d}}$`). Use braces to make the scope of roots, fractions, powers, and indices explicit. Use `\lvert` and `\rvert` for absolute values so Markdown table separators remain unambiguous.
- Use `aligned` inside one display equation when a long relationship needs multiple lines. Line breaks must preserve the original grouping and operations.
- Preserve literal application labels, code, filenames, and ordinary units as text. Keep scientific definitions and assumptions beside the equations; a notation conversion must not change the calculation, constants, signs, or reported quantities.

```markdown
> **Calculation:**
>
> $$
> \sigma_{q,i} = \sqrt{\sigma_{q,i,\mathrm{int}}^2 + \sigma_{q,i,\mathrm{bl}}^2}
> $$
```

Do not use HTML italics/subscripts/superscripts or Unicode approximations to construct mathematical expressions. LaTeX math is the editable source; the website publishes generated static MathML, including inline symbols. Convert it with a mathematical parser rather than translating equations by hand. For example, Pandoc 3.9 produces a chapter's HTML fragment with:

```sh
pandoc Documentation/UserManual/pages/05-processing-thermograms.md \
  --from=markdown+tex_math_dollars --to=html5 \
  --mathml --fail-if-warnings \
  --output=/tmp/processing-thermograms.html
```

The publisher places that content into the existing website layout, applies its callout/table styles, and gives display MathML the existing `manual-equation` class. Preserve the generated math structure and LaTeX annotations. Do not re-tokenize expressions, flatten fractions or roots, or require client-side equation rendering. Assemble the print edition from the same HTML before generating its PDF.

Before changing the convention or converter, inspect representative integrals, nested sums, roots, fractions, long equations, accents, charged subscripts, and inline/table symbols in website and PDF output. Check narrow-screen scrolling and print bounds, then verify the remaining equations with the same conversion.

## Screenshot assets

- The manual source includes the reviewed screenshots used by the website. Keep source references and `assets/assets.yml` aligned with the actual files.
- Application screenshots must come from real application captures or images explicitly approved by the user. Do not generate or edit UI placeholders to illustrate controls that have not been captured.
- The reviewed `fitting-injection-inclusion.webp`, `analysis-result-summary.webp`, `final-figure-workspace.webp`, and `analysis-result-exporter.webp` replace retired stale PNGs. Do not restore the old same-name `.png` files or use them during publication.
- PNG and WebP are both valid screenshot formats. Preserve reviewed images without resizing, recompressing, redrawing, or substituting them during a text-only manual sync. Match the website HTML dimensions to the images actually published.
- Inspect proposed screenshot replacements visually. A matching filename, different dimensions, or a newer timestamp does not establish that an image is current or correct.
- When a screenshot is approved in the website repository, update its authoritative manual-source counterpart and inventory so future source syncs cannot bring back an older image.
- Publish only assets marked `publication_status: publish`; held images are excluded from publication.
