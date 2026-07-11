# Sprint 7 — Pre-Launch Hardening
**Objective:** Gate E — the launch checklist. Boring on purpose.

## In scope
- Privacy self-serve: JSON export (ratings, profile, check-ins, badges);
  deletion flow w/ user choice (ADR-018): FULL DELETE vs ANONYMIZE
  (contributions reassigned to anonymous, PII rows purged either way);
  grace period (7 days, flag); confirmation email.
- Backups: nightly encrypted pg_dump → object storage (checklist item 10),
  30-day retention, RESTORE DRILL: scripted restore to a scratch container,
  smoke-verified, documented in RUNBOOK.md (drill output = CI/PR artifact).
- Security pass: authz negative-test suite across every endpoint (matrix in
  repo); dependency audit (dotnet list package --vulnerable + npm audit) clean
  or waived w/ notes; headers/CSP; secrets scan; VPS hardening verified
  (script re-run idempotent); Postgres unreachable externally (probe test).
- Age-gate audit: all three signup paths re-verified incl. OAuth DOB capture;
  full-DOB-absence assertion; under-21 path screenshots; copy sweep for
  consumption-encouraging language ("drink responsibly" footer present).
- Monitoring: uptime ping on /health (external), error tracker w/ PII scrub
  config proven (synthetic error → no PII in event), structured logs verified,
  alert rules: down >2min, error-rate spike. Alert → founder email.
- Analytics dashboard (ADR-017): admin page — signups, activation rate,
  D1/D7/D30 cohort retention, WAU, ratings/week, check-ins/week. SQL views
  documented. Kill-threshold numbers visible at a glance.
- Legal placeholders: ToS + Privacy Policy drafts (attorney-flag comments
  inline), 21+ statement, contact/report mechanism linked in footer.
- RUNBOOK.md complete: deploy, rollback, restore, incident basics, weekly ops
  (10-min founder routine).
- Live-catalog rec-quality eval verb (founder-scoped 2026-07-10; launch-gate
  trigger): CLI verb computing Precision@10 with the synthetic golden-set
  personas against the LIVE catalog, strictly READ-ONLY — no synthetic
  writes to production data (that constraint is why the fixture harness
  cannot be pointed at the VPS). Reference: Gate C1 fixture baseline was
  0.71 (fixture and live numbers are compared, not equated).

## Acceptance criteria
- Export downloads valid JSON; both deletion paths work e2e (verify DB state).
- Restore drill artifact: dump → restore → smoke green, timed.
- Authz matrix suite green; vulnerability scans clean/waived.
- Synthetic error visible in tracker with PII scrubbed (screenshot).
- Dashboard renders cohorts from real dev data; kill thresholds annotated.
- Founder receives a test alert on phone.
- Eval verb runs on the VPS via the existing exec pattern (RUNBOOK `… <cmd>`
  form), touches no production rows, and prints a single comparable
  Precision@10 number.

## Out of scope
Marketing site, app stores, payments, anything new beyond the scoped
live-catalog eval verb — feature freeze otherwise.

## Gate E (founder, ~30 min)
Walk the launch checklist (generated as docs/LAUNCH.md): export your data,
delete a test account both ways, read the restore-drill log, trigger the test
alert, review dashboard. If all green: flip DNS to the launch domain, post in
the first local channel. That's launch.
