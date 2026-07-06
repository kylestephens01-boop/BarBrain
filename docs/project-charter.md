# Project Charter — BarBrain

> **Repo note (July 2026):** the settled charter text lives in the founder's
> original docs pack and was never committed to this repo. This file currently
> carries ONLY the proposed amendments below. Founder: when convenient, paste
> the settled charter above this marker and adjudicate the proposals in place.
> Agents: do NOT treat the absence of settled text as license to write it.

---

## PROPOSED CHANGES — July 2026 (FOUNDER REVIEW PENDING — nothing below is settled)

Drafted during Sprint 1 kickoff per founder direction (decision E). Each item
names the change, the reason, and what it would supersede. Approving is a
charter edit + (where noted) an ADR amendment; rejecting deletes the item.

### P1. Domain reality (factual correction)

**Proposed text:** BarBrain operates on **barbrain.co** (owned; currently the
dev host `dev.barbrain.co` behind Cloudflare Access). **barbrain.app was
unavailable** to acquire. The public-launch TLD — staying on .co vs acquiring
an alternative (.io/.ai/getbarbrain.com) — is a **post-trademark-knockout
decision**, deliberately deferred until the name itself is cleared.

**Why:** any charter references to acquiring barbrain.app describe a plan that
is no longer possible; docs/HUMAN-CHECKLIST.md item 4 was already corrected in
Sprint 0.5. **Supersedes:** the domain-portfolio line of the settled charter.

### P2. Moat reframe — operational moat primary, dataset supporting

**Proposed text:** BarBrain's primary defensibility is the **corridor
operational moat**: venue density in the Cedar Rapids–Iowa City corridor plus
the founder's repeatable venue-onboarding playbook (QR kits, verified-tier
onboarding, personal relationships). This is what a well-funded incumbent
cannot copy quickly at local scale. The **cross-category attribute dataset is
a supporting, compounding asset** — it deepens switching costs and powers the
product, but it is NOT the sole moat and should not be argued as one.

**Why:** the dataset alone is replicable by an incumbent with more data;
corridor density + relationships are not. Sequencing implication (M1–4 data
machinery → M4–8 venue push) stays unchanged; what changes is the *argument*
for why BarBrain survives contact with an incumbent.
**Supersedes (on approval):** the moat framing in the settled charter AND the
first sentence of **ADR-022** ("the cross-category attribute dataset is the
only compounding asset") — ADR-022 would need a founder-signed amendment;
it has deliberately NOT been rewritten yet (see the note under ADR-022).

### P3. Recommendation-engine sequencing — pgvector first, CF deferred

**Proposed text:** Recommendations launch on **pgvector attribute similarity**
(per-drink 8-dim category vectors + 6-dim cross-category bridge, style
inheritance with provenance/confidence). **Collaborative filtering is
deferred** until rating density justifies it; the upgrade path is preserved by
the append-only ratings history and first-party events (no schema rework).

**Why:** content-based similarity works from the first rating (no cold-start
dependency on co-rating density) and is explainable ("because" chips per
ADR-013). CF adds value only after meaningful rating overlap exists.
**Status:** already decided by the founder and recorded as **ADR-025**
(amending ADR-007's sequencing); listed here so the charter and ADRs tell the
same story once approved.

---

*(Settled charter sections belong above the marker; do not add content below
this line except new dated proposals.)*
