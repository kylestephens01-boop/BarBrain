# STATE — agent session handoff
> Agents: update this at the END of every session. Keep it short and factual.

## Done (2026-07-15 session — outage fix, branch `sprint-7-fix-email-failopen`)
- **Incident**: merging PR #22 took dev down (502) — the VPS .env had
  SMTP_HOST but no EMAIL_FROM, and the Email:From startup fail-fast
  crash-looped the api; deploy health check red; no rollback step exists, so
  the broken container stayed up. Restore = set the five SMTP_*/EMAIL_FROM
  vars in /opt/barbrain/infra/.env + `up -d api` (RUNBOOK "Transactional
  email").
- **Fix (PR #23)**: Production now degrades half-configured email to the
  log-only senders with one ERROR log line (EmailMisconfigurationAlert);
  dev/CI keep the fail-fast. Test pins the prod degrade path.
- **Follow-ups surfaced, NOT done**: (a) infra/backup.sh dies on line 53
  (`set -u` + bare `$1`) whenever run in loop mode — the backup service has
  been crash-looping since abe2441, so NIGHTLY BACKUPS ARE NOT RUNNING;
  needs `${1:-}` guard. (b) deploy.yml has no rollback on failed health
  check — a bad deploy strands the broken stack.

## Done (2026-07-14 session — SMTP email wiring, branch `sprint-7-fix-email-smtp`)
- **Real SMTP sender (HUMAN-CHECKLIST 6)**: `src/api/Email/` — MailKit-backed
  `SmtpEmailClient` + SMTP implementations of all three email interfaces
  (verification, deletion confirmation, digest/alerts). `AddBarBrainEmail()`
  picks SMTP vs the existing logging stubs off `Email:Smtp:Host` — dev/CI
  behavior unchanged (empty = log-only). Digest sender honors the CAN-SPAM
  `deliver:false` contract.
- **Config convention settled**: infra/.env vars `SMTP_HOST/SMTP_PORT/
  SMTP_USERNAME/SMTP_PASSWORD/EMAIL_FROM` → compose maps to `Email__*` → the
  `Email` section. `RESEND_API_KEY` / raw `Email__Smtp__*` in .env are NOT
  read; RUNBOOK + .env.example document this. EmailWiringTests prove binding,
  DI selection, fail-fast on missing From, and a real send against a loopback
  SMTP server (no Docker needed).
- Resend creds (smtp.resend.com:465, sender noreply@barbrain.co) live on the
  dev VPS `.env`; founder to normalize var names there + confirm a real
  delivery post-merge.

## Current sprint
Sprint 7 — pre-launch hardening (branch `sprint-7`, off `main` after PR #14
merged the charter adjudication). Spec: docs/specs/sprint-7.md (includes the
founder-scoped live-catalog eval verb). History: Sprint 6 (PR #13); charter
adjudication (PR #14); Sprint 5 (PR #12); data-beer-national (PR #11);
4.8/4.7/4.6/4.5 (PRs #10/9/8/7); 4 (PR #6); 3 (PR #5); 2 (PR #4); 1 (PR #3).
STILL OUTSTANDING from Sprint 1: the VPS bulk seed run per RUNBOOK.

## Done (this session — Sprint 7, code-complete pending CI)
- **Privacy self-serve (ADR-018)**: GET /api/account/export (profile, full
  rating history, check-ins, badges as a JSON download); deletion flow with
  mode choice — FULL DELETE (own rows removed; shared public catalog rows go
  OWNERLESS, never cascading into other users' ratings) vs ANONYMIZE (public
  contributions stay under a scrubbed `anonymous_*` handle); PII purged both
  ways; events jsonb userId scrubbed; moderation_actions untouched (audit
  survives, Sprint 6 design honored). Grace window is flag
  `privacy.deletion_grace_days` (7); PrivacyNightlyService executes due
  deletions; confirmation email via LoggingAccountEmailSender (HC 6 pattern).
  Migration `Sprint7Privacy` (additive: DeletionRequestedAt/Mode + CHECKs).
  Profile "Your data" card: export link, mode radio + password confirm,
  pending banner with cancel. PrivacyFlowTests + privacy.spec.ts.
- **Live-catalog eval verb (founder-scoped)**: `dotnet BarBrain.Api.dll eval
  recs [--out]` — archetype personas (fixture parity), REAL profile+rec
  pipeline, Precision@10 vs top-quartile over unrated eligible drinks, all
  inside a transaction that ALWAYS rolls back (no commit path exists).
  Categories <20 drinks are skipped with a note. LiveRecEvalTests pins a
  parseable number AND zero surviving rows. CI e2e job runs it via the
  compose exec pattern and uploads rec-eval.md.
- **Authz matrix (route-level perimeter)**: every endpoint classified
  Anon/User/Admin in AuthzMatrixTests; completeness fails on unclassified OR
  stale entries; anon→401 on all User routes; missing/wrong/user-cookie
  tokens→401 on all Admin routes (Admin:Token pinned — the stub allows-all
  when empty). Ownership 404-posture stays in AuthzTests.
- **Headers/CSP**: Caddy header block — CSP (wasm-unsafe-eval; 'unsafe-inline'
  script-src is a DOCUMENTED COMPROMISE for the build-injected import map —
  follow-up: hash it at publish; Turnstile origin allowed), HSTS, nosniff,
  frame DENY, Referrer-Policy, Permissions-Policy (geolocation=self).
  e2e security-headers.spec.ts asserts headers AND that the app boots with
  zero CSP violations. ComposeHardeningTests pins ports:[] / loopback / the
  header block (Hard Rule 8 file guards).
- **CI security**: security.yml — NuGet vulnerable-package audit (fail on
  hit), npm audit (high), gitleaks full-history secrets scan; weekly cron.
  dependabot.yml was an EMPTY-ecosystem no-op — now nuget+npm+actions.
  infra/probe.sh = external exposure probe (80/443 only).
- **Age-gate audit**: existing coverage re-verified complete (3 signup paths,
  under-21 e2e screenshots, NoFullDob schema+whole-DB assertions). NEW:
  WebCopyLintTests — word-boundary BRAND.md lint over ALL web copy + pins
  the footer statement (the copy sweep is now a permanent gate).
- **Legal + footer**: /legal/terms + /legal/privacy (DRAFT, inline
  attorney [FLAG]s), /legal/contact (report pointer, SAMHSA line, address
  pending HC 6/13); MainLayout footer: "adults 21 and over. Drink
  responsibly." + links. legal-footer.spec.ts screenshots.
- **Analytics dashboard (ADR-017)**: GET /api/admin/analytics — signups,
  events-funnel activation, D1/D7/D30 cohorts ("active" = rated or checked
  in, NOT page views), WAU, ratings/checkins per ISO week, PRD data-asset
  metrics (ratings/active user, % in 2+ categories); kill/excellent
  thresholds as flags (analytics.d30_kill_pct=3 / d30_excellent_pct=7, PRD).
  /admin/analytics page (teal semantic accents). SQL documented verbatim in
  docs/ANALYTICS.md. AnalyticsEndpointTests plants a D1 cohort.
- **Backups**: compose `backup` service (same pg image) — nightly
  pg_dump|gzip|AES-256, 30-day prune, tiny-dump guard, optional rclone
  off-box (HC 10; on-box-only is logged nightly). Prod overlay REFUSES the
  dev passphrase. infra/restore-drill.sh → scratch container + smoke +
  timed log. CI backup-drill job runs the full cycle per PR, uploads
  restore-drill.log.
- **Monitoring**: ErrorTrackingExceptionHandler → `error` events (PII
  scrubbed, path-only, NO userId — operational data survives deletion);
  PiiScrubber (emails, credentials-to-EOL); ErrorRateAlertService (flags
  monitoring.*, one alert/hour, via IDigestSender — logs until SMTP);
  Production logs = JSON console. /api/debug/throw test-only endpoint
  (config-gated, Production-blocked). MonitoringTests prove planted PII is
  absent from the stored event; alert fires then throttles. HC 15 added
  (EXTERNAL uptime monitor for down>2min — a down box can't email).
- **RUNBOOK complete**: backup/restore (+incident restore), monitoring,
  weekly 10-min founder ops, probe, eval verb. **docs/LAUNCH.md** = Gate E
  walk + infra/legal gates + the DNS flip.
- Tests: 79 pass locally (no Docker; 135 skipped = Testcontainers +
  container-bound suites — CI is the done gate). Build 0 warnings.

## Deferred within Sprint 7 (noted for the PR)
- CSP script-src keeps 'unsafe-inline' for Blazor's build-injected import
  map — follow-up: hash the import map at publish and drop it.
- JSON-log "verified" = config + RUNBOOK; no automated Production-env boot
  test (WebApplicationFactory boots Development).
- gitleaks-action needs a (free personal) license var if the repo moves to
  the org — recheck at the ADR-005 transfer.

## Doc inconsistency to flag (carried)
- Muted-text token: BRAND.md `--bb-text-muted` vs DESIGN-REFERENCE
  `--bb-muted` (alias in place; founder may standardize).
- Charter settled v7 text still uncommitted — founder holds it; repo file
  carries the adjudication record (P1–P3 approved 2026-07-10).

## Environment note (this build machine)
Docker/Node absent: Testcontainers + Playwright suites authored-not-run
locally; CI runs them — CI green is the done gate. Verified locally: build
0 warnings; `dotnet test` 79 passed / 135 skipped.

## Backlog (unscheduled — revisit on a concrete trigger, not speculatively)
- beer.db rejection is docs-only; registry has no rejected-source semantics.
  Trigger: next importer-code touch — rejected flag, loud refusal, CI test.
- Events table has no user_id column (jsonb only) — was fine; the Sprint 7
  dashboard shipped WITHOUT per-user event queries (aggregates come from
  ratings/checkins tables), so the trigger did NOT fire. Unchanged.
- Admin auth is still the Sprint 0 token stub (outside every sprint spec so
  far); the authz matrix now pins its perimeter behavior.

## Blockers / needs founder
- HUMAN-CHECKLIST 15 (NEW): external uptime monitor on /health;
  monitoring.alert_email flag; BACKUP_PASSPHRASE on the VPS + off-box copy.
- Carried: HC 14 (SVG masters + WOFF2 — icon pipeline dormant); HC 6 (SMTP +
  physical address — gates digest AND deletion-confirmation + alert email
  delivery); HC 7–9 (OAuth/Turnstile creds); HC 10 (object storage →
  RCLONE_REMOTE); VPS bulk seed run; beer-national VERIFY-ABV backlog;
  Cloudflare TLS Flexible→Full before launch (ARCHITECTURE.md; LAUNCH.md).
- Gate E: walk docs/LAUNCH.md after this PR merges.

## Next session should
- Watch Sprint 7 PR CI (new jobs: backup-drill + Security workflow + the
  rec-eval artifact ride the existing e2e job); fix red if any.
- Then Gate E (founder, ~30 min): walk docs/LAUNCH.md top to bottom — export,
  both deletion modes on throwaways, restore-drill log, test alert,
  dashboard review. The infra/legal gates (TLS Full, uptime monitor,
  knockout, LLC, SMTP…) are founder-paced; launch is the DNS flip at the end.
