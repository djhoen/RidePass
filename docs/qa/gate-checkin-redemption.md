# QA Test Plan: Gate Check-in / Redemption

> Scope: the staff-facing gate flow: scan/redeem a ticket or add-on QR token (single and scan-once-redeem-many), the per-tenant require-ID-at-check-in policy, the redemption audit trail (who/when), the admin Event Riders check-in toggle, and the Loam Pass QR gate check-in. Deep Loam Pass credit linking and redeem-on-purchase are covered separately in `loampass-credit-integration.md`. Last updated: 2026-06-20.

## Surface map
- **Redemption (SalesRedeem):** `RedemptionController` -> `GET /api/Redemption/Preview/{token}` (single ticket preview), `POST /api/Redemption/Redeem/{token}` (single redeem), `GET /api/Redemption/Order/{token}` (scan-once: every ticket + add-on the same purchaser holds for the same event), `POST /api/Redemption/Order/Redeem` (bulk redeem with the photo-ID gate).
- **Admin check-in (SalesRedeem):** `ReportsController` -> `GET /api/Reports/Admin/CheckInLookup` (token lookup with waiver/membership gating + today/future registrations), `PUT /api/Reports/Admin/EventRiders/{purchaseId}/CheckIn` (toggle check-in for an event ticket or season pass).
- **Loam Pass gate (SalesRedeem):** `RiderLoampassController.GateCheckIn` -> `POST /api/RiderLoampass/GateCheckIn` (scan a linked rider's Loam Pass QR to redeem their existing race-entry reservation; never spends a credit).
- **Scope resolver:** `RedemptionController.ResolveAnchor` (token -> event + purchaser), `IEventTicketPurchaseRepository.ListByEventForPurchaser`, `IEventExtraRepository.ListByEventForPurchaser`, `MarkRedeemed` / `UpdateStatus` / `UndoRedeemed`.
- **Policy / audit:** `tenant.require_id_at_checkin` (`Script0120`), redemption audit columns `redeemed_at_utc` + `redeemed_by_user_id` and `sold_by_user_id` + season pass `checked_in_by_user_id` (`Script0074`), Loam Pass redemption ledger (`Script0112`).

## Concepts under test
- **Redemption token** is a GUID minted per purchase row (`event_ticket_purchase.redemption_token`, `event_extra_purchase.redemption_token`). The rider presents it as a QR at the gate.
- **Tenant scope.** Every token lookup is filtered by `tenant_id`: tickets via `GetByRedemptionToken(token, tenantId)`; add-ons via `GetPurchaseByRedemptionToken` then an explicit `ex.TenantId == tenantId` check. A token from another tenant resolves to nothing.
- **Date window.** A ticket is redeemable only when "today" in the **tenant time zone** falls within the event's start/end dates. Outside that window the preview/order item reports `IsRedeemableToday=false` with a reason ("Event is on ..." / "Event ended ...").
- **Scan-once-redeem-many.** `Order/{token}` resolves the scanned token to one event + one purchaser, then lists every ticket and add-on that same purchaser holds for that same event (across multiple orders). Bulk redeem authorizes only ids inside that event+purchaser set, so a leaked purchase id cannot redeem outside the scanned rider's event. A no-event add-on (counter merch) is a "solo" anchor: only that one row is in scope.
- **Photo-ID gate.** When `tenant.require_id_at_checkin` is true, `Order/Redeem` requires `IdVerified=true` (staff attests the rider's ID matches the purchaser name) before anything redeems. Default is false (one-tap check-in until opt-in).
- **Audit.** A redeem performed by an authenticated staff member calls `MarkRedeemed(id, tenant, staffId, nowUtc)`, stamping `redeemed_at_utc` + `redeemed_by_user_id`. Without a resolvable staff id it falls back to a plain `UpdateStatus('redeemed')` (status only, no who/when).
- **Loam Pass gate check-in** is identity-based, not token-based: scan the rider's Loam Pass QR, resolve the linked RidePass user (tenant-scoped link), and redeem their existing **paid `race_entry`** ticket for the chosen event. The credit was already spent at booking, so this never spends another.

## Preconditions / test data
- A tenant with **SalesRedeem** gate staff and a known time zone; a second tenant for isolation.
- An ongoing event (today inside its window), a future event, and a past event.
- For one purchaser: a paid race entry + a paid gate fee + a paid add-on on the ongoing event, placed across two separate orders (to exercise scan-once-redeem-many). One already-`redeemed` ticket. One `refunded`/non-paid ticket.
- A standalone counter add-on with no event (solo anchor).
- Tenant copies with `require_id_at_checkin` = false (default) and = true.
- For Loam Pass: a tenant with `loampass_mx_destination_id` set, a rider whose Loam Pass is linked (email + code), with a paid race-entry reservation on the event; a rider whose pass is not linked.

---

## Staff (single token)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| GC1 [NN] | Preview a valid ticket | `GET /api/Redemption/Preview/{token}` for a paid, in-window ticket | 200; returns purchaser, event/tier, amount, `Status=paid`, `IsRedeemableToday=true`, race number, registration-complete flag. |
| GC2 [NN] | Redeem a valid ticket | `POST /api/Redemption/Redeem/{token}` as SalesRedeem staff | 200; status flips to `redeemed`; `redeemed_at_utc` + `redeemed_by_user_id` stamped with the staff id (verify audit columns). |
| GC3 [NN] | Already-redeemed | Redeem the same token again | 400 "Already redeemed." Audit columns unchanged from the first redeem. |
| GC4 [NN] | Non-paid status | Redeem a `refunded`/pending ticket | 400 "Cannot redeem a purchase with status '<status>'." |
| GC5 [NN] | Too early (future event) | Redeem a paid ticket for the future event | 400 with "too early to redeem" reason; status stays `paid`. |
| GC6 [NN] | Expired (past event) | Redeem a paid ticket for the past event | 400 with "ticket expired" reason; status stays `paid`. |
| GC7 [NN] | Time-zone boundary | With the event ending "today" in tenant tz but already past in UTC, attempt redeem near midnight | Redeemability is computed in the **tenant** time zone, not UTC. Confirm the in-window day still redeems. |
| GC8 [NN] | Wrong tenant token | From tenant B, preview/redeem a token minted in tenant A | 404 "No purchase found for this token in your tenant." |
| GC9 [NN] | Unknown / malformed token | Preview a random GUID | 404. (Route enforces `:guid`, so non-GUID is a 404 route miss.) |

---

## Staff (scan-once-redeem-many + ID gate)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| GC10 [NN] | Order lookup aggregates | `GET /api/Redemption/Order/{token}` scanning any one token the purchaser owns | Lists every ticket + add-on that purchaser holds for that event across all their orders; each item carries its own `IsRedeemableToday`/reason and `RegistrationComplete`. `RequireIdAtCheckin` flag echoed. |
| GC11 [NN] | Bulk redeem (gate off) | `POST /api/Redemption/Order/Redeem` with the order's items, tenant `require_id_at_checkin=false` | 200; `RedeemedCount` matches paid in-window items; each stamps staff audit. |
| GC12 [NN] | Bulk redeem skips bad items | Include an already-redeemed and a non-paid item in the bulk request | Those are skipped and surfaced in `Errors` ("already redeemed - skipped" / "status is ... - can't redeem"); the rest still redeem. Response is 200 with partial success. |
| GC13 [NN] | Scope guard on leaked id | Add a `purchaseId` that belongs to a different purchaser/event to the bulk request | That id is rejected ("doesn't belong to this rider's order - skipped"); it never redeems. Confirms event+purchaser authorization scope. |
| GC14 [NN] | ID gate blocks unverified | Tenant `require_id_at_checkin=true`, bulk redeem with `IdVerified=false` | 400 requiring photo-ID confirmation; nothing redeems. |
| GC15 [NN] | ID gate passes when attested | Same tenant, `IdVerified=true` | Items redeem normally. |
| GC16 [NN] | Solo add-on (no event) | Scan a counter merch add-on token (no event) via `Order/{token}` then bulk redeem | Only that single row is in scope (solo anchor); no event aggregation. |
| GC17 [NN] | De-dup in one request | Send the same (kind, purchaseId) twice in `Items` | Redeemed once (`DistinctBy`), not double-counted. |
| GC18 [R] | Redeemer name stamped | Order lookup after a redeem | Already-redeemed items show `RedeemedByName` resolved from the staff user. |

---

## Staff (admin Event Riders + check-in lookup)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| GC19 [NN] | Toggle ticket check-in | `PUT /api/Reports/Admin/EventRiders/{id}/CheckIn` `{source:"event_ticket", checkedIn:true}` | `MarkRedeemed` with staff audit; toggling `checkedIn:false` calls `UndoRedeemed` (clears the redeem). |
| GC20 [NN] | Season pass check-in | Same endpoint `{source:"season_pass", checkedIn:true}` | Reservation status -> `checked_in`, `checked_in_by_user_id` stamped; undo returns it to `reserved` and clears the staffer. |
| GC21 [NN] | Check-in lookup gating | `GET /api/Reports/Admin/CheckInLookup?token=...` for a ticket whose event requires a waiver | Returns `RequiresWaiver`/`WaiverSigned` and `RequiresMembership`/`MembershipActive` plus today/future registrations so staff are warned before checking in. |
| GC22 [R] | SalesRedeem required | Call the redeem/check-in endpoints without SalesRedeem | 403. `ReportsView` is read-only and must not be able to redeem. |

---

## Staff (Loam Pass QR gate check-in)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| GC23 [NN] | Check in linked rider | `POST /api/RiderLoampass/GateCheckIn` `{passQr, eventId}` for a linked rider with a paid race entry | 200 `{checkedIn:true, riderName, item}`; the race-entry ticket is `MarkRedeemed` with staff audit. No credit is spent (already spent at booking). |
| GC24 [NN] | QR format tolerance | Submit the QR as a full `{issuer}/QR/{passId}` URL and as a bare pass id | Both parse to the same pass id (`ParsePassId` splits on the last `/`). |
| GC25 [NN] | Pass not linked here | Scan a valid pass not linked to any rider in this tenant | 400 "This Loam Pass isn't linked to a rider here." (Link lookup is tenant-scoped via `GetUserIdByAccount`.) |
| GC26 [NN] | Already checked in | Scan a rider whose race entry is already `redeemed` | 400 "This rider is already checked in for this event." |
| GC27 [NN] | No reservation | Scan a linked rider with no paid race entry for that event | 400 "No reservation found for this rider at this event." |
| GC28 [NN] | Unrecognized pass | Scan a pass id Loam Pass does not recognize | 400 "That Loam Pass wasn't recognized." |
| GC29 [R] | Required args | POST with missing `passQr` or empty `eventId` | 400 "passQr and eventId are required." |

---

## Known risks / watch-items (multi-tenant isolation)

- **The photo-ID gate is only enforced on bulk redeem.** `tenant.require_id_at_checkin` is checked in `Order/Redeem` but **not** in the single `POST /api/Redemption/Redeem/{token}`, in `ReportsController.SetCheckIn`, or in the Loam Pass `GateCheckIn`. A tenant that opts into ID checks can still be bypassed via the single-token redeem, the admin Event Riders toggle, or a Loam Pass scan. Confirm whether the ID gate should cover those paths too.
- **Audit gap on the no-staff fallback.** When the `UserId` claim is missing/unparseable, redeem falls back to `UpdateStatus('redeemed')`, which records no `redeemed_by_user_id`/`redeemed_at_utc`. Confirm every redeem path runs under an authenticated staff token so the audit trail is always populated.
- **Loam Pass gate only redeems `race_entry`.** `GateCheckIn` matches the first paid ticket with `TierKind == "race_entry"` for the event; gate fees, spectator tickets, or add-ons bought with a credit are not checked in by the QR scan. Confirm intent (other items still need a token scan).
- **Purchaser matching uses user id OR email.** `ListByEventForPurchaser` aggregates by purchaser user id and email; a guest order and a later account under the same email could merge (intended for scan-once) or, if emails differ in case/whitespace, fail to aggregate. Verify the email match is case-insensitive and trimmed.
- **Cross-tenant token isolation.** Re-verify GC8: ticket lookups are tenant-scoped in SQL and add-on lookups re-check `TenantId` in code; any new redemption surface must keep both. The `RedemptionController` is `[Authorize(SalesRedeem)]` at the class level, so unauthenticated scans are rejected before reaching the resolver.
- **Bulk redeem returns 200 on partial failure.** `Order/Redeem` collects per-item errors and still returns OK. The gate UI must surface `Errors` and reconcile `RedeemedCount` against the items sent, or staff may believe everyone checked in when some were skipped.
- **Cross-reference:** Loam Pass credit linking, balance, and redeem-on-purchase (including refund reversal of `loampass_redemption`) are validated in `loampass-credit-integration.md`; this plan covers only the staff gate/redemption side.
