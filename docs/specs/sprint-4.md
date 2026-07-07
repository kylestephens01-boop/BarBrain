# Sprint 4 — Matching & Hybrid Recs
**Objective:** palate twins exist, the match % shows with honesty controls,
CF strengthens the feed. Gate C2 = matching eval green.

## In scope
- Nightly CF batch: per-category user-user Pearson on mean-centered vectors;
  min co-rated (default 3) + shrinkage; top-K neighborhoods materialized.
- Match score (ADR-014): blend of attribute-profile cosine + co-rating agreement,
  weights shift toward co-rating as overlap grows (density-weighted; weights in
  config). Confidence tier computed (low/med/high).
- Display flag (ADR-006/014): mode=eager → show % from day one labeled "early
  estimate" at low confidence; mode=conservative → % only at med+ confidence.
  Ship in EAGER. Hide-me toggle (user setting) excludes a user from all match
  surfaces both directions.
- Surfaces: Your Matches panel (handle, %, confidence label, their recent loves);
  drink-page "strong matches loved this"; rec-card social proof; 4th feed
  section "Loved by Your Matches".
- Hybrid: CF score blended into Up Your Alley ranking (fallback ladder: cold →
  pure content; dense → blended; weights in config).
- Weekly digest: email template + sender job (provider from checklist item 6);
  blocks: week recap, streak, top picks per section, match hook ("a strong
  palate match was found" / "your match tried X"). Per-block config flags;
  unsubscribe honored; events instrumented.
- Eval extensions (CI): planted synthetic twins → top-1 match = twin for ≥90%
  of personas; LOO hit-rate@10 improves vs Sprint 3 content-only baseline on
  dense synthetics; sparsity simulation report (UX at 50 / 500 users: % of
  users with ≥1 med-confidence match) as CI artifact.

## Acceptance criteria
- Two seeded similar accounts see each other w/ sane % + label; hide-me removes
  both directions immediately.
- Flag flip eager↔conservative changes display with no deploy (e2e proves).
- Digest renders correctly (litmus screenshot), sends to a test inbox, respects
  block flags + unsubscribe.
- Eval suite + sparsity report green/attached in CI.

## Out of scope
DMs/follows/any interaction between matches; push; venue data in digest.

## Gate C2 (founder, phone, ~15 min)
Check Your Matches on your real + persona accounts; flip the display flag and
confirm; read sparsity report ("what will launch month feel like"); receive the
digest email. Approve → Sprint 5.

> **Gate C2 is EVAL-ONLY this sprint (charter, July 2026).** Matching cannot be
> human-reviewed with one real user. The synthetic-twins eval IS the acceptance
> gate and runs in CI: planted twins are found (top-1 match = twin for ≥90% of
> personas), density-weighting behaves (attribute similarity dominates at low
> co-rating density — the current reality), hide-me removes a user from match
> results both directions, and the match-% display flag toggles display. No
> founder feel-review is expected until real users exist; the phone task above
> is deferred to whenever that is.

## Applied standing decisions (charter, July 2026)
Recorded here so the spec reflects what shipped; none of these override an ADR.

- **CF finally enters — as a density-gated BLEND partner only** (ADR-025's
  deferred-CF upgrade path activating; ADR-007's Pearson design realized).
  Nightly user-user Pearson (mean-centered, per category, min-co-rated +
  shrinkage) is blended with attribute-profile similarity, density-weighted:
  attribute similarity DOMINATES when co-rating density is low (today's
  reality); the CF weight grows as density grows. CF is never the sole signal.
- **Named matches are ONE-WAY only**: a user sees who matches their palate
  (handle + match %). No DM, no follow, no friend graph, no mutual-consent
  handshake, no messaging (reaffirms Out of scope). ADR-014 holds.
- **Hide-me** is a user setting that removes a user from all match surfaces in
  BOTH directions, effective immediately (enforced on read, not only in the
  nightly batch).
- **Match-% display is a feature flag** (`match.display_mode`): eager (labeled
  "early estimate" at low confidence) vs conservative (% only at med+
  confidence). Ships defaulting to EAGER per charter.
- **Weekly email digest ONLY** (no push — MVP has none, ADR-019). Verification/
  digest links are full-page navigations under `/api/*`, which the Sprint 2
  service-worker fix already excludes from SPA interception (confirmed).
- **CAN-SPAM**: the digest footer carries a physical mailing address (a
  founder-provided config value — HUMAN-CHECKLIST item 6/15) and an unsubscribe
  mechanism. Until a real address is configured the digest is LOG-ONLY, the
  same pattern as Sprint 2's verification emails.
