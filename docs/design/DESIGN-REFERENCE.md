# DESIGN-REFERENCE.md — Visual Contract for Agents

> Approved by founder 2026-06-17. Screenshots in docs/design/screens/.
> This file is BINDING — build toward these screens. When in doubt, match the
> screenshot. If a sprint spec conflicts with this reference, flag it in STATE.md.

## Global rules (apply to every screen)

- **Dark-only MVP.** Background = --bb-ink (#141A24). No light mode.
- **Tokens from docs/BRAND.md are the single styling source.** No hardcoded colors.
- **Two-temperature grammar:** amber (--bb-pour) = beverage/drink objects only;
  teal (--bb-synapse) = intelligence/recommendation objects only. NEVER swapped.
  A drink card the user merely logged = amber accent. The same card surfaced
  by the recommender = teal accent.
- **Fonts:** Space Grotesk 400/500 (display, headings, stat numbers) +
  Inter 400/500 (everything else). Self-hosted WOFF2 only. No CDN. No other
  weights, no other typefaces.
- **Icons:** Tabler outline icons only (webfont). No emoji anywhere in the product.
  Decorative icons get aria-hidden="true"; icon-only buttons get aria-label.
- **Tap targets:** 44px minimum. One-handed, low-light use assumed.
- **Cards:** --bb-surface background, --bb-radius corners, 12-14px padding.
- **Border-left accents on drink cards:** 3px, teal or amber per grammar.
- **Stars:** outlined empty state visible from the start (stroke #4A5568);
  filled = --bb-pour. Never invisible before interaction.
- **Nav bar:** 5 items — Home (ti-home), Feed (ti-sparkles), Search (ti-search),
  Venues (ti-map-pin), Profile (ti-user). Active = --bb-synapse.
- **Prohibited language:** see docs/BRAND.md. Zero volume/frequency framing.

---

## Screen 1: Onboarding — Interest gate
**Sprint:** 2 (signup) + 3 (quiz)
**File:** screens/onboarding-gate.png

### Layout
- Full-screen dark. H1 "What are you into?" in Space Grotesk.
- Subtitle in --bb-muted.
- Three category cards, stacked vertically:
  - Beer: ti-beer icon in amber-tinted square (rgba pour 0.12)
  - Whiskey: ti-glass icon in amber-tinted square
  - Wine: ti-glass-full icon in bitters-tinted square (rgba bitters 0.12)
- Each card: icon box (40x40, 10px radius) + name (Space Grotesk 15px 500) +
  description (11px muted) + circular checkbox (22px, muted border → synapse
  fill + check on select). Card border goes synapse on select.
- Primary button "Let's go" at bottom, disabled until ≥1 selected.

### Component: CategoryCard
Props: icon, iconClass, name, description, selected.
Selected state: border-color synapse, checkbox filled synapse with ✓.

---

## Screen 2: Onboarding — Quiz
**Sprint:** 3
**File:** screens/onboarding-quiz.png

### Layout
- Progress dots at top (done=pour, active=synapse, pending=surface).
- H2 "Quick beer check" + subtitle.
- Scrollable list of QuizDrinkCards.
- Counter "N rated" + primary button "Next category" / "See your palate."

### Component: QuizDrinkCard
- Surface card, row layout: drink info (name 14px 500, style 11px muted)
  on left; right column = stars row + "Haven't tried" text link below.
- Stars: 5x outlined stars (26px tap target, 20px SVG). Tap fills left-to-right
  in pour amber. MUST be visible as outlines before any interaction.
- "Haven't tried": 11px muted text link, right-aligned under stars. On tap,
  clears stars and shows italic "Skipped" in pour color. Not a button — no
  border, no background.

---

## Screen 3: Onboarding — First radar
**Sprint:** 3
**File:** screens/onboarding-radar.png

### Layout
- H2 "Your palate is taking shape" + subtitle.
- Centered radar chart SVG (240x240). 8 spokes, 3 concentric grid rings
  (surface stroke), filled area (synapse, 25% opacity), stroke line (synapse),
  dots at vertices (synapse, 3.5px radius). Spoke labels 10px muted.
- Below radar: stat rows (strongest/weakest dimension in pour, ratings count).
- Primary button "Explore your feed →".

### Component: RadarChart
Props: labels (string[8]), values (number[8] 0-1).
Grid: 3 rings at 33/66/100%. Always 8 spokes.
Colors: grid=surface, fill=synapse@0.25, stroke=synapse, dots=synapse.

---

## Screen 4: Rec feed
**Sprint:** 3-4
**File:** screens/feed.png

### Layout
- H2 "Your feed" + settings icon (ti-adjustments-horizontal, muted) top-right.
- Subtitle "Based on N ratings across [categories]."
- Tab row: Up your alley (ti-target-arrow), Stretch a little (ti-compass),
  Wildcard (ti-dice-3, amber active state), From your matches (ti-users).
  Active tab: synapse underline (or amber for Wildcard). Count badge per tab.
- Filter pills below tabs: All, Beer, Whiskey, Wine, Near me. Active =
  synapse bg 12%, synapse border, synapse text.
- Content area shows cards for the active tab only (not all sections stacked).

### Component: DrinkRecCard
- Surface card with 3px left border (teal for rec sections, amber for Wildcard).
- Top row: name (14px 500 coaster) + match % (Space Grotesk 12px synapse) if present.
- Detail line: style · ABV (11px muted).
- Optional tag pill: "Cross-category" (synapse bg, ti-arrows-cross), "3 twins
  love this" (synapse bg), "New territory" (pour bg, ti-sparkles).
- Reason text: 11px muted, with key attributes bolded in accent color
  (synapse for teal cards, pour for amber cards). Separated by 1px top border.

---

## Screen 5: Venue list + check-in + personalized menu
**Sprint:** 5
**Files:** screens/venue-list.png, screens/venue-precheckin.png, screens/venue-menu.png

### Venue list
- H2 "Venues nearby" + subtitle.
- Search bar: surface bg, ti-search icon, placeholder text.
- VenueCards stacked: icon box (ti-beer, pour-tinted, 40x40), name (15px 500),
  meta line (type · verified badge if applicable), distance right-aligned.
- Verified badge: synapse bg 12%, synapse text, ti-circle-check icon, 10px.
- QR prompt card at bottom: ti-qrcode icon (28px muted), explanatory text.

### Pre-check-in venue page
- Back arrow + venue name as header.
- Venue info row (icon, type, address, tap count, verified badge or
  "Community-managed" italic).
- Check-in prompt: dashed synapse border, lock icon, "Check in to unlock your
  menu" CTA, primary button with ti-map-pin-check icon.
- Below: flat unsorted menu list (surface cards, name + style/ABV + price,
  no personalization signals). "N more on tap" truncation.

### Personalized menu (post-check-in)
- Checked-in banner: synapse gradient bg, check icon in synapse circle,
  "Checked in / Menu sorted for your palate."
- Shelf tabs: Favorites, Familiar, Adventurous, New for you. Active underline
  (amber for Favorites, synapse for others).
- Menu items per shelf: surface cards with left border accent.
  - Favorites: amber border, your star rating shown, "Your favorite" tag.
  - Familiar: teal border, match % where available.
  - Adventurous: teal border, "Cross-category" tag where applicable.
  - New for you: amber border, "New territory" tag.
  - All items: name, style·ABV, price right-aligned, reason text.

---

## Screen 6: Drink page
**Sprint:** 2 (rating) + 3 (attributes/recs)
**File:** screens/drink-page.png

### Layout
- Back arrow + drink name (H2) + style/ABV/producer subtitle.
- Rating card: large stars (36px target, 28px SVG, centered), optional note
  textarea (surface bg, placeholder), location selector row (Home bar default
  active in pour tint, "At a venue" toggle).
- Flavor profile card: "Flavor profile" label, horizontal attribute bars
  (8 rows: name 70px right-aligned, bar bg rgba white 0.06, filled bar in
  pour amber, value in Space Grotesk 11px).
- Recommendation card (teal label): reason text with bolded synapse attributes,
  match %, social proof line with mini avatar + "N palate matches rated this."
- Recent ratings card: pseudonymous handle + note + stars per entry.

### Component: AttributeBars
Props: attributes ({name, value}[], value 0-10).
Bar color: pour amber (on drink pages), synapse teal (on profile/palate pages).

---

## Screen 7: Journal / profile
**Sprint:** 2 (journal), 4 (matches), 6 (badges)
**File:** screens/profile.png

### Layout
- Profile header: avatar circle (initials, synapse bg 15%), handle in Space
  Grotesk 18px, stats line (ratings · categories · badges), settings icon.
- Tab row: Journal, Palate, Matches, Badges.

### Journal tab
- Category filter pills (All, Beer, Whiskey).
- JournalItem rows: category icon box (32px, category-tinted), name (13px 500),
  meta (style · location · when), stars right-aligned. Tappable → drink page.

### Palate tab
- Category label, radar chart (same RadarChart component), attribute bars
  below in teal. "Needs N more ratings" prompt for inactive categories.

### Matches tab
- MatchCards: avatar circle (initials, synapse), handle (14px 500), description
  (11px muted), match % (Space Grotesk 16px synapse). Tappable.
- "Hide me" toggle at bottom (ti-eye-off icon).

### Badges tab
- Streak card: large streak number (Space Grotesk 28px pour), title + subtitle,
  flame icon (ti-flame, pour).
- Earned badges: circular icon containers (40px, pour-tinted bg, pour icon).
- Locked badges: same layout, surface bg, #4A5568 icon.
- Labels: 10px muted, centered under each badge.

---

## Component inventory (reusable across screens)

| Component | Used in | Key props |
|-----------|---------|-----------|
| CategoryCard | Onboarding | icon, name, desc, selected |
| QuizDrinkCard | Onboarding quiz | name, style, rating, skipped |
| StarRating | Quiz, drink page, journal | size (sm/lg), value, interactive |
| RadarChart | Onboarding, profile | labels[], values[], size |
| AttributeBars | Drink page, profile | attrs[], color (pour/synapse) |
| DrinkRecCard | Feed, venue menu | name, detail, reason, match%, tag, borderColor |
| VenueCard | Venue list | name, type, dist, verified |
| ShelfTabs | Venue menu, feed | tabs[], activeIdx |
| FilterPills | Feed, journal | options[], activeIdx |
| MatchCard | Profile, drink page | initials, handle, desc, pct |
| BadgeIcon | Profile | icon, label, earned |
| NavBar | All screens | activeTab |
