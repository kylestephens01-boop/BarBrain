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
