# QA Results: Gate Check-in / Redemption

Traced against current code on 2026-06-20. Verdicts: PASS / FAIL / NEEDS-LIVE / N/A.
File paths are absolute; line numbers are at time of review.

## Staff (single token)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| GC1 | PASS | Preview returns purchaser, event/tier, amount, Status, IsRedeemableToday, RaceNumber, RegistrationComplete (RedemptionController.cs:36-45, 328-374). |
| GC2 | PASS | Redeem flips to redeemed; with a parseable staff id calls MarkRedeemed(id, tenant, staffId, nowUtc) stamping audit columns (RedemptionController.cs:78-85). Shared waiver gate now also runs first (lines 70-76). |
| GC3 | PASS | Status "redeemed" -> 400 "Already redeemed." before any write; audit unchanged (RedemptionController.cs:56-59). |
| GC4 | PASS | Non-paid -> 400 "Cannot redeem a purchase with status '<status>'." (RedemptionController.cs:60-63). |
| GC5 | PASS | Future event -> IsRedeemableToday false, reason "Event is on ... too early to redeem." 400, no write (RedemptionController.cs:65-68, 346-347). |
| GC6 | PASS | Past event -> reason "Event ended ... ticket expired." 400, no write (RedemptionController.cs:65-68, 348). |
| GC7 | PASS | Window computed in tenant tz: todayInTenant vs start/end converted via ResolveTenantTimeZone (RedemptionController.cs:331-342, 379-384). |
| GC8 | PASS | Ticket lookup tenant-scoped GetByRedemptionToken(token, tenantId); add-on re-checks ex.TenantId; null -> 404 "No purchase found for this token in your tenant." (RedemptionController.cs:42, 285-308, 334). |
| GC9 | PASS | Route `{token:guid}` rejects non-GUID (404 route miss); unknown GUID -> LookupAsync null -> 404 (RedemptionController.cs:36-44, 376). |

## Staff (scan-once-redeem-many + ID gate)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| GC10 | PASS | Order aggregates all tickets + add-ons for event+purchaser; per-item IsRedeemableToday/reason/RegistrationComplete; RequireIdAtCheckin echoed (RedemptionController.cs:93-176, 108). |
| GC11 | PASS | RedeemBulk with gate off redeems paid in-scope items, each stamps staff audit (RedemptionController.cs:217-250). Note: bulk does NOT re-validate the event date window server-side (only status + scope + waiver), so RedeemedCount matches in-window only because the client sends in-window items. |
| GC12 | PASS | Already-redeemed -> "A ticket was already redeemed - skipped."; non-paid -> "Ticket status is '<status>' - can't redeem."; collected in Errors, 200 partial (RedemptionController.cs:229-230, 245-246, 262). |
| GC13 | PASS | Leaked id not in allowedTicketIds/allowedExtraIds -> "... doesn't belong to this rider's order - skipped." never redeems (RedemptionController.cs:199-211, 223-225, 239-241). |
| GC14 | PASS | RequireIdAtCheckin && !IdVerified -> 400, nothing redeems (RedemptionController.cs:185-189). |
| GC15 | PASS | IdVerified true -> passes gate, items redeem (RedemptionController.cs:185 negated). |
| GC16 | PASS | Solo add-on (no event) -> only SoloExtra in scope; no aggregation (RedemptionController.cs:149-154, 208-211, 305). |
| GC17 | PASS | `req.Items.DistinctBy(i => (i.Kind, i.PurchaseId))` de-dups (RedemptionController.cs:217). |
| GC18 | PASS | Order resolves redeemer names and stamps RedeemedByName on already-redeemed items (RedemptionController.cs:156-173). |

## Staff (admin Event Riders + check-in lookup)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| GC19 | PASS | checkedIn true -> MarkRedeemed(purchaseId, tenant, staffId, now); false -> UndoRedeemed (ReportsController.cs:180-192). Shared waiver gate runs first when checking in (lines 184-188). |
| GC20 | PASS | season_pass checkedIn -> UpdateReservationStatus "checked_in" with staffId; undo -> "reserved", staffId null (ReportsController.cs:194-209). Waiver gate via BlockReason runs first (lines 198-203). |
| GC21 | PASS | CheckInLookup returns RequiresWaiver/WaiverSigned and RequiresMembership/MembershipActive plus today/future registrations (ReportsController.cs:506-567). |
| GC22 | PASS | SetCheckIn and CheckInLookup both `[Authorize(SalesRedeem)]` (ReportsController.cs:169-170, 505-506); ReportsView cannot redeem. |

## Staff (Loam Pass QR gate check-in)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| GC23 | PASS | GateCheckIn MarkRedeemed the paid race_entry with staff audit; returns {checkedIn, riderName, item}; no credit spent (RiderLoampassController.cs:139-158). Waiver gate runs first (lines 149-155). |
| GC24 | PASS | ParsePassId splits on last '/', so full URL and bare id parse identically (RiderLoampassController.cs:162-167). |
| GC25 | PASS | Tenant-scoped GetUserIdByAccount null -> 400 "This Loam Pass isn't linked to a rider here ..." (RiderLoampassController.cs:134-137). |
| GC26 | PASS | No paid entry but a redeemed one exists -> 400 "This rider is already checked in for this event." (RiderLoampassController.cs:141-146). |
| GC27 | PASS | No paid race_entry and none redeemed -> 400 "No reservation found for this rider at this event." (RiderLoampassController.cs:141-146). |
| GC28 | PASS | GetPassOwnerAsync null -> 400 "That Loam Pass wasn't recognized." (RiderLoampassController.cs:131-132). |
| GC29 | PASS | Missing passQr or empty eventId -> 400 "passQr and eventId are required." (RiderLoampassController.cs:128-129). |

## Notes on known risks (watch-items)

- Photo-ID gate confirmed only on bulk Order/Redeem (RedemptionController.cs:185-189); NOT on single Redeem, ReportsController.SetCheckIn, or Loam Pass GateCheckIn. Matches the documented "still only on bulk redeem" expectation. No numbered case asserts it elsewhere.
- Audit fallback: single Redeem and bulk fall back to UpdateStatus('redeemed') with no staff id when the UserId claim is unparseable (RedemptionController.cs:82, 234, 248). SetCheckIn / GateCheckIn instead 400 on a missing claim (ReportsController.cs:174-175; RiderLoampassController.cs:127), so those always audit.
- Loam Pass gate matches only the first paid TierKind == "race_entry" (RiderLoampassController.cs:140); gate fees / add-ons are not checked in by the QR scan, as documented.
- Purchaser email aggregation is case-insensitive (`lower(p.purchaser_email) = lower(@purchaserEmail)`, EventTicketPurchaseRepository.cs:121) but NOT trimmed. Whitespace differences would fail to aggregate. Minor watch-item, no numbered case.
