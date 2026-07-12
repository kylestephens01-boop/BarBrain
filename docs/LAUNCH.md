# LAUNCH — the Gate E checklist (Sprint 7)

> Walk this top to bottom (founder, ~30 min). Every unchecked box blocks the
> DNS flip. Items marked [HC n] point at docs/HUMAN-CHECKLIST.md.

## The walk (do these live, on the dev site)

- [ ] **Export your data**: Profile → Your data → Download. Open the JSON:
      profile, ratings, check-ins, badges present; birth YEAR only.
- [ ] **Delete a test account, both ways** (create two throwaways):
      anonymize → public rating survives under `anonymous_*`; full delete →
      sign-in dead, rows gone. Verify via a fresh signup that the handles are
      free/anonymized as expected. (Set `privacy.deletion_grace_days=0` on
      dev to watch execution; put it back to 7.)
- [ ] **Read the restore-drill log** from the latest CI run (artifact
      `restore-drill-log`) — or run `./infra/restore-drill.sh` on the VPS.
- [ ] **Trigger the test alert**: temporarily set
      `monitoring.error_spike_threshold=1`, hit a bad request path, confirm
      the alert email (or its log line, pre-SMTP). Reset the flag.
- [ ] **Review the dashboard**: `/admin/analytics` renders cohorts from real
      dev data; kill (<3%) / excellent (≥7%) annotations visible.
- [ ] **Rec quality**: latest CI `rec-eval-report` artifact — Live
      Precision@10 sane vs the 0.71 fixture baseline trend.

## Infrastructure gates

- [ ] Cloudflare TLS **Flexible → Full** + origin cert (ARCHITECTURE.md).
- [ ] External uptime monitor live, "down > 2 min" rule → founder [HC 15].
- [ ] `./infra/probe.sh <host>` from a non-VPS machine: only 80/443 answer.
- [ ] `BACKUP_PASSPHRASE` set on the VPS + copy stored off-box; object
      storage + `RCLONE_REMOTE` for off-box backups [HC 10/15].
- [ ] SMTP provider + CAN-SPAM physical address; `digest.physical_address`
      + `monitoring.alert_email` set; verification/digest emails deliver
      [HC 6].
- [ ] Turnstile keys set (bot check active on signup) [HC 9].
- [ ] Google + Apple OAuth creds set, both flows walked once [HC 7/8].
- [ ] CI green on main: build+tests, Playwright, Lighthouse PWA ≥ 0.9,
      Security workflow (audits + gitleaks), backup drill.

## Legal / brand gates

- [ ] Trademark knockout CLEAR + ITU filed [HC 11] — or coined-mark fallback
      decision executed (BRAND.md THE GATE).
- [ ] Attorney pass on ToS/Privacy drafts (every [FLAG] resolved) + Iowa
      alcohol-law review [HC 12]; mailing address into /legal/contact.
- [ ] Iowa LLC formed; repo transferred to the org [HC 13; ADR-005].
- [ ] SVG logo masters + WOFF2 fonts landed; icon pipeline ran; placeholder
      icons gone [HC 14].
- [ ] Cloudflare Access removed from the launch hostname (dev stays gated).
- [ ] VPS bulk seed run per RUNBOOK (corridor + whiskey-national +
      beer-national + Open Brewery DB producers) and `report` output sane.

## The flip

- [ ] Launch TLD decided (post-knockout call: .co vs alternative).
- [ ] DNS → launch domain; `/health` + `/version` green through Cloudflare.
- [ ] Post in the first corridor channel. That's launch.
