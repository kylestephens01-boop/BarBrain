// PWA icon pipeline (Sprint 6). DORMANT until the SVG logo masters land in
// docs/brand/ (HUMAN-CHECKLIST item 14; founder ruling 2026-07-10: ship wired
// but dormant, placeholders stay meanwhile).
//
// When docs/brand/mark.svg exists, this generates per docs/BRAND.md:
//   src/web/wwwroot/icon-192.png            (any)
//   src/web/wwwroot/icon-512.png            (any)
//   src/web/wwwroot/icon-512-maskable.png   (mark at 80% safe zone on --bb-ink)
//   src/web/wwwroot/favicon.png             (single-node variant if present)
//
// Usage (CI does this automatically): npm install --no-save sharp
//                                     node infra/generate-icons.mjs
//
// AFTER first real generation: switch the manifest's maskable entry from
// icon-512.png to icon-512-maskable.png (this script prints a reminder).

import { existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const mark = resolve(root, 'docs/brand/mark.svg');
const singleNode = resolve(root, 'docs/brand/mark-single-node.svg');
const out = (name) => resolve(root, 'src/web/wwwroot', name);

const INK = '#141A24'; // --bb-ink (BRAND.md); never any other background

if (!existsSync(mark)) {
    console.log('generate-icons: docs/brand/mark.svg not in repo yet (HUMAN-CHECKLIST 14).');
    console.log('generate-icons: keeping placeholder icons — nothing to do.');
    process.exit(0);
}

const { default: sharp } = await import('sharp');

// Full-bleed "any" icons.
for (const size of [192, 512]) {
    await sharp(mark).resize(size, size, { fit: 'contain', background: INK })
        .flatten({ background: INK })
        .png().toFile(out(`icon-${size}.png`));
}

// Maskable: mark occupies the central 80% safe zone on solid ink.
const inner = Math.round(512 * 0.8);
const pad = Math.round((512 - inner) / 2);
const markPng = await sharp(mark).resize(inner, inner, { fit: 'contain', background: INK }).png().toBuffer();
await sharp({ create: { width: 512, height: 512, channels: 4, background: INK } })
    .composite([{ input: markPng, top: pad, left: pad }])
    .png().toFile(out('icon-512-maskable.png'));

// Favicon: the single-node variant (BRAND.md: under 32px use single-node).
const faviconSource = existsSync(singleNode) ? singleNode : mark;
await sharp(faviconSource).resize(48, 48, { fit: 'contain', background: INK })
    .flatten({ background: INK })
    .png().toFile(out('favicon.png'));

console.log('generate-icons: icons regenerated from SVG masters.');
console.log('generate-icons: REMINDER — point the manifest\'s maskable entry at icon-512-maskable.png.');
