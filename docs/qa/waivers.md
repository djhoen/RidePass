# QA Test Plan: Waivers

> Scope: per-tenant waiver catalog, in-place editing vs. new-version-as-new-waiver, per-event rider/spectator waiver selection and required flags, minor (under-18) parent/guardian capture, handwritten signature image capture/validation, and sign-on-behalf at the POS counter. Last updated: 2026-06-20.

## Surface map
- **Admin (CatalogManage):** `WaiverController` -> `GET /api/Waiver/Admin` (list), `POST /api/Waiver` (create), `PUT /api/Waiver/{id}` (in-place update), `PUT /api/Waiver` (legacy "publish new" = creates a fresh waiver), `GET /api/Waiver/{id}/Events` (associated events), `PUT /api/Waiver/{id}/Events/{eventId}` (attach/detach as rider and/or spectator).
- **User (rider, authed):** `GET /api/Waiver` (active fallback), `GET /api/Waiver/{id}`, `GET /api/Waiver/MySignature`, `GET /api/Waiver/{id}/MySignature`, `POST /api/Waiver/Sign` (active), `POST /api/Waiver/{id}/Sign` (explicit waiver).
- **Online checkout enforcement:** `PurchaseController.BuyEventTicket` (card path, immediate), `PurchaseController.CompleteTicketRegistration` (deferred/unified checkout, per-audience), and the Loam Pass credit redeem path (all gate on a signed waiver).
- **POS sign-on-behalf (SalesCounter):** `CounterController` signs the active waiver for the rider when a cart item requires it.
- **Repository:** `Services/Repositories/WaiverRepository.cs` (`GetActive`, `GetById`, `Create`, `Update`, `PublishNewVersion`, `Sign`, `SignSpectator`, `GetSignature`, `GetSignatureBySignerEmailForSelf`). Minor rule: `webapi/Helpers/WaiverPolicy.IsMinor`.
- **Migrations:** `Script0023_WaiverSignatureImage`, `Script0031_MinorWaiver`, `Script0067_MultipleWaivers`, `Script0068_WaiverVersionPerRow`, `Script0069_EventWaiverSplit`, `Script0071_SpectatorWaiverSignatures`, `Script0072_EventWaiverPerAudience`.

## Concepts under test
- **Multi-waiver tenant.** A tenant can hold many waivers (`tenant_waiver`), each with `name` (admin label), `title` (legal heading), `body`, `is_active`, and optional `expires_at`. `GetActive` is no longer unique: it returns the newest active, non-expired row (`ORDER BY created_at DESC LIMIT 1`) as the tenant default fallback.
- **Versioning is per row.** Every waiver starts at `version = 1` and owns its own version sequence (the old per-tenant unique-version constraint was dropped in Script0068). Editing via `PUT /api/Waiver/{id}` is **in-place** (no version bump). The legacy `PUT /api/Waiver` ("publish new version") now just **creates a brand-new waiver** and does not deactivate the old one.
- **Per-event audience split.** An event carries `spectator_waiver_id` and `racer_waiver_id` (both nullable, `ON DELETE SET NULL`); null means fall back to the tenant default active waiver. Independent boolean gates `requires_rider_waiver` and `requires_spectator_waiver` decide whether each audience must sign at purchase.
- **Minor rule.** `IsMinor` is true only when a birthdate is on file and the age is under 18 (UTC). No birthdate on file is treated as an adult (legacy accounts keep working). Minors require `parent_name` + `parent_phone` (phone >= 7 chars); the signature image is the parent's.
- **Signature image.** Stored as a base64 PNG data URL. Validation: must start with `data:image/png;base64,` and be between 800 and 1,400,000 chars. On re-sign, `Sign` keeps the original image (`ON CONFLICT (user_id, waiver_id) DO UPDATE SET signed_at`) and only refreshes the timestamp; the first signature is the legal artifact.
- **Spectator / guest signatures.** `rider_waiver_signature.user_id` is nullable; the uniqueness index `uk_rider_waiver_once_user` only applies when `user_id IS NOT NULL`, so a guest may sign once per spectator against the same waiver. Self-sign lookup (`GetSignatureBySignerEmailForSelf`) only counts rows with `signed_by_parent = false` AND `spectator_first_name IS NULL`.

## Preconditions / test data
- A tenant resolved by subdomain with **CatalogManage** admin, a **SalesCounter** cashier, and a **SalesRedeem** gate user.
- At least three waivers: "General Waiver" (active), "Race-Day Waiver" (active), and one with `expires_at` set in the past (expired).
- One race event with `requires_rider_waiver = true` and `racer_waiver_id` pinned to "Race-Day Waiver"; `requires_spectator_waiver = true` and `spectator_waiver_id` null (so it falls back to the tenant default).
- One event with both required flags false (control).
- Rider accounts: an adult (DOB makes age >= 18), a minor (DOB under 18), and a legacy rider with **no** birthdate. One guest email for spectator flows.
- A second tenant (different subdomain) with its own waiver for isolation checks.

---

## Admin

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| WV1 [NN] | Create a waiver | `POST /api/Waiver` with name/title/body, `isActive=true` | Saves at `version = 1`; appears in `GET /api/Waiver/Admin`. Reopen confirms persistence. |
| WV2 [NN] | Multiple active waivers coexist | Create a second active waiver | Both stay active (no auto-deactivation); list shows both with `isActive=true`. Confirms the single-active constraint is gone. |
| WV3 [NN] | Active fallback = newest | With two active non-expired waivers, call `GET /api/Waiver` | Returns the most recently **created** one (`created_at DESC`), not the most recently edited. |
| WV4 [NN] | In-place edit keeps version | `PUT /api/Waiver/{id}` changing the body | `version` stays at 1; body updates in place. No new row created. |
| WV5 [NN] | Legacy publish-new creates a new waiver | `PUT /api/Waiver` (no id) with new title/body | A **new** waiver row is created named "Waiver"; the prior waiver stays active (not flipped inactive). Document that this is the legacy path and the new active fallback may now point at this fresh row. |
| WV6 [NN] | Expiry hides from fallback | Set `expiresAtUtc` to a past time on the only active waiver | `GET /api/Waiver` returns 404 ("No active waiver"); `GET /api/Waiver/Admin` still lists it (admins see expired rows). |
| WV7 [NN] | Attach waiver to event roles | `PUT /api/Waiver/{id}/Events/{eventId}` with `{asRider:true, asSpectator:false}` | Event's `racer_waiver_id` set, `spectator_waiver_id` untouched. `GET /api/Waiver/{id}/Events` lists the event with `AsRider=true`. |
| WV8 [NN] | Detach fully | Same endpoint with `{asRider:false, asSpectator:false}` | Both role columns cleared for that event; event now falls back to the tenant default for any required audience. |
| WV9 [NN] | Delete a referenced waiver (FK) | Remove a waiver pinned as `racer_waiver_id` (via DB or future delete path) | `ON DELETE SET NULL` clears the event's `racer_waiver_id`; event silently falls back to tenant default. Signatures cascade-delete with their waiver. |
| WV10 [R] | CatalogManage gating | Call `POST /api/Waiver` and `PUT /api/Waiver/{id}/Events/{eventId}` without CatalogManage | 403. Read endpoints (`GET /api/Waiver`, `GET /api/Waiver/{id}`) remain public. |

---

## User (sign)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| WV11 [NN] | Adult signs active waiver | Adult rider `POST /api/Waiver/Sign` with a valid PNG data URL | 200; `HasSignedCurrent=true`, `SignedByParent=false`. `GET /api/Waiver/MySignature` reflects it. |
| WV12 [NN] | Sign explicit waiver id | Rider `POST /api/Waiver/{id}/Sign` for the event's racer waiver | 200; signature stored against that exact `waiver_id`, independent of the tenant default. |
| WV13 [NN] | Empty / non-PNG signature rejected | Sign with missing data URL, a JPEG data URL, or a <=800-char stub | 400 "A handwritten signature is required." No row written. |
| WV14 [NN] | Oversized signature rejected | Sign with a data URL >= 1,400,000 chars | 400 (fails the size cap). No row written. |
| WV15 [NN] | Minor needs parent fields | Minor rider signs with no `parentName`/`parentPhone` (or phone < 7 chars) | 400 "A parent or guardian must sign for riders under 18". `SignedByParent` would be true once provided. |
| WV16 [NN] | Minor signs with parent fields | Minor rider signs with valid parent name + phone (>= 7) + PNG | 200; row has `signed_by_parent=true`, `parent_name`, `parent_phone`. |
| WV17 [NN] | Legacy no-DOB treated as adult | No-birthdate rider signs without parent fields | 200; treated as adult (`RiderIsMinor=false`). Document this as intentional legacy behavior. |
| WV18 [NN] | Re-sign keeps original image | Adult who already signed re-posts with a different PNG | 200; `signed_at` refreshes but the stored `signature_data_url` is unchanged (first signature is the legal artifact). Verify the original image persists. |
| WV19 [NN] | Sign expired waiver blocked | `POST /api/Waiver/{id}/Sign` on the expired waiver | 400 "This waiver has expired and can no longer be signed." Note: only the explicit-id path checks expiry; `POST /api/Waiver/Sign` resolves the active fallback, which already excludes expired rows. |
| WV20 [NN] | MySignature minor + emergency-contact flags | Call `GET /api/Waiver/MySignature` as a minor and as an adult | Returns `RiderIsMinor`, `SignedByParent`, `ParentName/Phone`, and `RiderHasEmergencyContact` derived from the user profile. |

---

## Checkout enforcement (online + Loam Pass credit)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| WV21 [NN] | Card buy requires waiver | Authed rider buys a race entry on an event with `requires_rider_waiver=true` but has not signed | 400 "You must sign the current waiver before completing this purchase." After signing, the buy succeeds. |
| WV22 [NN] | Guest blocked from required-waiver race entry | Guest (no sign-in) attempts a race entry on the required-rider-waiver event (not deferred) | 400 prompting sign-in ("please sign in before purchasing race entries"). |
| WV23 [NN] | Deferred per-audience waiver, rider | Unified checkout (DeferRegistration), then `CompleteTicketRegistration` for the race entry without a signature | 400 "<name> needs a signed waiver for this event." Provide `WaiverSignatureDataUrl` to complete; signature stored against the event's `RacerWaiverId` (or tenant default if null). |
| WV24 [NN] | Deferred per-audience waiver, spectator | Same flow for a spectator gate fee on an event with `requires_spectator_waiver=true`, `spectator_waiver_id` null | Registration step is required; signature stored against the tenant default active waiver (fallback). |
| WV25 [NN] | One signature spans the registration's tickets | Complete a registration covering multiple tickets needing the same waiver | The single captured `WaiverSignatureDataUrl` is applied to each ticket's signature row. |
| WV26 [R] | No-waiver event skips the gate | Buy on the control event (both required flags false) | No signature prompt; purchase completes. |
| WV27 [R] | Loam Pass credit redeem honors waiver | Redeem a Loam Pass credit for a race entry on a `requires_rider_waiver` event without signing | 400 "You must sign the current waiver before redeeming a credit for this entry." (Mirrors the card path.) |

---

## POS sign-on-behalf (Counter)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| WV28 [NN] | Cashier signs for rider | At the counter, add a waiver-requiring item for a rider who has not signed; submit with `SignWaiver=true` + PNG | Active waiver signed on the rider's behalf; `WaiverSignatureId` attached to the qualifying line. |
| WV29 [NN] | Counter refuses without signature | Same cart, `SignWaiver=false` (or missing PNG) | 400 "Rider has not signed the active waiver." / "A handwritten signature is required to sign the waiver." Sale blocked. |
| WV30 [NN] | Counter minor enforcement | Counter sale for a minor rider without parent name/phone | 400 "Riders under 18 need a parent or guardian's name and phone number on the waiver." |
| WV31 [NN] | Counter reuses existing signature | Rider already signed the active waiver; cashier sells another waiver-requiring item | No re-sign required; the existing signature id is reused on the new line. |
| WV32 | Counter lookup shows waiver status | Look up a rider at the counter | Response surfaces `HasSignedCurrentWaiver`, `WaiverSignedAtUtc`, `WaiverSignatureDataUrl`, `IsMinor`, and parent fields. |

---

## Known risks / watch-items (multi-tenant isolation)

- **Card buy and counter use the tenant default, not the per-event waiver.** `BuyEventTicket` and `CounterController` enforce/sign via `GetActive` (tenant default), while the **deferred** `CompleteTicketRegistration` path correctly resolves `RacerWaiverId` / `SpectatorWaiverId`. On an event that pins a specific racer waiver, an immediate card buy or counter sale can capture a signature against the *wrong* (default) waiver. Confirm intended behavior or align the immediate paths to the per-event waiver.
- **`SignSpectator` is implemented but not wired to any controller endpoint.** The repo method and `GetSignatureBySignerEmailForSelf` exist (Script0071 schema is in place) but no API route calls them, so guest/spectator self-and-child signing at purchase is not reachable. Confirm whether the spectator-signing UI is expected to be live yet.
- **Signature lookups trust the waiver id for tenant scope.** `GetSignature(userId, waiverId)` and `GetSignatureBySignerEmailForSelf(email, waiverId)` are not themselves filtered by `tenant_id`; they rely on the caller having resolved `waiverId` through a tenant-scoped `GetById`/`GetActive`. Any new caller that passes an unvalidated `waiverId` could read a signature outside the tenant. Keep the resolve-then-lookup ordering.
- **Cross-tenant waiver access is blocked at the read layer.** Verify `GET /api/Waiver/{id}` for waiver A under tenant B returns 404, and that `PUT /api/Waiver/{id}/Events/{eventId}` rejects when the waiver or event belongs to another tenant (both `GetById` calls are tenant-scoped).
- **`IsMinor` is evaluated at sign time** from the profile birthdate. A rider whose DOB is corrected after signing keeps the old `signed_by_parent` flag. Confirm whether a profile DOB change should invalidate or re-prompt the waiver.
- **Legacy publish-new can quietly change the default.** `PUT /api/Waiver` creates a newer active row that immediately becomes the `GetActive` fallback. Confirm admins understand the legacy button creates a new waiver rather than versioning the existing one.
