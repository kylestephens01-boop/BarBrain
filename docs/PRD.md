# BarBrain — Product Requirements (MVP)

## One-liner
Rate what you drink; BarBrain learns your palate per category, recommends what to
try next (including across categories), quietly finds your palate twins, and sorts
any participating venue's menu into what YOU will love.

## Users
- Drinkers (21+): beer, whiskey/bourbon, wine enthusiasts and casuals. Pseudonymous.
- Venues (later-paying): bars/taprooms/restaurants in the Cedar Rapids–Iowa City
  corridor first. MVP = free wiki tier + manually-flagged verified tier. No payments.

## Core loop
rate → profile → recommend → match → (check in → personalized menu) → rate…

## Feature areas

### Catalog
Canonical drinks: (producer, product name, category). Package format = rating
metadata. Wine vintage = rating metadata (single entity per wine). Styles taxonomy
(BJCP-derived) per category; every style carries a baseline attribute vector;
drinks inherit + override with provenance (manufacturer/crowd/LLM/moderator) and
confidence. ABV = metadata + dedup signal. User submissions → merge queue
(pg_trgm fuzzy match) → moderator review. Seeding: BJCP styles; Open Brewery DB
(producers ONLY — it has no products); beer.db products; TTB COLA extraction;
corridor-priority product list (local-depth strategy: what is actually poured in
CR/Iowa City — macros, regionals, locals — before national breadth).

### Identity & accounts
Email+password, Google, Apple ("Sign in with Apple" required later in native app —
built now). OAuth flows capture DOB post-auth before activation. Hard 21+ DOB gate;
persist birth year + attestation timestamp ONLY. Pseudonymous unique handle,
optional display name, changeable with cooldown. Soft email verification: rate
immediately, verify within 7 days. Cloudflare Turnstile on signup.

### Ratings & journal
1.0–5.0 in 0.5 steps + optional text note (no photos in MVP). Pseudonymous-public
by default with per-rating private toggle. Every rating has a location context:
venue, Home Bar, or untagged — defaults to Home Bar, geo-suggests nearby venues,
remembers last. Re-rating appends history (journal is a log); engine uses most
recent. Journal filterable by category; palate radar per category.

### Palate engine
Per-category profile = preference-weighted average of rated drinks' attribute
vectors (weighted by rating minus the user's own mean). Min 5 ratings per category.
8 attribute dims per category; 6-dim cross-category bridge (sweetness, bitterness/
tannin, body, smoke, fruit, acidity). Onboarding: conversational interest gate
("Into beer? → 45-sec quiz of recognizable staples; haven't-tried skips") per
claimed category; interest flags stored. Recs are content-based v1 (pgvector
cosine, unrated filter, popularity prior, diversity pass) presented as a
SECTIONED feed: "Up Your Alley" / "Stretch a Little" / "Wildcard" — confidence-
adaptive. Every rec shows its "because" (attribute explanation). Cross-category
recs via bridge = Sprint 3 stretch.

### Matching (passive social)
User-user CF (Pearson, mean-centered, per category, nightly batch, min co-rated
+ shrinkage). Match score = blend of attribute-profile similarity and co-rating
agreement (density-weighted). Display: named matches (handle + %) with a hide-me
toggle; one-way only — no DM, no follow, no friend graph. Match % shows day one
labeled "early estimate" — BEHIND A CONFIG FLAG with a conservative mode for
scale. Surfaces: drink pages, rec-card social proof, Your Matches panel, and a
4th feed section "Loved by Your Matches". Weekly email digest (no push in MVP):
recap, streak, picks, match hooks.

### Venues & check-in
Wiki tier: any user adds venues/menu items (provenance, rate limits, venue merge
queue). Verified tier = admin flag, manually granted during founder onboarding;
NO payment processing in MVP. Menu item: venue→drink + optional price +
availability + last-confirmed + source. Personalized menu on check-in: four
shelves — Favorites / Familiar / Adventurous / New for You (same engine,
venue-filtered); pre-check-in teaser: "Check in to see this menu sorted for you."
Check-in = manual one-tap + geo-assisted nearby sort; NO GPS proximity
requirement in v1 (revisit when badges make it gameable). QR kit per venue links
to its menu page (also the founder's table-tent sales prop). Ratings made while
checked in tag the venue. Home Bar: auto-created PRIVATE virtual venue per user;
default rating location; excluded from all discovery. (Backlog: Home Bar
library/inventory — "what should I pour from my own shelf.") Venue discovery =
distance-sorted list; no map in MVP.

### Gamification
Badge framework is DATA-DRIVEN (criteria in config; new badges without deploys).
Categories: breadth (styles/categories tried), exploration (wildcards tried),
venue variety, contribution (drinks added, menus confirmed, merges accepted),
weekly streaks. HARD RULE: nothing rewards consumption volume/frequency — weekly
("logged something this week") streaks only; distinct-drinks counts only.
CUT from MVP: leaderboards (fast-follow at density), photos, XP/points.

### Moderation & data quality
Ratings count instantly for the user's own profile; count toward public averages
and CF only after account ≥7 days AND ≥5 ratings (config-tunable). Server-side
rate limits. Admin: drink/venue merge queues, report queue, rating-anomaly
review, shadow-limit/ban.

### Privacy & compliance
Drinking data = sensitive. Pseudonymous by default; aggregate-only external
sharing ever. Self-serve export (JSON). Deletion: user chooses full delete vs
anonymize-contributions (PII deleted either way). First-party analytics ONLY:
events table in our Postgres + admin retention dashboard (D1/D7/D30 cohorts,
WAU); no third-party trackers, no cookie banner needed. 21+ everywhere;
"drink responsibly" footer; zero consumption-encouraging copy.

### Design & brand
Brand guide v1 governs (docs/brand + docs/BRAND.md). DARK-ONLY MVP. --bb-*
tokens are the single styling source; amber accents beverage objects, teal
accents intelligence objects, never swapped. Prohibited-language list binds
all copy. Self-hosted fonts only.

## Launch posture
Open signup; hyper-local marketing only (corridor). PWA only — no app stores in
MVP (sidesteps alcohol-policy review during validation).

## Success metrics & kill thresholds
D30 retention >3% good / 7–8% excellent (accelerate). Weekly actives + session
depth are the honest measures. Data-asset metrics instrumented alongside retention (moat posture: data
first, venues as proof): ratings/user, % users active in 2+ categories,
attribute-confidence coverage, cross-category rec engagement. Kill/pivot:
venues won't convert free→paid after dense local launch; D30 well under 3%
despite gamification; or an incumbent ships polished cross-category palate
matching first — RE-EVALUATE rather than auto-fold if the venue book is
renewing or dataset depth supports acquisition conversations.

## Cut list (explicit, MVP)
Coupons/points redemption · friend graph/DMs/follows · native apps & app-store
submission · photos · leaderboards · XP/levels · payments/Stripe · map view ·
web push · Home Bar library · GPS-verified check-ins · manufacturer analytics.
