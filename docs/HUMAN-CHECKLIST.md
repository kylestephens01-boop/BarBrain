# Human-only checklist (agents cannot do these)
Est. ~45–60 min total. Items 1–5 block Sprint 0; the rest block later sprints.

1. GitHub: create private repo `barbrain` (personal acct). Add this docs pack.
   Enable branch protection on main (PRs required, CI required). [Sprint 0]
2. Hetzner: account + CX22 VPS (Ubuntu 24.04). Note IP. Add SSH key. [Sprint 0]
3. Cloudflare: add domain, point `dev.barbrain.co` → VPS IP (proxied). Enable
   Cloudflare Access (or basic auth) on dev until the trademark knockout clears.
   [Sprint 0]
4. Domains (if not already done): barbrain.app/.io/.ai + getbarbrain.com
   (+ mybarbrain.com if free). [NOW — predates everything]
5. GH Actions secrets: VPS_HOST, VPS_SSH_KEY, GHCR token. [Sprint 0]
6. Transactional email provider (free tier: Resend/Brevo/SES). API key → secrets.
   PLUS a non-home physical mailing address for email footers (CAN-SPAM
   requires one): virtual mailbox or registered-agent service address. [Sprint 2]
7. Google Cloud console: OAuth client (web). Redirect URIs for dev + prod.
   [Sprint 2]
8. Apple Developer ($99/yr): enroll, configure Sign in with Apple service ID +
   key. [Sprint 2]
9. Cloudflare Turnstile: site + secret keys. [Sprint 2]
10. Object storage for backups (Hetzner Storage Box or Backblaze B2): bucket +
    credentials. [Sprint 7]
11. Attorney: trademark knockout search (Classes 9/35/42/43 + the Class 45
    matching question). GATE for public use of the name. [parallel, Weeks 0–4]
12. Attorney: one-time Iowa alcohol-law review (age gating, future coupon loop,
    ToS/PP draft review — one engagement). [before public launch]
13. Iowa LLC: Certificate of Organization ($50) + operating agreement; org
    transfer of repo at formation. RECOMMEND registered-agent service
    (~$125/yr) for address privacy — interacts with the CAN-SPAM footer
    address (item 6). [before public launch]
14. Brand v1 EXISTS (brand guide, June 2026). Remaining: SVG logo masters
    (wordmark, mark, single-node) + WOFF2 font files into the repo per
    docs/BRAND.md. Public brand use stays gated on the knockout.
    [Sprint 0 for assets; post-knockout for public use]
