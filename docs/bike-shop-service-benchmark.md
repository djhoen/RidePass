# Work orders / service: benchmark vs leading systems

Comparison of the RidePass bike-shop service module against the systems bike shops actually use.
Researched 2026-07-20 from vendor documentation. Our side was verified against the schema and
screens, not assumed. Companion to `bike-shop-benchmark.md` (the product catalog comparison).

Systems: **Lightspeed Retail R-Series** (what most bike shops run), **Ascend RMS** (Trek's POS),
**Shopmonkey** and **RepairDesk** (repair-centric, auto/device rather than bike).

## Verdict

Better shape than the catalog was. We already have several things the category treats as premium,
and we beat Lightspeed R-Series outright on customer communication. The gaps are narrow and specific.

## Where we're ahead

- **Signed repair authorization.** We capture a signature against a versioned `work_order_terms`
  agreement. Ascend's equivalent is a "Call If Over" dollar threshold (a policy prompt, no captured
  consent); Lightspeed emails a quote and approval happens off-system.
- **Ready notification by SMS *and* email, tenant-configurable.** Lightspeed's is email-only, on by
  default, and **cannot be edited** — the weakest of the four.
- **Condition photos staged intake / progress / return.** Ascend appears to have none (a real gap
  for damage disputes); Lightspeed caps at 12 static images.
- **Saved jobs** = Shopmonkey's "canned services" (tier-gated premium there); Ascend's "Quickies"
  are lighter.
- **Special-order parts linked to a PO line with an arrival timestamp** — the workflow Lightspeed
  only recently shipped colour-coding for.
- **Parts consume stock** on a committed job, returned to the shelf on cancel; estimates consume
  nothing. This is the inventory-commitment property the research calls table stakes.

## Gaps

| Gap | What leaders do |
|---|---|
| Customer's bike is free text (`customer_bike_desc`) | Ascend prompts for serial at intake, auto-fills customer + description if the shop sold it, offers prior descriptions, answers "has this bike been in before" |
| No shop labor rate | Lightspeed sets $/hour per location, overridable per line. We type a price on every labor line |
| One bike per ticket | Ascend supports multiple repairs per transaction, with per-bike vs whole-ticket completion notification |
| Single notes field | Lightspeed separates customer-facing receipt notes from timestamped internal notes |
| Fixed 7 statuses | All four allow custom statuses; RepairDesk colour-codes them and binds notifications to status transitions |

The bike-identity gap is foundational: without a serial there's no service history, warranty lookup,
recall matching, or multi-point inspection.

## Premium ideas worth stealing, ranked

1. **Multi-point inspection** (Ascend). Colour-graded components, customer-friendly rendering,
   next-service date, surfaced recalls/service bulletins by model. Simultaneously QC, trust artifact,
   and the best upsell engine in the category. Depends on bike identity.
2. **Estimated vs actual time** per job: per-SKU estimated times, a running timer, and a
   time-variance report (Ascend). What makes flat-rate pricing defensible.
3. **Capacity-based scheduling** (Ascend, alone). Per-tech available minutes drive the promise date,
   with a red/yellow/green tech × date availability grid. Hardest to build, biggest operational lever.
4. **Per-line approve/decline on estimates** with e-signature and an audit trail (Shopmonkey). We
   already capture signatures, so we're closer to this than anyone else in the set.
5. **"Checked By" second-reviewer QC field** (Ascend). Trivial to build, measurably fewer comebacks.
6. **Labor documentation compliance %** (share of labor sales actually attached to a work order) and
   **labor $/clock-hour** (Ascend HQ). Catches revenue leaking off-ticket.
7. **Notifications bound to status transitions** with templates and macros (RepairDesk), rather than
   one hardcoded pickup message.
8. **Space/bay assignment** as a scheduling dimension (Ascend).
9. **Assemblies / bike-build work orders** with an "Assembled By" tech, tracked separately from
   customer repairs (Ascend). New-bike assembly is a large share of a bike shop's wrench hours and
   most generic repair tools have no concept of it.

## Table stakes for a bike shop service department

Custom statuses + a queue filtered by status and tech; tech assignment, promise date and an overdue
view; serial + description on the ticket with prior-service lookup; parts that commit inventory and
labor at a configurable shop rate; automatic ready notification by SMS *and* email; deposit at intake
and clean conversion to a POS sale; multi-bike tickets; printable bike tag; internal vs customer-facing
notes; labor-vs-parts revenue split and labor dollars by mechanic.

## Sourcing caveats

- Lightspeed R-Series and Ascend claims come from vendor pages fetched in full.
- **Shopmonkey's support site, RepairDesk's docs, and Lightspeed X-Series return 403 to automated
  fetching.** Those claims rest on search excerpts or marketing pages — less reliable.
- The Lightspeed work-order export column list came from third-party integrator Workstand, not
  Lightspeed.
- Negative findings (no signature capture in Lightspeed R-Series, no priority field, no photos in
  Ascend) are inferred from absence in the docs, not explicit vendor statements.

### Key sources

- R-Series work orders: <https://retail-support.lightspeedhq.com/hc/en-us/articles/229131428-Creating-and-completing-work-orders>
- R-Series labor charges: <https://retail-support.lightspeedhq.com/hc/en-us/articles/228842707-Configuring-labor-charges>
- R-Series reservations: <https://retail-support.lightspeedhq.com/hc/en-us/articles/37405329604891-What-s-new-in-reservation-and-sales-workflows>
- Ascend work order details: <https://help.ascendrms.com/en_US/service/service-walk-through-the-work-order-details-screen>
- Ascend multi-point inspection: <https://help.ascendrms.com/record-bike-inspections-and-recommended-service-multi-point-inspection>
- Ascend scheduling availability: <https://help.ascendrms.com/set-up-my-service-centers-availability-for-scheduling>
- Ascend service metrics: <https://help.ascendrms.com/ascend-hq-service-team-analysis-metrics>
- Ascend statuses: <https://help.ascendrms.com/manage-work-order-statuses>
- RepairDesk ticket status + notifications: <https://help.repairdesk.co/portal/en/kb/articles/how-to-manage-ticket-status>
- Shopmonkey estimates/workflow: <https://www.shopmonkey.io/product/estimates>, <https://www.shopmonkey.io/product/workflow>
