# Sprint 5 — Venues & Check-in
**Objective:** Gate D — the venue demo: check in, see the menu sorted into four
shelves. The money feature works end to end (minus payments, forever-out of MVP).

## In scope
- Venue model: venues (type: public|home_bar), geo (lat/lng), address, hours
  (optional), tier flag (wiki|verified — admin-set only), provenance.
- Home Bar (ADR-015): auto-created private virtual venue per user at signup
  (backfill existing); default rating location; excluded from search/discovery/
  sitemaps; rename allowed.
- Wiki contributions: add venue (dedupe via geo+name trigram → merge queue),
  add/edit menu items (venue→drink link, optional price, availability toggle,
  last-confirmed timestamp, source=crowd), rate limits, audit trail.
- Verified tier: admin flag; venue-managed menu UI (same UI, source=venue,
  badge "Verified menu"); no billing anywhere.
- Check-in: one-tap from venue page; geo-assist nearby list (distance-sorted,
  permission-optional); active check-in state (expires after N hours, flag);
  ratings made while checked in auto-tag the venue.
- Personalized menu: four shelves — Favorites / Familiar / Adventurous / New
  for You — engine output filtered to venue menu; pre-check-in teaser banner
  ("Check in to see this menu sorted for you"); graceful no-profile fallback
  (popularity + style grouping).
- QR kit: per-venue QR (deep link to venue page) + printable one-pager PDF
  generator (table tent) for founder onboarding.
- Venue discovery: nearby list page (distance-sorted; no map).
- Events: venue_added, menu_item_added, checkin, menu_viewed_personalized.

## Acceptance criteria
- E2E: create venue → add 8 menu items → check in → four shelves render
  correctly for a profiled test user (screenshots); teaser shows pre-check-in.
- Home Bar: exists for all users, default on rating flow, absent from all
  public surfaces (negative tests).
- Venue dedupe: seeded near-dupes hit merge queue; merge preserves menus.
- QR resolves to venue page; PDF one-pager generates.
- Check-in-tagged rating visible in venue's recent activity.

## Out of scope
Payments/Stripe/tiers UI, venue analytics dashboard (renewal pitch — post-MVP),
map view, GPS proximity enforcement, hours-aware features, reservations/specials.

## Gate D (founder, in person if possible, ~20 min)
Stand in a real corridor bar (or simulate): add it, add 6 real taps, check in,
judge the four shelves against your actual palate. Print the QR one-pager —
would you hand this to the owner? Approve → Sprint 6.
