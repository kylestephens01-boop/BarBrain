# SEED-FORMAT — generic product-seed files (`import products`)

> Introduced in sprint 4.6 (ADR-028). `corridor-priority.json` is one instance
> of this format; any first-party product batch (e.g. a national whiskey
> catalog) is another. The importer is `CatalogImportService.ImportProductsAsync`,
> exposed as `… import products --file <path>` (see RUNBOOK). This doc was
> expected to predate the implementation (an earlier analysis pass was to have
> produced it); it did not exist, so it was authored alongside the code.

## File shape

```jsonc
{
  // REQUIRED. Provenance tag written to every producer/drink row. Must start
  // with "seed:" and MUST have an entry in docs/DATA-SOURCES.md — the
  // importer refuses unregistered tags (ADR-024, fail-closed). Never reuse an
  // existing tag for a different dataset: (source, ref) is the idempotency
  // key, so a reused tag silently merges into the other dataset's rows.
  "source": "seed:whiskey-national",

  // OPTIONAL. Confidence (0–1) for this file's attribute-override rows.
  // Omitted → the catalog.seed_override_confidence_pct flag decides
  // (default 80 → 0.80).
  "attributeConfidence": 0.85,

  "producers": [
    {
      "ref": "buffalo-trace",          // REQUIRED, stable — idempotency key; never rename
      "name": "Buffalo Trace Distillery",
      "type": "distillery",            // brewery|distillery|winery|cidery|meadery|multi|other
      "city": "Frankfort", "region": "KY", "country": "US",
      "drinks": [
        {
          "ref": "bt-eagle-rare",      // REQUIRED, stable per source — idempotency key
          "name": "Eagle Rare 10 Year",
          "category": "whiskey",       // beer|whiskey|wine
          "style": "WH-AM-BRB",        // style CODE (preferred) or exact name; unknown → warn, import unstyled
          "abv": 45.0,

          // OPTIONAL editorial attribute overrides. SHORT keys (the importer
          // prefixes the category → "whiskey.oak"), values 0–1 on the same
          // scale as style baselines — bridge dims therefore stay on the
          // cross-category scale automatically. Dims NOT listed here inherit
          // from the style baseline exactly like a drink with no block.
          "attributes": { "oak": 0.8, "sweetness": 0.55 }
        },
        { "ref": "bt-flagship", "name": "Buffalo Trace", "category": "whiskey",
          "style": "WH-AM-BRB", "abv": 45.0 }   // no block → pure inheritance
      ]
    }
  ]
}
```

JSON comments and trailing commas are tolerated (same parser settings as all
bundled seeds).

## Semantics

- **Provenance**: every row gets `Source = <file's tag>`, `SourceRef = ref`.
  Re-runs upsert on the `(Source, SourceRef)` partial unique indexes — no
  duplicates, ever. Changed names/styles/ABVs update in place; unchanged rows
  are counted `unchanged`.
- **Overrides** land in `drink_attributes` as **`source = 'moderator'`** rows:
  of the schema's allowed set (`inherited | manufacturer | crowd | llm |
  moderator`, SCHEMA.md), moderator is the one that means *a human curator's
  editorial judgment*, which is exactly what a founder-authored seed override
  is (see ADR-028 for the full justification). Confidence = file's
  `attributeConfidence`, else the flag default.
- **Inheritance fallback**: the vector sync only materializes `inherited` rows
  for dims a drink *lacks*, so an override wins its dim and every other dim
  behaves exactly as before. Full 8-dim coverage (override + inherited) is
  required before a vector is written — same rule as always.
- **Removing an override later does NOT revert the row** — the importer only
  manages keys present in the file (it never deletes, so it cannot clobber
  future moderation-UI edits). To change a value, edit it; to genuinely revert
  a dim to inheritance, that's a manual moderation action.
- **Malformed overrides fail the whole run loudly** (unknown attribute key,
  value or confidence outside 0–1). Seed files are first-party editorial data;
  warn-and-skip would silently skew the palate engine. Drink-level problems
  keep the corridor importer's forgiving behavior (invalid category → skip +
  warn; unknown style → import unstyled + warn).
- **License gate (ADR-024, fail-closed)**: the source tag must appear in
  docs/DATA-SOURCES.md, which is embedded into the api binary at build time.
  Registering a new source therefore requires a rebuild/redeploy — which a new
  data batch needs anyway.

## Authoring rules (binding)

- First-party facts only: names, ABVs, and style mappings from general or
  producer-published knowledge. **Never** from competitor databases (Hard
  Rule 1). Attribute override values are BarBrain-original editorial data.
- `ref` values are permanent. Renaming one orphans the old row and creates a
  duplicate on the next run.
- Register the tag in DATA-SOURCES.md (URL/first-party statement, license,
  capture date) BEFORE authoring data against it.
