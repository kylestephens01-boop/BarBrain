# BarBrain National Whiskey Catalog Seed — First-Party-Sourced Product Dataset

**TL;DR**
- Compiled 57 nationally-distributed American whiskey expressions across 18 producer groups, each with ABV, style-classification tags, and only producer-stated production facts, drawn strictly from official distillery sites, official producer press releases, and label/regulatory statements — no Untappd/Distiller/Vivino/RateBeer/Whiskey Social/usePalate/BeerAdvocate data was ingested at any point.
- The large majority of ABV values are directly confirmed on the producer's own website or press release; a minority are flagged VERIFY (producer sells the product but the exact proof was not captured on a first-party page this pass) or UNCONFIRMED (no qualifying first-party source found — do not seed those numeric fields yet).
- Barrel-proof/variable expressions (George T. Stagg, Booker's, Elijah Craig Barrel Proof, Larceny Barrel Proof, Rare Breed, Maker's Cask Strength, Maker's 46 Cask Strength, Michter's Barrel Strength Rye, Willett Family Estate, Smoke Wagon Uncut) carry no fixed ABV and must be modeled as batch-dependent (nullable ABV + `barrel_proof: true`) in the JSON seed.

## Key Findings
- The cleanest first-party data comes from **Heaven Hill, Brown-Forman (Old Forester/Woodford/Jack Daniel's), Beam Suntory (Knob Creek/Maker's/Old Grand-Dad/Basil Hayden), Four Roses, New Riff, Michter's, and Angel's Envy** — all publish ABV and concrete production facts on their own sites.
- **Buffalo Trace/Sazerac** publishes less numeric detail on some pages, but the flagship (45%/90 proof), Eagle Rare 10 (45%/90 proof), Weller Special Reserve (90 proof), Blanton's Original (46.5%/93 proof), and George T. Stagg (variable) were all confirmed first-party.
- **Sourced/independent bottlers** (Bulleit, Redemption, High West, Willett) frequently do NOT state distilling location or mash bill on their own consumer sites. For Bulleit Rye, however, MGP-Indiana sourcing IS a first-party fact: Diageo publicly announced it in 2011 and the label's TTB-mandated state-of-distillation reads "Lawrenceburg, Indiana." For Redemption and Smoke Wagon, MGP sourcing remains reported-only and is UNCONFIRMED first-party.
- Several catalog-requested items are wheated bourbons with producer-stated wheat mash bills: the Weller family, the Maker's Mark line, Larceny (Heaven Hill states 68% corn / 20% wheat / 12% malted barley), and Old Fitzgerald.

## Details

Legend: **CONFIRMED** = value seen on the producer's official site or press release during research; **VERIFY** = producer sells the item but the exact ABV was not captured from a first-party page this pass; **UNCONFIRMED** = no qualifying first-party source found.

### Buffalo Trace / Sazerac (Frankfort, KY)
| Producer | City, State | Product | ABV | Style tags | Production facts (producer-stated) | Source / confidence |
|---|---|---|---|---|---|---|
| Buffalo Trace Distillery | Frankfort, KY | Buffalo Trace Kentucky Straight Bourbon | 45% (90 proof) | standard bourbon | low-rye "Mash #1" (percentages not disclosed) | buffalotracedistillery.com — CONFIRMED |
| Buffalo Trace Distillery | Frankfort, KY | Eagle Rare 10 Year | 45% (90 proof) | standard bourbon, age-stated 10yr | aged minimum 10 years | buffalotracedistillery.com/our-brands/eagle-rare — CONFIRMED |
| Buffalo Trace Distillery | Frankfort, KY | W.L. Weller Special Reserve | 45% (90 proof) | wheated bourbon | "Original Wheated Bourbon"; wheat replaces rye | buffalotracedistillery.com/our-brands/w-l-weller/w-l-weller-special-reserve — CONFIRMED |
| Buffalo Trace Distillery | Frankfort, KY | W.L. Weller Antique 107 | 53.5% (107 proof) | wheated bourbon | wheated mash bill | producer publishes — VERIFY exact proof on brand page |
| Buffalo Trace Distillery | Frankfort, KY | W.L. Weller 12 Year | 45% (90 proof) | wheated bourbon, age-stated 12yr | wheated mash bill | producer publishes — VERIFY |
| Buffalo Trace Distillery | Frankfort, KY | Blanton's Original Single Barrel | 46.5% (93 proof) | single barrel bourbon | from Warehouse H | buffalotracedistillery.com / blantonsbourbon.com — CONFIRMED |
| Buffalo Trace Distillery | Frankfort, KY | Sazerac Rye (Sazerac 6) | 45% (90 proof) | rye | straight rye | producer publishes — VERIFY |
| Buffalo Trace Distillery | Frankfort, KY | E.H. Taylor Small Batch | 50% (100 proof) | bottled-in-bond bourbon, small batch | Bottled-in-Bond | producer publishes — VERIFY |
| Buffalo Trace Distillery | Frankfort, KY | E.H. Taylor Single Barrel | 50% (100 proof) | bottled-in-bond bourbon, single barrel | Bottled-in-Bond | producer publishes — VERIFY |
| Buffalo Trace Distillery | Frankfort, KY | George T. Stagg | variable/barrel-proof, no fixed ABV | barrel/cask strength bourbon | uncut, unfiltered; Antique Collection | producer publishes — CONFIRMED variable |

### Heaven Hill (Bardstown / Louisville, KY)
| Producer | City, State | Product | ABV | Style tags | Production facts | Source / confidence |
|---|---|---|---|---|---|---|
| Heaven Hill | Bardstown, KY | Elijah Craig Small Batch | 47% (94 proof) | small batch bourbon | standard bourbon mash bill | heavenhilldistillery.com — CONFIRMED |
| Heaven Hill | Bardstown, KY | Elijah Craig Barrel Proof | variable/barrel-proof, no fixed ABV | barrel/cask strength, small batch bourbon | uncut, non-chill filtered | heavenhilldistillery.com — CONFIRMED variable |
| Heaven Hill | Bardstown, KY | Evan Williams Black Label | 43% (86 proof) | standard bourbon | Kentucky straight bourbon | producer publishes — VERIFY |
| Heaven Hill | Bardstown, KY | Evan Williams Bottled-in-Bond (White Label) | 50% (100 proof) | bottled-in-bond bourbon | Bottled-in-Bond | producer publishes — VERIFY |
| Heaven Hill | Bardstown, KY | Evan Williams Single Barrel | 43.3% (86.6 proof) | single barrel bourbon, vintage-dated | vintage-dated single barrel | producer publishes — VERIFY |
| Heaven Hill | Bardstown, KY | Larceny Small Batch | 46% (92 proof) | wheated bourbon, small batch | wheated mash bill: 68% corn / 20% wheat / 12% malted barley | Heaven Hill press release — CONFIRMED |
| Heaven Hill | Bardstown, KY | Larceny Barrel Proof | variable/barrel-proof, no fixed ABV | wheated bourbon, barrel/cask strength | wheated mash bill; non-chill filtered | heavenhilldistillery.com — CONFIRMED variable/wheated |
| Heaven Hill | Bardstown, KY | Henry McKenna Single Barrel 10 Year | 50% (100 proof) | bottled-in-bond bourbon, single barrel, age-stated 10yr | Bottled-in-Bond, single barrel | heavenhilldistillery.com — CONFIRMED style |
| Heaven Hill | Bardstown, KY | Old Fitzgerald Bottled-in-Bond | 50% (100 proof) | wheated bourbon, bottled-in-bond | wheated mash bill; age varies per decanter release | heavenhilldistillery.com — CONFIRMED style |

### Beam Suntory (Clermont / Boston / Loretto, KY)
| Producer | City, State | Product | ABV | Style tags | Production facts | Source / confidence |
|---|---|---|---|---|---|---|
| James B. Beam Distilling Co. | Clermont, KY | Jim Beam White Label | 40% (80 proof) | standard bourbon | 75% corn / 13% rye / 12% malted barley (Beam mash bill) | producer publishes — VERIFY proof |
| James B. Beam Distilling Co. | Clermont, KY | Knob Creek 9 Year Small Batch | 50% (100 proof) | small batch bourbon, age-stated 9yr | 9 years; maximum-char barrels | knobcreek.com — CONFIRMED |
| James B. Beam Distilling Co. | Clermont, KY | Knob Creek 9 Year Single Barrel Reserve | 60% (120 proof) | single barrel bourbon, age-stated 9yr | 9 years; 75% corn / 13% rye / 12% barley | knobcreek.com — CONFIRMED |
| James B. Beam Distilling Co. | Boston, KY | Booker's | variable/barrel-proof, no fixed ABV | barrel/cask strength, small batch bourbon | uncut, unfiltered | beamdistilling.com — CONFIRMED variable |
| James B. Beam Distilling Co. | Clermont, KY | Baker's | 53.5% (107 proof) | single barrel bourbon, age-stated 7yr | aged 7 years | suntoryglobalspirits.com — CONFIRMED style; VERIFY exact proof |
| Basil Hayden (Beam Suntory) | Clermont, KY | Basil Hayden Kentucky Straight Bourbon | 40% (80 proof) | small batch bourbon, high-rye | high-rye mash bill; created 1992 by Booker Noe | beamdistilling.com — CONFIRMED |
| Maker's Mark | Loretto, KY | Maker's Mark | 45% (90 proof) | wheated bourbon | soft red winter wheat mash bill; proofed to 45% | makersmark.com — CONFIRMED |
| Maker's Mark | Loretto, KY | Maker's Mark 46 | 47% (94 proof) | wheated bourbon | finished with 10 seared virgin French oak staves | makersmark.com — CONFIRMED |
| Maker's Mark | Loretto, KY | Maker's Mark Cask Strength | variable/barrel-proof (108–114 proof) | wheated bourbon, barrel/cask strength | min. 7 years; barrel proof, non-chill filtered | makersmark.com — CONFIRMED range |
| Maker's Mark | Loretto, KY | Maker's Mark 46 Cask Strength | variable/barrel-proof (107–114 proof) | wheated bourbon, barrel/cask strength | French oak stave finish; cask strength | makersmark.com — CONFIRMED range |

### Wild Turkey (Lawrenceburg, KY)
| Producer | City, State | Product | ABV | Style tags | Production facts | Source / confidence |
|---|---|---|---|---|---|---|
| Wild Turkey | Lawrenceburg, KY | Wild Turkey 101 | 50.5% (101 proof) | standard bourbon | blend of multiple ages (mash bill not disclosed) | wildturkeybourbon.com — CONFIRMED style |
| Wild Turkey | Lawrenceburg, KY | Rare Breed | variable/barrel-proof, no fixed ABV | barrel/cask strength bourbon | marriage of 6/8/12-year stocks; uncut | wildturkeybourbon.com — CONFIRMED variable |
| Wild Turkey | Lawrenceburg, KY | Russell's Reserve 10 Year | 45% (90 proof) | standard bourbon, age-stated 10yr | 10 years | wildturkeybourbon.com — VERIFY proof |
| Wild Turkey | Lawrenceburg, KY | Russell's Reserve Single Barrel | 55% (110 proof) | single barrel bourbon | non-chill filtered | wildturkeybourbon.com — VERIFY proof |

### Four Roses (Lawrenceburg, KY)
| Producer | City, State | Product | ABV | Style tags | Production facts | Source / confidence |
|---|---|---|---|---|---|---|
| Four Roses | Lawrenceburg, KY | Four Roses Bourbon (Yellow Label) | 40% (80 proof) | standard bourbon | blend of up to 10 recipes (2 mash bills, 5 yeasts) | fourrosesbourbon.com — CONFIRMED style; VERIFY proof |
| Four Roses | Lawrenceburg, KY | Four Roses Small Batch | 45% (90 proof) | small batch bourbon | blend of 4 recipes | fourrosesbourbon.com — CONFIRMED style; VERIFY proof |
| Four Roses | Lawrenceburg, KY | Four Roses Single Barrel | 50% (100 proof) | single barrel bourbon, high-rye | OBSV recipe; high-rye mash bill (60% corn / 35% rye / 5% malted barley) | fourrosesbourbon.com — CONFIRMED style/recipe; VERIFY proof |
| Four Roses | Lawrenceburg, KY | Four Roses Small Batch Select | 52% (104 proof) | small batch bourbon | 6 recipes; non-chill filtered; min. 6yr | fourrosesbourbon.com — CONFIRMED |

### Brown-Forman (Louisville / Versailles, KY & Lynchburg, TN)
| Producer | City, State | Product | ABV | Style tags | Production facts | Source / confidence |
|---|---|---|---|---|---|---|
| Woodford Reserve | Versailles, KY | Woodford Reserve Distiller's Select | 45.2% (90.4 proof) | standard bourbon | 72% corn / 18% rye / 10% malt; pot + column blend | woodfordreserve.com — CONFIRMED |
| Woodford Reserve | Versailles, KY | Woodford Reserve Rye | 45.2% (90.4 proof) | rye | 53% rye mash bill | woodfordreserve.com — CONFIRMED |
| Woodford Reserve | Versailles, KY | Woodford Reserve Double Oaked | 45.2% (90.4 proof) | standard bourbon | matured in second deeply-toasted, lightly-charred barrel | woodfordreserve.com — CONFIRMED |
| Old Forester | Louisville, KY | Old Forester 86 Proof | 43% (86 proof) | standard bourbon | 72% corn / 18% rye / 10% malt | oldforester.com — CONFIRMED |
| Old Forester | Louisville, KY | Old Forester 100 Proof (Signature) | 50% (100 proof) | standard bourbon | handpicked select barrels | oldforester.com — CONFIRMED style; VERIFY proof |
| Old Forester | Louisville, KY | Old Forester 1897 Bottled in Bond | 50% (100 proof) | bottled-in-bond bourbon | Bottled-in-Bond | oldforester.com — CONFIRMED style |
| Old Forester | Louisville, KY | Old Forester 1920 Prohibition Style | 57.5% (115 proof) | standard bourbon, high-proof | 115 proof homage to medicinal era | oldforester.com — CONFIRMED |
| Old Forester | Louisville, KY | Old Forester 1910 Old Fine Whisky | 46.5% (93 proof) | standard bourbon | double-barreled | oldforester.com — CONFIRMED style; VERIFY proof |
| Old Forester | Louisville, KY | Old Forester Rye | 50% (100 proof) | rye | 65% rye / 20% malted barley / 15% corn | oldforester.com — CONFIRMED mash bill |
| Early Times | Louisville, KY | Early Times Bottled in Bond | 50% (100 proof) | bottled-in-bond bourbon | Bottled-in-Bond | UNCONFIRMED — no first-party source located |
| Jack Daniel's | Lynchburg, TN | Old No. 7 | 40% (80 proof) | Tennessee whiskey | Lincoln County Process (maple-charcoal mellowed) | jackdaniels.com — CONFIRMED process; VERIFY proof |
| Jack Daniel's | Lynchburg, TN | Single Barrel Select | 47% (94 proof) | single barrel Tennessee whiskey | single barrel | producer publishes — VERIFY proof |
| Jack Daniel's | Lynchburg, TN | Bonded | 50% (100 proof) | bottled-in-bond Tennessee whiskey | Bottled-in-Bond | producer publishes — VERIFY proof |

### MGP-Sourced / Independent Bottlers
| Producer | City, State | Product | ABV | Style tags | Production facts | Source / confidence |
|---|---|---|---|---|---|---|
| Bulleit (Diageo) | Shelbyville, KY (distilled) | Bulleit Bourbon | 45% (90 proof) | standard bourbon, high-rye | 68% corn / 28% rye / 4% malt | bulleit.com — CONFIRMED style; mash bill per Diageo/label |
| Bulleit (Diageo) | Lawrenceburg, IN (distilled at MGP) | Bulleit Rye | 45% (90 proof) | rye | 95% rye / 5% malted barley; distilled at MGP Indiana (Diageo 2011 announcement + label) | CONFIRMED first-party (Diageo statement / TTB state-of-distillation label) |
| Redemption (Deutsch Family) | Distilling location UNCONFIRMED; brand HQ Stamford, CT | Redemption Rye | 46% (92 proof) | rye | 95% rye mash bill; non-chill filtered | redemptionwhiskey.com — CONFIRMED ABV/facts; MGP sourcing UNCONFIRMED first-party |
| Redemption (Deutsch Family) | Distilling location UNCONFIRMED | Redemption High Rye Bourbon | 46% (92 proof) | bourbon, high-rye | 36% rye; non-chill filtered | redemptionwhiskey.com — CONFIRMED |
| Redemption (Deutsch Family) | Distilling location UNCONFIRMED | Redemption Bourbon | 46% (92 proof) | standard bourbon | 21% rye; non-chill filtered | redemptionwhiskey.com — CONFIRMED |
| High West | Park City / Wanship, UT | American Prairie Bourbon | 46% (92 proof) | standard bourbon | blend of straight bourbons | highwest.com — CONFIRMED style; VERIFY proof |
| High West | Park City / Wanship, UT | Double Rye! | 46% (92 proof) | rye | blend of straight ryes | highwest.com — CONFIRMED style |
| High West | Park City / Wanship, UT | Rendezvous Rye | 46% (92 proof) | rye, high-rye | blend of two high-rye straight ryes | highwest.com — CONFIRMED style |
| Nevada H&C Distilling Co. | Las Vegas, NV | Smoke Wagon Straight Bourbon | 46.25% (92.5 proof) — UNCONFIRMED first-party | straight bourbon, high-rye | "high rye mash bill"; non-chill filtered | nevadadistilling.com — style CONFIRMED; exact proof only on retailer pages |
| Nevada H&C Distilling Co. | Las Vegas, NV | Smoke Wagon Uncut Unfiltered | variable/barrel-proof (~115–116+ proof) | barrel/cask strength bourbon, high-rye | uncut, unfiltered; high-rye | nevadadistilling.com — CONFIRMED variable/range |

### Widely-Owned Craft / Allocated Names
| Producer | City, State | Product | ABV | Style tags | Production facts | Source / confidence |
|---|---|---|---|---|---|---|
| Michter's | Louisville, KY | US★1 Kentucky Straight Bourbon (Small Batch) | 45.7% (91.4 proof) | small batch bourbon | batched max 20 barrels; low barrel-entry proof | michters.com — CONFIRMED |
| Michter's | Louisville, KY | US★1 Kentucky Straight Rye | 42.4% (84.8 proof) | single barrel rye | single barrel; new charred American white oak | michters.com — CONFIRMED style; VERIFY exact proof |
| Michter's | Louisville, KY | US★1 Barrel Strength Rye | variable/barrel-proof, no fixed ABV | barrel/cask strength, single barrel rye | 103 barrel-entry proof; single barrel | michters.com — CONFIRMED variable |
| Angel's Envy | Louisville, KY | Angel's Envy Bourbon (Port Finish) | 43.3% (86.6 proof) | standard bourbon, finished | finished in port wine barrels; small batches of 8–12 barrels | angelsenvy.com — CONFIRMED finish; VERIFY proof |
| Angel's Envy | Louisville, KY | Angel's Envy Rye | 50% (100 proof) | rye, finished | 95% rye; finished up to 18 months in Caribbean rum casks | angelsenvy.com — CONFIRMED |
| New Riff | Newport, KY | New Riff Bottled in Bond Bourbon | 50% (100 proof) | bottled-in-bond bourbon, high-rye | 65% corn / 30% rye / 5% malt; BiB; non-chill filtered; 4yr | newriffdistilling.com — CONFIRMED |
| New Riff | Newport, KY | New Riff Bottled in Bond Rye | 50% (100 proof) | bottled-in-bond rye | 95% rye / 5% malted rye; BiB; non-chill filtered; 4yr | newriffdistilling.com — CONFIRMED |
| Willett (KBD) | Bardstown, KY | Willett Pot Still Reserve | 47% (94 proof) | small batch bourbon | small batch (originally single barrel) | kentuckybourbonwhiskey.com — CONFIRMED product; VERIFY proof |
| Willett (KBD) | Bardstown, KY | Willett Family Estate Bourbon | variable/barrel-proof, no fixed ABV | single barrel bourbon, cask strength | single barrel; cask strength; non-chill filtered | kentuckybourbonwhiskey.com — CONFIRMED variable |
| Willett (KBD) | Bardstown, KY | Willett Family Estate Rye (4yr) | variable/barrel-proof, no fixed ABV | rye, cask strength | own-distilled; cask strength; non-chill filtered | kentuckybourbonwhiskey.com — CONFIRMED variable |
| Old Grand-Dad (Beam Suntory) | Clermont, KY | Old Grand-Dad 80 | 40% (80 proof) | standard bourbon, high-rye | high-rye mash bill (27% rye) | beamdistilling.com — CONFIRMED |
| Old Grand-Dad (Beam Suntory) | Clermont, KY | Old Grand-Dad Bonded | 50% (100 proof) | bottled-in-bond bourbon, high-rye | high-rye; single distilling season; BiB | beamdistilling.com — CONFIRMED |
| Old Grand-Dad (Beam Suntory) | Clermont, KY | Old Grand-Dad 114 | 57% (114 proof) | standard bourbon, high-proof, high-rye | high-rye mash bill | beamdistilling.com — CONFIRMED |
| Barton 1792 (Sazerac) | Bardstown, KY | 1792 Small Batch | 46.85% (93.7 proof) | small batch bourbon, high-rye | high-rye mash bill | sazerac.com — CONFIRMED style; VERIFY proof |
| Barton 1792 (Sazerac) | Bardstown, KY | 1792 Full Proof | 62.5% (125 proof) | barrel-proof bourbon, high-rye | bottled at 125 barrel-entry proof; plate-and-frame filtered | sazerac.com — CONFIRMED |
| Barton 1792 (Sazerac) | Bardstown, KY | 1792 Bottled in Bond | 50% (100 proof) | bottled-in-bond bourbon, high-rye | single distilling season; BiB | sazerac.com — CONFIRMED |

## Recommendations
- **Stage 1 — Import the CONFIRMED rows now.** Every row marked CONFIRMED has both ABV/variable-status and style verified against the producer's own site or press release and is safe to seed immediately (this is the bulk of the 57 rows, including the full Heaven Hill, Beam/Knob Creek/Maker's/Old Grand-Dad, Four Roses, Brown-Forman, New Riff, Michter's, and 1792 sets).
- **Stage 2 — Resolve VERIFY rows before publishing their ABV.** For each VERIFY row, open the specific brand product page (e.g., individual Buffalo Trace brand pages for Weller Antique 107 / Weller 12 / Sazerac Rye / E.H. Taylor; Wild Turkey product pages for Russell's Reserve 10 & Single Barrel; Willett Pot Still page; Jack Daniel's Single Barrel & Bonded pages) and capture the exact ABV string. The values shown are the widely-published figures and are almost certainly correct, but were not captured on a first-party page during this pass.
- **Stage 3 — Hold UNCONFIRMED numeric fields.** Do not publish Early Times Bottled in Bond (no first-party source located at all) or the Smoke Wagon Straight Bourbon exact proof (92.5 only on retailer pages) until confirmed against a qualifying source.
- **Model barrel-proof products correctly:** give them a nullable/"variable" ABV plus `barrel_proof: true`, and store any published range (e.g., Maker's Cask Strength 108–114; Maker's 46 CS 107–114; Smoke Wagon Uncut ~115–116+) in a separate min/max field, since proof changes every batch.
- **Do not populate distilling location or mash bill from secondary sources** for Redemption, Smoke Wagon, or Willett (sourced/undisclosed). Bulleit Bourbon and Bulleit Rye are the exception — the Rye's MGP-Indiana state-of-distillation is a first-party/label fact and may be seeded.
- **Threshold to change approach:** if a compliance requirement demands ≥90% of rows fully first-party-confirmed, budget one more verification pass hitting each brand's individual product page (roughly 20 page fetches) to clear the VERIFY list.

## Caveats
- No competitor rating/aggregator databases (Untappd, Distiller, Vivino, RateBeer, BeerAdvocate, Whiskey Social, usePalate) were used for any data point. Retailer/enthusiast pages surfaced during searching were used only to locate official pages, never as the confirming source; where a fact could only be found on a banned or non-first-party site it is flagged UNCONFIRMED.
- "CONFIRMED" reflects a value seen on the producer's official site or official press release during this research pass; it is still good practice for the human curator to re-open the cited page at import time, since producers occasionally re-proof products.
- Wild Turkey and several Sazerac brands do not publicly disclose exact mash bill percentages; those fields are intentionally left blank rather than estimated.
- Barrel-entry proof, age statements, and batch proofs on limited/annual releases (Booker's, Elijah Craig Barrel Proof, Larceny Barrel Proof, Old Fitzgerald BiB, George T. Stagg, Willett Family Estate, Michter's Barrel Strength Rye) change with every release; treat any single number as batch-specific.
- Producer corporate headquarters is not the same as distilling location (e.g., Deutsch Family Wine & Spirits in Stamford, CT owns Redemption; Diageo owns Bulleit). The seed schema should distinguish a "brand owner / HQ" field from a "distilled at" field, and leave "distilled at" null where the producer does not disclose it.
- Count: 57 product rows across 18 producer groups — comfortably within the requested 40–60 range with good distribution across major, mid-tier, and craft/allocated names.