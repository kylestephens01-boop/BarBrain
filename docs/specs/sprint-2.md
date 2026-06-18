# Sprint 2 — Identity & Core Loop
**Objective:** Gate B — a stranger signs up and logs their first drink in <2 min.

## In scope
- ASP.NET Identity: email+password (Argon2/bcrypt via Identity defaults),
  Google OAuth, Apple OAuth. Post-OAuth DOB capture step (ADR-011) before
  activation. Cloudflare Turnstile on signup.
- 21+ gate: DOB entry, validate ≥21, persist birth_year + attested_at ONLY
  (ADR-010). Under-21 → polite hard stop, no account.
- Soft email verification: full use immediately; banner until verified; account
  limited (cannot rate) after 7 days unverified (config flag).
- Handles: unique, pseudonymous, validation, change w/ 30-day cooldown (flag).
- Rating flow: search → drink page → 0.5-step stars → optional note → location
  context selector (Home Bar default — STUB Home Bar as concept now, full venue
  model is Sprint 5; store context enum + nullable venue ref) → save.
- Visibility: public-pseudonymous default, per-rating private toggle (ADR-012).
- Journal: my ratings list, category filter, edit/append re-rating (history kept),
  delete own rating.
- Drink page: info, attribute radar (static render), recent public ratings.
- Events instrumentation: signup, activation, first_rating, rating (ADR-017).

## Acceptance criteria
- E2E: fresh email signup → DOB → quiz-less first rating in ≤6 screens;
  Playwright timed run <2 min; screenshots of every step.
- E2E: Google + Apple flows (mocked providers in CI) hit DOB capture; under-21
  blocked on all three paths.
- Authz tests: user A cannot read B's private ratings via API (negative tests).
- Verification: full DOB provably absent from DB (schema + assertion test).
- Re-rating appends; journal shows history; latest drives drink page aggregate.

## Out of scope
Quiz (Sprint 3), recs, matching, venues/check-in proper, digest, password-less.

## Gate B (founder, phone, ~15 min)
Real signup on dev URL from your phone; log a real drink; flip one rating
private; confirm it vanishes from the drink page in a logged-out browser.
Review CI screenshots of OAuth+DOB paths. Approve → Sprint 3.
