# STATE — agent session handoff
> Agents: update this at the END of every session. Keep it short and factual.

## Current sprint
Sprint 4 — Matching + Hybrid Recs + Weekly Digest (branch `sprint-4`, off `main`
which now has Sprint 3 via PR #5). Gate C2 is EVAL-ONLY this sprint (charter):
the synthetic-twins MatchEval suite in CI IS the acceptance gate — no founder
feel-review until real users exist. Sprint 3 merged (PR #5); Sprint 2 (PR #4);
Sprint 1 (PR #3). STILL OUTSTANDING from Sprint 1: the VPS bulk seed run per
RUNBOOK (real-catalog feel review still wants it).

## Done (Sprint 4)
- **Docs**: sprint-4.md amended with the applied charter standing decisions +
  the eval-only Gate C2 note; ADR-007 (CF realized as density-gated blend),
  ADR-014 (blend/tiers/hide-me/display-flag detail), ADR-019 (digest impl +
  CAN-SPAM) amended; HUMAN-CHECKLIST 6 extended (digest physical address +
  SMTP gate the weekly digest; log-only until set).
- **Schema** (`Sprint4Matching`, additive): `users.HideFromMatches`,
  `users.DigestUnsubscribedAt`, `users.DigestUnsubscribeToken` (unique;
  backfilled per-row via `gen_random_uuid()` so the unique index holds on
  existing rows), and `user_match_neighbors` (per-(user,neighbor,category)
  attribute-sim + Pearson agreement + co-rated count + blended score, both
  directions materialized). Migration-from-empty count → 5; new
  MigrationFromSprint3Tests proves the Sprint3→4 upgrade + token backfill.
- **MatchService** (ADR-014/007/025): PURE `ComputeEdge` = density-weighted
  blend of preference-vector cosine (primary) + mean-centered co-rated Pearson
  (significance-shrunk, floored at min-co-rated). CF weight = coRated/(coRated+K)
  → attribute-sim dominates at low density, CF grows with density. Confidence
  tier (low/med/high) from co-rated depth. `ComputeAllAsync` full-rebuilds the
  graph (includes hidden users; hide-me enforced on READ). Read surfaces:
  `GetMatchesAsync` (aggregates per-category edges → one row/neighbor, honors
  hide-me both directions + display-mode flag) and `LovedByMatchesAsync` (feed
  + social proof). All knobs are flags (`match.*`).
- **MatchNightlyService**: nightly rebuild (hour 10, after palate recompute),
  flag-gated. **Endpoints**: GET /api/matches, GET/PUT match settings
  (hide-me), PUT digest subscription, unauthenticated CAN-SPAM
  GET /api/digest/unsubscribe?token= (full-page HTML, under /api/* so the SW
  leaves it alone), + admin POST /api/admin/matches/rebuild and /digest/run
  (Gate C2 / e2e triggers; admin-token gated).
- **Feed hybrid** (RecommendationService): "Loved by your matches" is now a
  REAL 4th section (was ComingSoon). CF nudges Up-Your-Alley scores toward
  match-loved drinks (density-gated — cold users get pure content, so the
  golden RecEval is unchanged). Social proof (`LovedByMatchCount/Handle`) rides
  on every rec. RecDto gained two optional trailing fields.
- **Weekly digest** (ADR-019): `DigestComposer` (recap / ADR-016-safe
  weekly-distinct-drink streak / top pick per section / match hook — each
  block flag-gated), `DigestRenderer` (pure, inline-styled, two-temperature,
  CAN-SPAM footer w/ physical address + unsubscribe), `DigestService.RunOnceAsync`
  (compose→render→send, `digest_sent` events), `WeeklyDigestService` (weekly
  schedule). `IDigestSender`=LoggingDigestSender (log-only, mirrors Sprint 2
  verification). CAN-SPAM guard: NEVER delivers without a configured physical
  address (`digest.physical_address`) — log-only until then.
- **Gate C2 eval** (`Category=MatchEval`, own pgvector container): planted
  twin pairs among decoys → asserts top-1 match = twin for ≥90%; density
  weighting (pure-math test runs everywhere + seeded low/high-density pairs);
  hide-me both directions; match-% flag toggles display; sparsity sim at
  50/500 users → CI artifact `match-eval-report.md`. Digest covered by
  DigestServiceTests (block flags, unsubscribe, no-address log-only guard) +
  DigestRendererTests (litmus HTML artifact, no-volume-copy assertion).
- **Web**: `/matches` page (handle, teal %, confidence label, early-estimate
  caption, amber recent-loves; hidden + empty states; NO interaction —
  one-way), feed matches section + teal social-proof line + "Loved by matches"
  pill, Profile "Privacy & email" section (hide-me + digest toggles), Matches
  nav link. New token-based CSS only.
- **e2e**: matching.spec (two like-palates match → eager shows %, conservative
  hides it via a live flag flip, hide-me removes both directions), digest.spec
  (litmus screenshot + CAN-SPAM footer), onboarding-feed.spec updated (matches
  tab now live+empty for a solo user).

## In progress
(nothing — Gate C2 is the CI eval by design; no human review until real users)

## Decisions made within spec bounds (log)
- Match neighbors materialized per-(user,category); "Your Matches" aggregates
  to one row/neighbor on read (evidence-weighted mean, weight = 1+coRated),
  confidence from the best category's co-rated depth.
- Co-rating agreement clamps negatives to 0 for the blend (we don't surface
  anti-matches), but stores the raw shrunk Pearson for auditing.
- Hide-me enforced on READ (immediate both directions) rather than only in the
  nightly batch; the batch still includes hidden users so un-hiding is instant too.
- CF hybrid nudges score WITHIN distance bands (band assignment stays
  attribute-distance based) → a user with no matches sees an unchanged feed,
  which is what keeps the Sprint 3 golden eval green.
- Digest streak = consecutive rolling-7-day buckets with ≥1 rating (ADR-016
  weekly-only), shown only at ≥2 weeks; never per-serving/per-day.
- Digest unsubscribe token backfilled with gen_random_uuid() in the migration
  (per-row) so the new unique index holds and tokens aren't guessable.

## Doc inconsistency to flag (carried)
- Muted-text token: BRAND.md `--bb-text-muted` vs DESIGN-REFERENCE `--bb-muted`
  (alias in place; founder may standardize).

## Environment note (this build machine)
Docker/Node absent: authored-not-run locally = Testcontainers suites (incl. the
new MatchEval + DigestService collections) + Playwright. Verified locally:
`dotnet build` (api/web/tests, 0 warnings), `dotnet test` (38 passed / 86
skipped absent Docker; the new pure density-weighting math test runs locally).
CI runs everything for real and uploads match-eval-report.md +
rec-eval-report.md + digest-litmus.html.

## Blockers / needs founder
- **HUMAN-CHECKLIST 6 (now gates the digest)**: set `digest.physical_address`
  (CAN-SPAM) + wire an SMTP provider before the weekly digest can send to real
  users. Until then it is log-only by design (guard enforced in DigestService).
  Flip `digest.enabled` on once both exist.
- **Gate C2**: eval-only this sprint — the MatchEval CI suite + sparsity report
  ARE the acceptance. When real users exist: check Your Matches on real/persona
  accounts, flip `match.display_mode`, read the sparsity report, receive a
  digest (admin POST /api/admin/digest/run?force=true previews it, log-only).
- Carried: VPS bulk seed run (RUNBOOK); HUMAN-CHECKLIST 7–9 (Google/Apple/
  Turnstile creds), 14 (fonts/logo assets), charter proposals P1–P3, beer.db call.

## Next session should
After Gate C2 (CI green) + merge: Sprint 5 (venues — check-in, personalized
menus; ADR-015). The match graph + digest jobs are flag-gated and nightly; tune
`match.*` / `feed.cf_weight_pct` on the real catalog once the bulk seed lands.
