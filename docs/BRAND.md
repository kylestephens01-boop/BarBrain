# BRAND — agent-consumable distillation (full guide: docs/brand/ PDF)

## Tokens (single source of truth)
--bb-ink: #141A24 (last call — default app background; dark-first)
--bb-surface: #1F2937 (back bar — cards, inputs)
--bb-pour: #E8A23B (amber — BEVERAGE objects only)
--bb-synapse: #46C0B0 (teal — INTELLIGENCE objects only: match %, recs, New for You)
--bb-coaster: #F2EDE3 (light base; primary text on dark)
--bb-bitters: #C2554A (destructive/warning, semantic only; large text only on ink)
--bb-text-muted: #8A94A6
--bb-font-display: 'Space Grotesk' (400/500) · --bb-font-ui: 'Inter' (400/500)
--bb-radius: 12px

## Grammar
Amber means drink. Teal means thinking. Never swapped, never decorative.
A logged drink card = amber; the same card surfaced by the recommender = teal.
MVP ships DARK-ONLY. Light mode deferred pending its own contrast pass.
Wordmark exception (deliberate, documented): "Bar" coaster, "Brain" pour amber.

## Type scale
Display 40/44 SG500 · H1 28/34 · Stat 24/28 (hero stats MAY use Display) ·
H2 20/26 · Body Inter 15/24 · Label Inter-500 13/18 · Caption 12/16

## Contrast (CI-enforced against tokens, incl. SURFACE pairs)
WCAG AA: 4.5:1 body, 3:1 large. Known: pour/ink 8.4:1, synapse/ink 8.6:1,
coaster/ink 14.7:1, bitters/ink large-only. CHECK muted-on-surface in CI.

## Logo
SVG masters only; raster icons (192/512/maskable) generated at build.
Under 32px: single-node variant. Clear space = height of "B". Never stretch,
rotate, outline, shadow, gradient, or re-typeface.

## Prohibited language (HARD RULES — all surfaces)
No volume/quantity framing ("crush," "binge," "power hour"). No intoxication
references or jokes, including euphemisms. No consumption-count badge names
("Explorer," never "Power Drinker"). No "drink more" CTAs or implied urgency
around alcohol. No health/wellness claims about alcohol.
No proof/ABV-as-potency framing — ABV and proof are data fields, never
selling points or intensity brags ("rocket fuel", "hits harder").

## Taglines
PRIMARY: "Know what you'll love." (store subtitle, landing hero, lockups)
SECONDARY: "Can't get your AI drunk — but it can help you." — landing-page
personality ONLY. Never in-app. Never in store listings.

## THE GATE
No public brand use (store listing, landing page, paid design) until the
trademark knockout clears + ITU files. Dev site stays behind auth until then.
If the gate fails: coined-mark fallback (Likli/Savry); tokens/type/voice survive.
