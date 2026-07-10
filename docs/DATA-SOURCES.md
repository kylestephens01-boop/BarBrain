# DATA-SOURCES — external dataset license registry (BINDING)

> **The gate (ADR-024):** No external dataset is ingested — locally, in CI, or in
> prod — until it has an entry here with its exact upstream URL and license,
> and that license is compatible with a closed-source revenue app. ODbL or any
> share-alike license is INCOMPATIBLE. Ambiguous licensing = STOP and flag the
> founder in STATE.md. Competitor databases and curated competitor content
> (Untappd, Vivino, Distiller, RateBeer, …) are NEVER sources (Hard Rule 1),
> regardless of license.

Verification quotes below were captured live on 2026-07-06.

## Approved sources

### Open Brewery DB — producers ONLY (ADR-020)
- **Upstream:** https://github.com/openbrewerydb/openbrewerydb (bulk CSV in-repo)
- **License:** MIT. Pre-approved by founder with attribution.
- **Use:** `import openbrewerydb --file <downloaded csv>` seeds **producers only**
  (the dataset has no products). Attribution line ships in the app's data
  credits when a public about/credits surface exists (tracked in STATE.md).
- **Provenance tags:** `source = "seed:openbrewerydb"`, `source_ref` = OBDB id.

### TTB COLA (Certificate of Label Approval) registry — sample batch + stub
- **Upstream:** https://ttbonline.gov/colasonline (US federal government work)
- **License:** US government work — public domain (17 U.S.C. § 105) / CC0-
  equivalent. Pre-approved by founder.
- **Use:** `import ttb-sample --file <csv>` ingests a small committed sample
  extract; the full extraction pipeline is deferred background work (Sprint 1
  spec). Provenance: `source = "seed:ttb"`, `source_ref` = TTB ID.

### BJCP style guidelines — FACTS ONLY, no text (decision A / ADR-023)
- **Upstream reference:** https://www.bjcp.org/bjcp-style-guidelines/
- **License posture:** The guideline PROSE is copyrighted and is
  **incompatible** with this app. We ingest **zero descriptive text**. We use
  only non-copyrightable facts: style names, style codes (e.g. 21A), and
  structural numeric ranges (ABV/IBU/SRM/OG/FG). Flavor attribute baselines in
  `style_attributes` are **BarBrain-original editorial data**, not derived from
  BJCP text. The `styles` table deliberately has NO description column; rich
  text may be added later only under explicit permission.
- **Provenance tags:** `source = "seed:barbrain-styles"` (the seed file is our
  own authored data; BJCP is a factual reference only).

### beer.db — the openbeer / geraldb project (LICENSE OK; QUALITY CAVEAT)
- **Upstream:** https://github.com/openbeer — specifically
  https://github.com/openbeer/us-united-states
- **License:** Public domain by author declaration. Org description: "Open
  Public Domain Beer, Brewery n Brewpub Data". Repo README: "Free open public
  domain beer, brewery n brewpub data for the United States of America."
  No formal CC0 LICENSE file was found, but the PD declaration is explicit and
  consistent; no ODbL terms anywhere. Verified 2026-07-06.
- **DATA QUALITY FLAG (founder attention):** the US dataset appears frozen at
  ~2012–2013. License-safe but very stale — most corridor-relevant products
  won't exist in it. Ingesting is *permitted*; whether it is *worth it* is a
  founder call. The importer exists and is fixture-tested; it only runs when
  invoked explicitly with a local checkout path.
- **Provenance tags:** `source = "seed:beerdb"`, `source_ref` = upstream key.

### Corridor priority list — founder/BarBrain-authored
- **Upstream:** none (first-party). `src/api/seed/corridor-priority.json`.
- **License:** our own data. Producer/product names and ABVs are facts entered
  from general knowledge and producer-published information — never from
  competitor databases.
- **Provenance tags:** `source = "seed:corridor"`.

## Prohibited sources (named explicitly to prevent confusion)

### openbeerdb.com — "Open Beer Database" — DO NOT USE
- **Upstream:** https://openbeerdb.com/
- **License:** ODbL 1.0 + Database Contents License. Site wording, verified
  2026-07-06: "This Open Beer Database data is made available under the Open
  Database License. Any rights in individual contents of the database are
  licensed under the Database Contents License."
- **Why prohibited:** ODbL is share-alike — ingesting it would obligate
  publishing our derived database. **This is a DIFFERENT project from the
  openbeer/geraldb beer.db above. Do not conflate them.** Any importer,
  script, or doc pointing at openbeerdb.com is a bug.

### Competitor databases — Untappd, Vivino, Distiller, RateBeer, BeerAdvocate, …
- Prohibited under Hard Rule 1 regardless of technical accessibility or any
  claimed license. No scraping, no imports, no "reference" copies.

## Adding a source (checklist)
1. Find the authoritative license statement (LICENSE file / data page).
2. Add an entry here with URL, exact license, quoted wording + capture date.
3. Share-alike (ODbL/CC-BY-SA/GPL-data) or ambiguous → STOP, flag in STATE.md,
   wait for founder sign-off.
4. Only then write/point the importer, tagging every row with `source` +
   `source_ref`.

> **Machine-enforced for product seeds (ADR-028):** this file is embedded into
> the api binary at build time, and `import products --file <…>` refuses any
> seed file whose `source` tag does not appear here (fail-closed — a missing
> registry also refuses). The gate matches the QUOTED tag, so include it
> exactly as `source = "seed:whiskey-national"` (quotes required) in the new
> entry, then rebuild/redeploy before importing.
