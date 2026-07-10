# BarBrain National Beer Catalog — First-Party Source Compilation

## TL;DR
- Compiled **41 nationally-distributed beers across 21 producer groups** (majority American craft/macro, plus Guinness and Modelo as widely-distributed imports), all sourced from first-party channels only — brewery official sites, official press releases, and label/regulatory statements. No competitor rating/aggregator site (Untappd, RateBeer, BeerAdvocate, etc.) was used as a confirming source.
- **28 entries are CONFIRMED** (ABV and/or style verified directly on the producer's official site, press release, or label), **11 are VERIFY** (producer sells the item but the exact numeric wasn't captured from a qualifying first-party page this pass), and **1 full entry is UNCONFIRMED** (Yuengling Traditional Lager) plus a set of inline IBU fields with no first-party source.
- The catalog deliberately spans **macro (Budweiser, Miller Lite, Coors Banquet, Modelo, Guinness), major regional/craft (Sierra Nevada, Bell's, Founders, Stone, New Belgium, Lagunitas, Deschutes, Allagash, Firestone Walker), and allocated/specialty/seasonal releases (Founders KBS, Sierra Nevada Celebration, Deschutes Black Butte XXXV, Bell's Double Two Hearted)** — mirroring the whiskey catalog's tiered approach rather than concentrating on the top 5 brands.

## Key Findings
- **First-party data quality is highest for craft breweries**, which routinely publish ABV, IBU, hop/malt/yeast bills, and process notes on their own product pages (Sierra Nevada, Founders, Odell, Deschutes, Stone were richest). Macro producers publish ABV and style but rarely IBU; several macro ABVs had to be taken from official label/regulatory statements rather than marketing pages.
- **IBU is the single most frequently missing first-party field.** Many commonly-cited IBU numbers exist only on aggregators/retailers and were therefore flagged UNCONFIRMED even when ABV was confirmed.
- A material data correction surfaced and was independently confirmed: **Founders' official KBS product page lists 45 IBU** (page modified 2025-05-09: "ABV 12% | IBUs 45 | Style Barrel-Aged | Hops Nugget, Willamette | Malts Oats, Chocolate, Roasted Barley, Wheat"), **not the 70 IBU repeated across older third-party sources** — trust the first-party figure.
- Several flagship beers carry **ownership or reformulation nuances** a curator must handle before import (New Belgium Fat Tire's 2023 reformulation; Bell's Kirin/Lion ownership; Anchor Brewing's 2023 closure and 2024 revival).

## Details — Beer Catalog Table

Confidence key: **C** = CONFIRMED (value on producer's official site / press release / label); **V** = VERIFY (producer sells item; exact numeric not captured from a qualifying first-party page this pass); **U** = UNCONFIRMED (no qualifying first-party source — do not publish the numeric).

### Anheuser-Busch — St. Louis, MO, USA
| Beer | ABV | IBU | Style (BJCP note) | Producer-stated production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Budweiser | 5.0% | — | American Lager (maps to BJCP 1B; often labeled "American Adjunct Lager") | Brewed with barley malt and up to ~30% rice, plus a blend of hop varieties; beechwood aging; introduced 1876 | V (ABV/style are AB's own copy, not confirmed on budweiser.com this pass; IBU U) | anheuser-busch.com / budweiser.com |

### Molson Coors — Golden, CO & Milwaukee, WI, USA
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Coors Banquet | 5.0% | — | American Lager (BJCP 1B) | Ingredients: water, barley malt, corn syrup (dextrose, used in brewing only), yeast, hop extract; brewed only in Golden, CO with Rocky Mountain water and high-country Moravian barley; malted in-house | C (ABV via label; ingredients via coors.com) | coors.com |
| Miller Lite | 4.2% | — | Brewery calls it "a true American Pilsner" (maps to BJCP 1A American Light Lager) | Ingredients: water, barley malt, yeast, hops, hop extract, corn syrup; pale + crystal barley malts; 96 cal, 3.2g carbs; brewed since 1975 | C (style/ingredients on millerlite.com); V (exact 4.2% ABV not on official page this pass) | millerlite.com / molsoncoors.com |

### Grupo Modelo (imported by Constellation Brands) — Mexico
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Modelo Especial | 4.4% | — | Pilsner-style Mexican lager (maps to BJCP 2A International Pale Lager) | Water, barley malt, non-malted cereals, and hops; 143 cal, 13.6g carbs per 12 oz | C (modelousa.com nutrition table) | modelousa.com |

### D.G. Yuengling & Son — Pottsville, PA, USA
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Traditional Lager | ~4.5% | — | American Amber Lager (does not map cleanly; amber/Vienna-adjacent) | Roasted caramel malt; cluster + cascade hops (per repeated copy of uncertain origin) | **U** — no official yuengling.com product page confirmed; sources conflict (4.5% vs 4.9%). Do not publish ABV. | flag for follow-up |

### Guinness (Diageo) — St. James's Gate, Dublin, Ireland
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Guinness Draught | 4.2% | — | Irish Stout (BJCP 15B) | Roasted barley gives dark ruby color; nitrogenated (nitrogen + CO₂) for creamy head; world's first widget/nitro beer | C (4.2% via official Diageo brand source; style/facts on guinness.com) | guinness.com |
| Guinness Foreign Extra Stout | 7.5% | — | Foreign Extra Stout (BJCP 16D) | Higher-strength export stout; more CO₂ / sharper profile than Draught | C (guinness.com FAQ) | guinness.com |

### Sierra Nevada Brewing Co. — Chico, CA & Mills River, NC, USA
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Pale Ale | 5.6% | 38 | American Pale Ale (BJCP 18B) | Cascade + Magnum hops; caramel/two-row malt; bottle-conditioned | C | sierranevada.com |
| Hazy Little Thing | 6.7% | 35 | Hazy IPA (BJCP 21C) | Hops: Citra, El Dorado, Magnum, Mosaic, Simcoe; Malts: Munich, Oats, Two-row Pale, Wheat; unfiltered, canned straight from tanks | C | sierranevada.com |
| Torpedo Extra IPA | 7.2% | 65 | American IPA (BJCP 21A) | Hops: Citra, Crystal, Magnum; two-row + caramelized malt; dry-hopped via the "Hop Torpedo" device | C | sierranevada.com |
| Celebration Fresh Hop IPA | 6.8% | 65 | American IPA (BJCP 21A), fresh-hop seasonal | Hops: Cascade, Centennial, Chinook (freshly picked whole-cone); seasonal Oct–Dec | C | sierranevada.com |

### Bell's Brewery — Comstock, MI, USA (owned by Lion/Kirin; sale announced Nov 10, 2021)
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Two Hearted Ale | 7.0% | — | American IPA (BJCP 21A) | 100% Centennial hops (Pacific Northwest); house ale yeast; kettle + fermenter hop additions | C (ABV; IBU not on official page) | bellsbeer.com |
| Double Two Hearted | 11.0% | — | Double IPA (BJCP 22A) | ~2.5× the Centennial hops of Two Hearted; house ale yeast | C | bellsbeer.com |
| Oberon Ale | 5.8% | — | American Wheat Ale (BJCP 1D) | Water, barley, wheat, hops, house ale yeast; no spices/fruit; seasonal (late Mar–Sept) | C | bellsbeer.com |
| Eclipse | 5.8% | — | Fruit Wheat Ale (raspberry) | Raspberry wheat ale, house ale yeast, natural raspberry flavor; seasonal | C | bellsbeer.com |
| Amber Ale | 5.8% | — | American Amber Ale (BJCP 19A) | Ingredients: water, malt, hops, house ale yeast; toasted + caramel malt | C | bellsbeer.com |

### Founders Brewing Co. — Grand Rapids, MI, USA
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| All Day IPA | 4.7% | 42 | Session IPA (no exact BJCP; brewery calls it a "session IPA") | Complex malt/grain/hop bill for a balanced citrusy session ale | C | foundersbrewing.com |
| KBS (Kentucky Breakfast Stout) | 12.0% | 45 | Barrel-aged Imperial Stout (BJCP 20C base + 33B wood-aged) | Brewed with chocolate and coffee, aged in bourbon barrels; Hops: Nugget, Willamette; Malts: Oats, Chocolate, Roasted Barley, Wheat; year-round | C (page modified 2025-05-09) | foundersbrewing.com |

### The Boston Beer Company (Samuel Adams) — Boston, MA, USA
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Samuel Adams Boston Lager | 5.0% | 30 | Brewery states "Lager" (does not map cleanly; closest BJCP 7A Vienna Lager) | Hallertau Mittelfrueh + Tettnang Tettnanger noble hops; two-row pale malt blend + Caramel 60; Samuel Adams lager yeast; decoction mash | C | samueladams.com |

### Stone Brewing — Escondido, CA, USA
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Stone IPA | 6.9% | 71 | American (West Coast) IPA (BJCP 21A) | Hops: Magnum, Chinook, Centennial; dry-hopped | C | stonebrewing.com |
| Stone Ruination Double IPA | 8.2% | 100+ | Double IPA (BJCP 22A) | One of the first year-round bottled West Coast double IPAs | C | stonebrewing.com |
| Stone Hazy IPA | 6.7% | 35 | Hazy IPA (BJCP 21C) | Hops: El Dorado, Azacca, Sabro | C | stonebrewing.com |
| Stone Delicious IPA | 7.7% | — | American IPA (BJCP 21A), gluten-reduced | Lemondrop + El Dorado hops; fermented to reduce gluten (FDA "gluten-reduced") | C (ABV; IBU not captured on official product page) | stonebrewing.com |
| Stone ///Fear.Movie.Lions | 8.5% | 60 | Hazy Double IPA (BJCP 22A / 21C blend) | Loral & Mosaic hops; West Coast bitterness with East Coast haze | C | stonebrewing.com |

### Dogfish Head Craft Brewery — Milton, DE, USA
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| 60 Minute IPA | 6.0% | 60 | American IPA (BJCP 21A) | Continually hopped — more than 60 hop additions over a 60-minute boil; Northwest hops | C (production facts on dogfish.com); V (exact 6.0%/60 IBU not captured from official page — site blocked automated access) | dogfish.com |

### New Belgium Brewing — Fort Collins, CO & Asheville, NC, USA
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Fat Tire | 5.2% | — | Reformulated Jan 2023 to a "deep gold" profile (historically American Amber Ale BJCP 19A; no longer maps cleanly) | Belgian yeast heritage; subtle malt, slightly fruity hop profile; certified carbon-neutral; same 5.2% ABV maintained through reformulation | C (style/facts on newbelgium.com; 5.2% ABV corroborated by New Belgium's own reformulation communications) | newbelgium.com |
| Voodoo Ranger IPA | 7.0% | 50 | American IPA (BJCP 21A) | Mosaic & Amarillo hops; golden IPA | C | newbelgium.com |
| Voodoo Ranger Imperial IPA | 9.0% | 70 | Double IPA (BJCP 22A) | Rare blend of hops; pine + citrus | C | newbelgium.com |
| Voodoo Ranger Juice Force | 9.5% | — | Imperial Hazy IPA (BJCP 22A/hazy) | Fruit-forward hazy imperial IPA | C (ABV; IBU not stated) | newbelgium.com |

### Allagash Brewing Company — Portland, ME, USA
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Allagash White | 5.2% | — | Belgian Witbier (BJCP 24A) | Wheat, coriander, and Curaçao orange peel; bottle-conditioned; house yeast | C (style/facts via allagash.com); V (exact 5.2% ABV — official product page not fetched this pass; sources range 5.1–5.5%) | allagash.com |

### Lagunitas Brewing Company — Petaluma, CA, USA
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Lagunitas IPA | 6.2% | 51 | American IPA (BJCP 21A) | "C" hops (Cascade, Centennial, Chinook + Simcoe) over English Crystal, Caramel & Munich malts | C (production facts on lagunitas.com); V (exact ABV/IBU not captured from official page) | lagunitas.com |
| Lagunitas Hazy IPA | 5.5% | — | Hazy IPA (BJCP 21C) | Nelson, Krush, Citra, Sabro hops | C (ABV on lagunitas.com; IBU not stated) | lagunitas.com |
| Lagunitas IPNA | <0.5% | — | Non-alcoholic IPA (no BJCP category) | Same hops/malt/yeast/water as their IPAs, brewed to <0.5% ABV | C | lagunitas.com |

### Deschutes Brewery — Bend, OR, USA
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Black Butte Porter | 5.5% | 30 | American Porter (BJCP 20A) | Malts: Dark Chocolate, Wheat, Crystal 75, 2-row, Carapils; Hops: Cascade, Tettnang | C | deschutesbrewery.com |
| Black Butte XXXV (anniversary) | 11.0% | 40 | Imperial/pastry porter (does not map cleanly) | Porter brewed with cherries, cocoa, and vanilla (Black Forest-cake inspired); anniversary reserve release | C | deschutesbrewery.com |

### Firestone Walker Brewing Company — Paso Robles, CA, USA
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| 805 | 4.7% | — | American Blonde Ale (BJCP 18A) | Light, malt-forward blonde ale; clean finish | C (style/brand on firestonewalker.com); V (exact 4.7% ABV not captured from official product page this pass) | firestonewalker.com |

### Goose Island Beer Co. — Chicago, IL, USA
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Goose IPA | 5.9% | — | Brewery states "IPA" (English-leaning; maps to BJCP 12C/21A) | Hops: Pilgrim, Celeia, Cascade, Centennial; contains wheat | C (ABV/hops on gooseisland.com; IBU not on official page) | gooseisland.com |

### Oskar Blues Brewery — Longmont, CO & Brevard, NC, USA
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Dale's Pale Ale | 6.5% | — | American Pale Ale (BJCP 18B; strong for style) | Hops: Cascade, Centennial, Comet | C (ABV/hops on oskarblues.com; IBU not on official page) | oskarblues.com |

### Odell Brewing Co. — Fort Collins, CO, USA
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Odell IPA | 7.0% | 60 | American IPA (BJCP 21A) | "Hero Hops" blend of nine American hops; Pale + Vienna malts; house yeast; Cache la Poudre River water | C | odellbrewing.com |

### Ballast Point Brewing — San Diego, CA, USA
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Sculpin IPA | 7.0% | — | American IPA (BJCP 21A) | Hopped at five separate stages; apricot/peach/mango/lemon character | C (ABV/facts on ballastpoint.com; IBU not on official page) | ballastpoint.com |

### Anchor Brewing Company — San Francisco, CA, USA
| Beer | ABV | IBU | Style | Production facts | Conf. | Source |
|---|---|---|---|---|---|---|
| Anchor Steam Beer | 4.9% | — | California Common (BJCP 19B) | Blend of pale + caramel malts; lager yeast fermented warm in shallow open-air fermenters; kräusened | V (values from Anchor label/press, not a live official product page — see caveats on closure/revival) | anchorbrewing.com |

## Recommendations
1. **Import the 28 CONFIRMED entries now** as the seed core — they carry first-party ABV and (where present) IBU plus production facts, matching the whiskey catalog's CONFIRMED standard.
2. **Queue a targeted verification pass for the 11 VERIFY entries.** Highest priority: capture the exact ABV directly from `budweiser.com`, `millerlite.com`, `firestonewalker.com`, `allagash.com`, `lagunitas.com`, and `dogfish.com` product pages. Note that several of these (notably dogfish.com) actively block automated fetches — a manual browser fetch or an official archived page will likely be needed.
3. **Hold Yuengling Traditional Lager as UNCONFIRMED** until an official Yuengling page is confirmed; do not publish its ABV given the unresolved 4.5% vs 4.9% conflict.
4. **Do not populate IBU fields flagged UNCONFIRMED** (e.g., Goose IPA ~55, Dale's ~65, Bell's Amber ~32, Sculpin ~70). These appear only on non-first-party sources. Leave IBU null rather than importing an aggregator value.
5. **Status-change thresholds:** promote VERIFY→CONFIRMED only when the exact numeric is seen on the producer's own domain, an official press release, or a label image. Demote CONFIRMED→VERIFY if a producer reformulates (Fat Tire is the cautionary example) or changes packaging specs.

## Caveats for the Data Curator
- **Brand-owner/HQ vs. actual brewing location.** Several beers are brewed in multiple facilities: Sierra Nevada (Chico, CA + Mills River, NC), New Belgium (Fort Collins, CO + Asheville, NC), Oskar Blues (Longmont, CO + Brevard, NC), Boston Beer/Samuel Adams (owned + contract breweries across MA/OH/PA). Store HQ and brewing location as distinct fields where possible.
- **Ownership vs. perceived "craft."** Bell's is owned by **Lion (a Kirin Holdings subsidiary); that sale was announced November 10, 2021** (Lion had earlier acquired New Belgium in 2019). Ballast Point and Anchor have changed hands; Modelo is imported by Constellation Brands in the US. Country/producer fields should reflect the legal producer, not craft perception.
- **Defunct/revived producer — Anchor.** **Anchor Brewing announced it would cease operations and liquidate on July 12, 2023** (it had been acquired by Sapporo for $85 million in August 2017). The brand was **subsequently purchased on May 31, 2024 by Shepherd Futures, the investment vehicle of Chobani founder Hamdi Ulukaya.** Treat Anchor Steam as VERIFY and re-confirm specs against the revived brand's official site before publishing.
- **Reformulations.** **New Belgium officially unveiled a reformulated Fat Tire on January 17, 2023**, moving it off the classic amber profile to a "deep gold" recipe while keeping the same **5.2% ABV** (rollout completed nationwide by mid-February 2023). The historical "American Amber Ale" BJCP mapping no longer cleanly applies — verify the current style before publishing.
- **Batch/seasonal ABV variance.** Guinness Draught is cited officially in a 4.1–4.3% range depending on market (4.2% used here). Anniversary/seasonal releases (Deschutes Black Butte XXX-series, Sierra Nevada Celebration, Bell's Oberon/Eclipse) vary year to year — tag them as seasonal/rotating and date-stamp the spec at import.
- **Style-mapping gaps.** Session IPAs (Founders All Day), gluten-reduced IPAs (Stone Delicious), non-alcoholic (Lagunitas IPNA), pastry/anniversary porters (Black Butte XXXV), Samuel Adams Boston Lager, and adjunct macro lagers do not map cleanly to a single BJCP code — flag these for manual BJCP-code assignment rather than auto-mapping.
- **IBU sparsity is structural.** Roughly half the catalog lacks a first-party IBU (macro brands and many craft flagships simply don't publish it). The schema must permit null IBU; do not backfill from aggregators.
- **Numeric schema (`numeric(4,1)`).** All confirmed ABV values fit one decimal place. Two label statements are non-numeric: Stone Ruination "100+" IBU and Lagunitas IPNA "<0.5%" ABV. Make a manual decision (e.g., store 100.0 / 0.5 with an accompanying text note, or add a separate qualifier field) rather than silently truncating.

### Confidence tally
- **CONFIRMED: 28** entries/primary fields.
- **VERIFY: 11** — Budweiser (ABV/style), Miller Lite (exact ABV), Dogfish Head 60 Minute (exact numerics), Fat Tire (exact ABV, now corroborated but not from a numeric official product page), Allagash White (exact ABV), Lagunitas IPA (exact ABV/IBU), Firestone 805 (exact ABV), Anchor Steam (entire entry), plus inline IBU-only gaps.
- **UNCONFIRMED: 1 full entry** — Yuengling Traditional Lager — plus all inline-flagged IBU values (Goose IPA, Dale's, Bell's Amber, Sculpin, and others) that have no first-party source. Do not publish these numeric fields.