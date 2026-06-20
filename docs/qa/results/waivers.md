# QA Results: Waivers

Traced against current code on 2026-06-20. Verdicts: PASS / FAIL / NEEDS-LIVE / N/A.
File paths are absolute; line numbers are at time of review.

## Admin

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| WV1 | PASS | Create inserts `version = 1` (WaiverRepository.cs:53-62); appears via ListAdmin -> ListByTenant (WaiverController.cs:63-69, 71-84). |
| WV2 | PASS | Create never deactivates other rows; no single-active constraint (WaiverRepository.cs:47-62). Both stay `is_active = true`. |
| WV3 | PASS | GetActive orders `created_at DESC LIMIT 1` (WaiverRepository.cs:24-33). Newest created wins, not newest edited. |
| WV4 | PASS | Update touches name/title/body/is_active/expires_at only, never `version` (WaiverRepository.cs:64-76). In-place, no new row. |
| WV5 | PASS | PublishNewVersion -> Create with name "Waiver", isActive true, no deactivation (WaiverRepository.cs:85-92; WaiverController.cs:146-152). |
| WV6 | PASS | GetActive excludes expired (`expires_at > now()`, WaiverRepository.cs:29); GET /Waiver returns 404 "No active waiver for this tenant." (WaiverController.cs:42-49). ListByTenant shows all rows incl. expired (WaiverRepository.cs:36-45). |
| WV7 | PASS | SetWaiverRole sets racer_waiver_id when asRider, leaves spectator in ELSE branch when asSpectator false and column != this waiver (EventRepository.cs:151-172); requires_rider_waiver flips true. ListAssociatedEvents returns AsRider (WaiverController.cs:107-125). |
| WV8 | PASS | Detach `{asRider:false, asSpectator:false}` NULLs both columns that point at this waiver (EventRepository.cs:153-162). Event falls back to tenant default. |
| WV9 | PASS | event.racer_waiver_id / spectator_waiver_id are `ON DELETE SET NULL` (Script0069_EventWaiverSplit.sql:9-10); rider_waiver_signature.waiver_id is `ON DELETE CASCADE` (Script0005_DayPassesWaivers.sql:48). |
| WV10 | PASS | POST and PUT /{id}/Events/{eventId} carry `[Authorize(CatalogManage)]` (WaiverController.cs:71-72, 129-131); GET /Waiver and GET /{id} have no auth attribute (WaiverController.cs:34-35, 53-54). |

## User (sign)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| WV11 | PASS | POST /Waiver/Sign -> SignWaiverInternal returns HasSignedCurrent true, SignedByParent false for an adult (WaiverController.cs:247-268, 297-308). |
| WV12 | PASS | POST /Waiver/{id}/Sign signs the explicit waiver id (WaiverController.cs:232-245); repo writes against `waiver.Id` (WaiverRepository.cs:162-181). |
| WV13 | PASS | IsValidPngDataUrl rejects missing, non-`data:image/png;base64,`, and <=800 chars -> 400 "A handwritten signature is required." (WaiverController.cs:274-276, 311-317). |
| WV14 | PASS | Size cap `< 1_400_000` (WaiverController.cs:316); >= 1,400,000 fails -> 400, no row written. |
| WV15 | PASS | Minor with no/short parent fields -> 400 "A parent or guardian must sign for riders under 18 ..." (WaiverController.cs:283-291). |
| WV16 | PASS | Valid parent name + phone (>=7) + PNG persists signed_by_parent/parent_name/parent_phone (WaiverController.cs:283-295). |
| WV17 | PASS | WaiverPolicy.IsMinor returns false when birthdate is null (WaiverPolicy.cs:10-17); no-DOB rider signs as adult. |
| WV18 | PASS | Sign uses `ON CONFLICT (user_id, waiver_id) DO UPDATE SET signed_at` only; stored image unchanged (WaiverRepository.cs:167-174). Note: the 200 response echoes the newly posted dataUrl (WaiverController.cs:303) but the DB row keeps the original. |
| WV19 | PASS | SignWaiverById blocks `ExpiresAt <= now` -> 400 "This waiver has expired and can no longer be signed." (WaiverController.cs:240-243). |
| WV20 | PASS | MySignature returns RiderIsMinor, SignedByParent, ParentName/Phone, RiderHasEmergencyContact from profile (WaiverController.cs:189-201). |

## Checkout enforcement (online + Loam Pass credit)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| WV21 | PASS | Card path (non-defer) blocks unsigned -> 400 "You must sign the current waiver before completing this purchase." (PurchaseController.cs:433-450). After signing, GetSignature non-null and buy proceeds. |
| WV22 | PASS | Guest (no purchaserUserId) on required-rider-waiver, non-deferred -> 400 "... please sign in before purchasing race entries." (PurchaseController.cs:435-440). |
| WV23 | PASS | CompleteTicketRegistration: missing WaiverSignatureDataUrl when a ticket needsWaiver -> 400 "<name> needs a signed waiver for this event." (PurchaseController.cs:939-941); signature stored against RacerWaiverId, else active fallback (PurchaseController.cs:948-965). |
| WV24 | PASS | Spectator gate fee with spectator_waiver_id null: needsWaiver uses RequiresSpectatorWaiver, waiverId null -> GetActive fallback (PurchaseController.cs:931-958). |
| WV25 | PASS | One reg.WaiverSignatureDataUrl applied to every ticket in the loop (PurchaseController.cs:944-965). |
| WV26 | PASS | Control event (both flags false): needsWaiver false, no prompt; purchase completes (PurchaseController.cs:433, 934). |
| WV27 | PASS | Loam Pass credit redeem mirrors card path -> 400 "You must sign the current waiver before redeeming a credit for this entry." (PurchaseController.cs:1075-1080). |

## POS sign-on-behalf (Counter)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| WV28 | PASS | SignWaiver=true + PNG signs active waiver on rider's behalf; WaiverSignatureId attached to waiver lines (CounterController.cs:464-494, 620). |
| WV29 | PASS | SignWaiver=false -> 400 "Rider has not signed the active waiver."; missing PNG -> 400 "A handwritten signature is required to sign the waiver." (CounterController.cs:472-478). |
| WV30 | PASS | Minor without parent name/phone -> 400 "Riders under 18 need a parent or guardian's name and phone number on the waiver." (CounterController.cs:480-489). |
| WV31 | PASS | Existing signature reused: waiverSignatureId = existing.Id, no re-sign (CounterController.cs:467-498). |
| WV32 | PASS | Counter lookup returns HasSignedCurrentWaiver, WaiverSignedAtUtc, WaiverSignatureDataUrl, IsMinor, parent fields (CounterController.cs:97-130). |

## Notes on known risks (watch-items)

- Immediate card buy (PurchaseController.cs:442) and counter sale (CounterController.cs:466) still enforce/sign via GetActive (tenant default), NOT the event's pinned RacerWaiverId. Matches the plan's documented risk; only the deferred path resolves per-event. Behavior is as the plan describes, not a regression.
- New shared check-in gate (Services/Waivers/WaiverCheckInGate.cs) correctly resolves the event's pinned waiver else tenant active (lines 71-72) and matches by user id or guest email (lines 75-78); wired into every check-in path (see gate-checkin results GC2/GC19/GC20/GC23 notes).
