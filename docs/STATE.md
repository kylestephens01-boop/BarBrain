# STATE — agent session handoff
> Agents: update this at the END of every session. Keep it short and factual.

## Current sprint
Sprint 2 — Identity & Core Rating Loop (branch `sprint-2`, PR opened into
main; Gate B review pending). Sprint 1 merged via PR #3 (Gate A cleared).
NOTE: the Sprint 1 bulk seed run on the VPS (Open Brewery DB ≥1k producers +
founder's beer.db call) is still outstanding per RUNBOOK — the corridor seed
is what's live.

## Done (Sprint 2)
- **Schema** (`Sprint2Identity`, purely additive Up): users stub extended into
  the ASP.NET Identity user on the SAME `users` table (UserName mapped onto
  Sprint 1's `Handle` column; phone/2FA columns deliberately unmapped);
  `BirthYear` + `AttestedAt` + `ActivatedAt` + `HandleChangedAt`; CHECKs:
  lowercase handles, plausible birth year, activation⇒gate-passed.
  `venues` STUB (users-stub precedent — real FK for rating location; full
  model Sprint 5): DB enforces home_bar⇒owned+private, one Home Bar per user.
  `ratings`: append-only, half-step value CHECK, visibility + location-context
  + venue-pairing CHECKs, partial unique = single latest per (user, drink).
  Identity link tables: user_logins/user_tokens/user_claims. No roles tables.
- **Auth API** (ADR-010/011): cookie session (HttpOnly, SameSite=Lax,
  401/403 not redirects; ForwardedHeaders honors Caddy's X-Forwarded-Proto).
  Email signup w/ inline DOB; Google + Apple OAuth → external cookie → DOB
  capture (`/signup/complete`) — an under-21 answer on ANY path means no
  account row ever exists; the full DOB is transient parameters only.
  AccountService is the single account-creation path (gate + Home Bar +
  signup/activation events). Soft verification: LoggingVerificationEmailSender
  (SMTP = HUMAN-CHECKLIST 6), grace window flag `auth.verification_grace_days`
  (7) — rating locks after; handle change w/ `auth.handle_cooldown_days` (30).
  Turnstile server verify: fail-closed when configured, skip+warn when not
  (keys = HUMAN-CHECKLIST 9). Mock Google/Apple providers under the REAL
  scheme names for CI/e2e (`Auth:EnableMockExternal`; startup throws in
  Production; registered only when real creds absent).
- **Ratings API** (ADR-012/026): create (verification-grace gate, merge-
  redirect-following drink resolution, transactional latest-swap), journal
  (full history, category filter, paged), note/visibility PATCH (edit ≠
  append), delete-promotes-previous, anonymous drink-page endpoint =
  latest+public only. home_bar context always resolves server-side to the
  CALLER'S Home Bar; 'venue' context accepts a venue id (contract complete;
  real venues Sprint 5). Foreign rows 404 (never 403 — no existence leak).
  Events: rating / first_rating with pseudonymous ids.
- **Web**: signup, login, /signup/complete (OAuth DOB capture), polite
  under-21 AgeStop, debounced search, drink page (36px half-step StarRating
  w/ per-half-star a11y buttons, note, Home-Bar-default location pills,
  private toggle, pour attribute bars, recent public ratings), profile w/
  journal tab (history, visibility flip, delete w/ inline confirm, filter
  pills; Palate/Matches/Badges tabs stubbed-disabled), Feed/Venues
  placeholder pages, 5-item bottom NavBar, verification banner, UserState
  mirror of /api/auth/me. Icons: Tabler-style inline SVG (official webfont
  rides with HUMAN-CHECKLIST 14 assets).
- **Tests**: AuthzTests (the founder-mandated spine: A vs B over real HTTP w/
  separate cookie jars), NoFullDobTests (schema scan + all-tables row grep),
  SignupFlowTests (boundary math incl. 21-today, no-row-on-refusal),
  ExternalOAuthTests (mock dance, under-21 leaves nothing incl. cookie),
  RatingRulesTests (append/latest/aggregate/delete/grace), Sprint2Constraint-
  Tests (DB refuses bad rows by name), MigrationFromSprint1Tests +
  MigrationFromEmpty updated. AgeGateTests run without Docker.
- **e2e**: signup→gate→rate timed <2 min w/ per-step screenshots; mock
  Google+Apple → DOB capture; under-21 blocked ×3 paths; private-flip
  vanishes from a logged-out browser. CI flips AUTH_ENABLE_MOCK_EXTERNAL for
  the e2e job; compose plumbs OAuth/Turnstile env (empty = feature off).

## In progress
(nothing — Gate B review is the blocker by design)

## Decisions made within spec bounds (log)
- Password hashing = ASP.NET Identity default (PBKDF2). The spec's
  parenthetical says "Argon2/bcrypt"; Identity's actual default is PBKDF2.
  FLAGGED in the PR rather than silently resolved — swapping in Argon2 is a
  contained IPasswordHasher change if wanted.
- Password policy: length-only (≥8), no composition rules — friction budget
  spent on the <2-min Gate B flow.
- OAuth-provided emails are treated as verified (EmailConfirmed=true) — the
  banner/grace only applies to email+password signups.
- `venues` stub table created now (Sprint 5 owns the real model): the spec's
  "nullable venue ref" gets a real FK per the ADR-026 constraint posture,
  mirroring the Sprint 1 users stub.
- Sprint 2's location UI offers "Home Bar" / "Somewhere else" (=untagged);
  the 'venue' enum value is API-complete but has no UI until venues exist.
- Design-ref Screen 1 (interest gate) deferred to Sprint 3: it has no
  persistence target this sprint and feeds the quiz; signup is the Sprint 2
  entry instead. Flag if the founder wants it earlier.
- Mock OAuth registered under the real scheme names so production code paths
  are what CI exercises; hard startup guard against Production.
- DataProtection keys are container-ephemeral → every redeploy signs everyone
  out. Acceptable at dev scale; persist keys (volume or DB) before launch.
- Events carry pseudonymous userId in properties (first-party only, ADR-017
  retention dashboard needs it). No IP, no email, no DOB anywhere near events.

## Doc inconsistency to flag (carried from Sprint 1)
- Muted-text token: BRAND.md `--bb-text-muted` vs DESIGN-REFERENCE `--bb-muted`;
  implemented canonical + alias. Founder may standardize.

## Environment note (this build machine)
Docker and Node are NOT installed here: authored-but-not-run locally =
Testcontainers integration suites + Playwright. Verified locally:
`dotnet build` (0 warnings) and `dotnet test` (35 passed / 66 skipped absent
Docker). CI runs everything for real.

## Blockers / needs founder
- **Gate B review** (phone, ~15 min): real signup on dev URL, log a real
  drink, flip a rating private, confirm it vanishes logged-out; review CI
  screenshots of the OAuth+DOB paths. Approve → Sprint 3.
- HUMAN-CHECKLIST 6–9 unblock real providers: SMTP (verification emails
  currently log-only), Google OAuth creds, Apple Developer + key, Turnstile
  keys. All config-gated — app works today without them.
- HUMAN-CHECKLIST 14 (fonts WOFF2 + logo SVGs) still open; Tabler webfont
  should ride along with it (inline-SVG icon set is the stopgap).
- Sprint 1 leftover: VPS bulk seed run per RUNBOOK + beer.db judgment call.
- Charter proposals P1–P3 (docs/project-charter.md) still await adjudication.

## Next session should
After Gate B approval + merge: Sprint 3 (onboarding quiz + palate profiles +
rec feed — pgvector similarity per ADR-025; interest gate screen 1 lands
here). If founder supplies HUMAN-CHECKLIST 6–9 creds, wire SMTP sender +
verify real Google/Apple round-trips on the dev URL. Pre-launch hardening
list so far: persist DataProtection keys; Cloudflare SSL "Full" + origin
cert; Turnstile keys mandatory.
