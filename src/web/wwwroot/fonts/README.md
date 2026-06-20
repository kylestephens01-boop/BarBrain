# Self-hosted fonts (WOFF2 only)

These files are a **human deliverable** (HUMAN-CHECKLIST item 14) and are not yet
committed. The app references them via `wwwroot/css/fonts.css` with
`font-display: swap`, so until they exist the UI falls back to the system-ui
stack in the `--bb-font-*` tokens — nothing breaks, the brand typefaces just
aren't applied.

## Required files (exact names — `fonts.css` expects these)

| File                      | Family        | Weight |
|---------------------------|---------------|--------|
| `space-grotesk-400.woff2` | Space Grotesk | 400    |
| `space-grotesk-500.woff2` | Space Grotesk | 500    |
| `inter-400.woff2`         | Inter         | 400    |
| `inter-500.woff2`         | Inter         | 500    |

## Rules (docs/BRAND.md)
- WOFF2 only. **Never** load fonts from a CDN.
- Exactly two weights each (400/500). No other weights, no other typefaces.
- Subset to the glyphs we use to keep payload small; both are OFL-licensed.
- `index.html` preloads `space-grotesk-500.woff2` and `inter-400.woff2`
  (the above-the-fold weights). If you rename files, update the preloads too.

Once the four files are here, the `@font-face` rules in `fonts.css` light up
automatically — no code change needed.
