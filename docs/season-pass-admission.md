# Season Pass Admission Modes: Walk-Up and Event Sign-Up

Implementation-ready design. Status: approved design, not yet implemented. 2026-07-24.

## 1. Problem and the two modes

Tenants split into two operating models for season pass admission:

- **Mode A, event sign-up required**: a pass holder must reserve a spot for a specific
  calendar event before the pass admits them. The pass alone does not admit.
- **Mode B, walk-up** (e.g. Highland Bike Park): the rider shows up on any operating day,
  staff scan the pass QR, hand over a wristband, and the rider gets on the lift. There may
  be no calendar event at all; the park is simply open.

Today RidePass supports neither cleanly. The gate redemption path
(`RedeemPassAtGate`, `webapi/Controllers/SeasonPassController.cs` line 1124) requires a
`[Required] Guid EventId` (`webapi/Controllers/API/Data/SeasonPass/SeasonPassGateRedeemRequest.cs`)
and rejects any event not running today, so a track with zero events has no admission path
(the scanner shows a dead-end alert at `vueapp/src/views/Admin/RedeemTickets.vue` lines 336-338).
Meanwhile a Mode A tenant cannot forbid walk-ups: any scheduled same-day event admits any
paid pass whether or not the rider signed up. Wristbands cannot be linked to a pass
admission at all: `event_wristband.ticket_id` is `NOT NULL REFERENCES event_ticket_purchase(id)`
(`RidePass.Migrator/Scripts/Script0189_EventWristbands.sql` line 26) and the UI only offers
"Link band" for `item.kind === 'event_ticket'` (`RedeemTickets.vue` line 152). There is no
tenant setting for the mode; `Tenant.cs` line 114 has only `SeasonPassesEnabled`.

This design adds a tenant-level admission-type setting, a no-event walk-up admission path,
mode enforcement in both directions, and wristband linkage for pass admissions. It extends
the existing `RedeemPassAtGate` flow rather than adding a parallel endpoint or a second
check-in table.

## 2. Verified current state

Every fact below was re-verified against the repo before this document was written.

- `RidePass.Migrator/Scripts/Script0035_SeasonPasses.sql` lines 75-84: `season_pass_reservation`
  with `event_id uuid NOT NULL REFERENCES event(id) ON DELETE CASCADE`, `status` CHECK
  (`reserved`/`checked_in`/`cancelled`), and `UNIQUE (season_pass_purchase_id, event_id)`.
  The table has **no tenant_id column**; tenant scope is reached only through
  `season_pass_purchase.tenant_id` (Script0035 line 45). Script0074 line 28 later added
  `checked_in_by_user_id`.
- `webapi/Controllers/SeasonPassController.cs` line 1124: `RedeemPassAtGate` validates pass
  status, season window, `days_of_week`, registration (photo + product waiver) and the
  event rider waiver, serializes double-scans with an advisory lock keyed
  `season-pass-redeem:{pass.Id}` (line 1186 via `_db.AcquireAdvisoryLock`), and performs the
  atomic burn + upsert via `CreateGateCheckIn` (`Services/Repositories/SeasonPassRepository.cs`
  lines 540-558).
- `RidePass.Migrator/Scripts/Script0189_EventWristbands.sql`: `event_wristband` with
  `tenant_id NOT NULL`, `event_id NOT NULL`, `ticket_id NOT NULL` (line 26), unique indexes
  `uk_event_wristband_code (tenant_id, event_id, lower(code))` (lines 35-36),
  `uk_event_wristband_ticket (ticket_id)` (lines 38-39), and lookup index
  `idx_event_wristband_lookup (tenant_id, lower(code))` (lines 42-43). Also adds
  `tenant.wristbands_enabled` (line 20).
- `Services/Repositories/Data/TenantData/Tenant.cs` line 114: `SeasonPassesEnabled` (from
  Script0064). Line 18: `RequireReservationForPasses` (from Script0005), a purchase-time
  day-pass concern, orthogonal to gate admission mode and **not reused** by this design.
- Highest migration is `Script0234_ConcessionNoPrepItems.sql`; this design owns 0235-0237.
- `EventResponse.EligiblePasses` (`webapi/Controllers/API/Data/Event/EventResponse.cs` line 39)
  is declared but never assigned anywhere in webapi; its backing table `event_pass_eligibility`
  was dropped by `Script0118_RemoveDayPass.sql` line 57. It is dead and must not be relied on.
- `SeasonPassService.reserve()` (`vueapp/src/services/SeasonPassService.ts` lines 231-233)
  has zero call sites in `vueapp/src`; the rider-facing Reserve UI does not exist today.
- `Services/Helpers/DbHelper.cs` registers `DateOnly` and `DateOnly?` Dapper type handlers in
  its static constructor, so `DateOnly` parameters are safe with the pinned Dapper 2.0.123.

A prose note on strings quoted from the repo: several existing strings in
`SeasonPassController.cs` and `RedeemTickets.vue` contain an em dash character. This document
never reproduces that character; wherever such a string is quoted, the dash is re-typed as a
plain hyphen and the implementer should keep whatever separator the existing source line
already uses. This is a documentation substitution only.

## 3. Decisions and trade-offs

The five design workstreams were authored independently; the following reconciliations were
made when assembling this document.

1. **Default mode is WalkUp (2).** Every existing tenant today operates in
   walk-up-to-today's-event mode (there is only one behavior and it matches value 2), so
   defaulting the new column to 2 changes no tenant's observed behavior. Mode A is opt-in.
2. **No-event anchor = nullable `event_id` plus a `check_in_date` column** on
   `season_pass_reservation`, with a partial unique index replacing
   `UNIQUE (season_pass_purchase_id, event_id)` for the no-event case. Rejected alternative:
   a synthetic "operating day" event row, which would pollute the calendar, reports, and
   capacity logic to fake an anchor.
3. **`TodayWalkUpCheckIn` response field dropped.** The backend workstream proposed both a
   `TodayWalkUpCheckIn` object on `LookupPassByToken` and widening `todaysReservations` to
   include the no-event row; the frontend and wristband workstreams both consume only
   `todaysReservations`. The extra field duplicated a query for no consumer, so it is cut.
   `AdmissionTypeId` and `WalkUpEligibleToday` are kept.
4. **Walk-up rows get `eventTitle: "Walk-up admission"` via controller mapping**, not SQL
   COALESCE, in `LookupPassByToken`'s reservation mapping, so `PassReservation.eventTitle`
   stays populated for the scanner's list while the SQL stays a plain LEFT JOIN.
5. **Date typing differs by layer, deliberately.** `SeasonPassReservation.CheckInDate` is
   `DateTime?` to match every sibling date property in
   `Services/Repositories/Data/PaymentData/SeasonPass.cs`. The wristband layer's new
   parameters and `SeasonPassReservationLinkContext.CheckInDate` use `DateOnly?`, which is
   safe because `DbHelper` already registers the handlers. Both bind correctly; each file
   follows its own established convention.
6. **`LinkToReservation` carries the admission's scope** (`Guid? eventId, DateOnly? validOnDate`),
   the wristband workstream's fuller signature, over a shorthand that omitted the event. The
   band inherits its validity scope from the admission it is linked to.
7. **Settings control is a `v-select`**, not a radio group or `v-btn-toggle`: two mutually
   exclusive named modes with explanatory text fit a compact select inside the existing
   Features list row.
8. **The rider-facing Reserve UI is in scope** and fully specified in section 8.4. One
   workstream deferred it; the frontend workstream designed it and the dispute ruling on that
   section assumed it ships. It is the only way a Mode A rider can satisfy the gate.
9. **Ruling edits applied to the frontend design** (all three override earlier drafts):
   (a) the Reserve dialog's event filter is a client-side mirror of the server's own
   `Reserve` validations, because `EligiblePasses` is dead (see section 2); no
   `MySeasonPass.productId` field is added and no backend `eligiblePasses` population is
   resurrected. (b) `admitPass()` keeps the repo's existing fallback string verbatim
   (`RedeemTickets.vue` line 729); the only deltas are the guard loosening and nullable
   eventId. (c) `applySeasonPassAdmissionType`'s fallback names the setting instead of the
   legacy generic `'Save failed.'` used by three untouched legacy call sites in the same file.
10. **`Reserve` and `Reservations/{id}/CheckIn` stay ungated in both modes.** A Mode B park
    can still run capacity-limited race days with sign-ups; the mode only gates the walk-up
    redemption path. Stated per-flow in section 6.6.
11. **Naming**: the new identifiers are `SeasonPassAdmissionTypeId` (int) +
    `SeasonPassAdmissionType` (enum), per the Type/TypeId convention. Existing columns and
    properties named `kind` (e.g. `product.Kind`) are referenced as-is, never renamed.

## 4. Tenant admission-type setting

### 4.1 Migration: `RidePass.Migrator/Scripts/Script0235_SeasonPassAdmissionType.sql`

```sql
-- Season pass admission mode, tenant-configurable:
--   1 = event sign-up required ("Mode A"): a pass holder must reserve a specific
--       event before the pass admits them. Enforced at gate redemption.
--   2 = walk-up ("Mode B", e.g. Highland Bike Park): the rider shows up on any
--       operating day and the pass alone (scanned at the gate) admits them, with
--       no calendar event required.
--
-- Default 2 (walk-up) for every existing tenant because that is the behavior the
-- app already ships today: RedeemPassAtGate currently redeems against whatever
-- event is scheduled for the tenant's local "today", with no separate reservation
-- requirement of its own. Setting the default to 2 means this migration changes
-- no tenant's observed behavior; Mode A is strictly opt-in per tenant from
-- Admin -> Settings -> Features once the enforcement side of this feature ships.
--
-- This column is unrelated to require_reservation_for_passes (Script0005), which
-- gates whether a RIDER must book a specific slot when BUYING a day pass product.
-- season_pass_admission_type_id instead gates what happens at the GATE when an
-- already-purchased season pass is scanned. The two are never read together.

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS season_pass_admission_type_id int NOT NULL DEFAULT 2;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_tenant_season_pass_admission_type'
    ) THEN
        ALTER TABLE tenant
            ADD CONSTRAINT chk_tenant_season_pass_admission_type
            CHECK (season_pass_admission_type_id IN (1, 2));
    END IF;
END $$;
```

Rerunnable: run 1 adds the column (the `DEFAULT 2` populates every existing row as part of
the single ALTER; Postgres 11+ does this as a metadata-only fill) and creates the CHECK;
run 2 skips the column via `IF NOT EXISTS` and skips the CHECK via the `pg_constraint` name
guard. Purely additive; between migrate and deploy the running app never reads the column,
so it sits at its default until the new code ships.

### 4.2 Enum: `Services/Repositories/Data/TenantData/SeasonPassAdmissionType.cs` (NEW file)

```csharp
namespace Services.Repositories.Data.TenantData
{
    /// <summary>
    /// Tenant-configurable season pass gate admission mode. Drives whether
    /// RedeemPassAtGate requires a pre-existing reservation (EventSignUp) or
    /// admits on scan alone, event or no event (WalkUp).
    /// </summary>
    public enum SeasonPassAdmissionType
    {
        EventSignUp = 1,
        WalkUp = 2
    }
}
```

### 4.3 Entity: `Services/Repositories/Data/TenantData/Tenant.cs`

Add next to `SeasonPassesEnabled` (line 114):

```csharp
// Gate admission mode for season passes: 1 (SeasonPassAdmissionType.EventSignUp) requires
// a prior reservation for a specific event before the gate admits the holder; 2
// (SeasonPassAdmissionType.WalkUp, the default) admits on scan alone, with or without a
// calendar event running that day. Stored as an int to match the plain-column mapping
// convention used throughout this class; cast to SeasonPassAdmissionType at the call site.
public int SeasonPassAdmissionTypeId { get; set; } = 2;
```

### 4.4 Repository: `Services/Repositories/TenantRepository.cs` + interface

Add to the `SelectColumns` constant, alongside `season_passes_enabled AS SeasonPassesEnabled`
(line 61):

```sql
season_pass_admission_type_id AS SeasonPassAdmissionTypeId,
```

New update method, mirroring `UpdateRequireReservation` (lines 149-153):

```csharp
public async Task UpdateSeasonPassAdmissionType(Guid tenantId, int admissionTypeId)
{
    const string sql = "UPDATE tenant SET season_pass_admission_type_id = @admissionTypeId WHERE id = @tenantId";
    await _db.Execute(sql, new { tenantId, admissionTypeId });
}
```

`WHERE id = @tenantId` is the tenant table's own primary-key scope (the row being written IS
the tenant). The caller must resolve `tenantId` from `_tenantContext.TenantId`, never from
the request body. `Services/Repositories/Interfaces/ITenantRepository.cs` gains, alongside
line 11's `UpdateRequireReservation`:

```csharp
Task UpdateSeasonPassAdmissionType(Guid tenantId, int admissionTypeId);
```

### 4.5 API plumbing

`webapi/Controllers/API/Data/Tenant/UpdateTenantRequest.cs` addition (this is a full-object
settings PUT; the field is sent on every save, like `RequireIdAtCheckin` today):

```csharp
[Range(1, 2, ErrorMessage = "SeasonPassAdmissionTypeId must be 1 (event sign-up required) or 2 (walk-up).")]
public int SeasonPassAdmissionTypeId { get; set; } = (int)SeasonPassAdmissionType.WalkUp;
```

`webapi/Controllers/TenantController.cs`, `UpdateTenantSettings` (lines 191-211, guarded by
`[Authorize(Policy = TenantPermissions.Policy.SettingsManage)]`, policy confirmed at
`webapi/AuthPolicies/TenantPermissions.cs`): one new line after the existing five
`_tenants.UpdateXxx` calls, all scoped by `_tenantContext.TenantId`:

```csharp
await _tenants.UpdateSeasonPassAdmissionType(_tenantContext.TenantId, request.SeasonPassAdmissionTypeId); // NEW
```

`webapi/Controllers/API/Data/Tenant/GetBrandingResponse.cs` addition (near
`RequireReservationForPasses` at line 23):

```csharp
public int SeasonPassAdmissionTypeId { get; set; } = (int)SeasonPassAdmissionType.WalkUp;
```

`TenantController.GetBranding` mapping, next to line 507's
`RequireReservationForPasses = tenant.RequireReservationForPasses`:

```csharp
SeasonPassAdmissionTypeId = tenant.SeasonPassAdmissionTypeId,
```

## 5. Walk-up reservation schema

### 5.1 Migration: `RidePass.Migrator/Scripts/Script0236_SeasonPassWalkUpAdmission.sql`

```sql
-- Allows a season_pass_reservation row to represent a no-event walk-up admission
-- (season_pass_admission_type_id = 2 tenants with zero calendar events on a given
-- operating day). Previously every reservation was anchored to event_id NOT NULL;
-- this migration relaxes that to allow EITHER an event anchor OR a check_in_date
-- anchor (never neither), and adds the uniqueness rule that replaces
-- UNIQUE (season_pass_purchase_id, event_id) for the no-event case.
--
-- Existing rows are untouched: every row inserted by the app to date has
-- event_id NOT NULL (RedeemPassAtGate today always requires an EventId - see
-- SeasonPassGateRedeemRequest [Required] EventId), so every existing row already
-- satisfies the new CHECK and needs no backfill.

ALTER TABLE season_pass_reservation
    ALTER COLUMN event_id DROP NOT NULL;

ALTER TABLE season_pass_reservation
    ADD COLUMN IF NOT EXISTS check_in_date date NULL;
-- check_in_date is populated ONLY on no-event walk-up rows (event_id IS NULL),
-- holding the tenant-local calendar date of admission (computed server-side from
-- the tenant's timezone, never UTC "today"). Event-anchored rows (event_id NOT
-- NULL) leave this NULL; the event's own scheduled date is the source of truth
-- for those rows, exactly as today.

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_season_pass_reservation_anchor'
    ) THEN
        ALTER TABLE season_pass_reservation
            ADD CONSTRAINT chk_season_pass_reservation_anchor
            CHECK (event_id IS NOT NULL OR check_in_date IS NOT NULL);
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS uk_season_pass_reservation_walkup
    ON season_pass_reservation (season_pass_purchase_id, check_in_date)
    WHERE event_id IS NULL;
```

**Why the partial index**: the existing `UNIQUE (season_pass_purchase_id, event_id)`
(Script0035 line 83) stays exactly as it is; it keeps protecting event-anchored rows. It
cannot protect no-event rows because Postgres treats every NULL in a unique key as distinct,
so two walk-up rows for the same pass would never collide on it. The partial index
`uk_season_pass_reservation_walkup` is scoped `WHERE event_id IS NULL` and keys on
`(season_pass_purchase_id, check_in_date)`: one rider checks in once per tenant-local
calendar day, and a raced second insert hits a unique violation the controller turns into
the idempotent already-checked-in response.

Rerunnable, statement by statement: `DROP NOT NULL` on an already-nullable column is a
silent no-op in Postgres; `ADD COLUMN IF NOT EXISTS` skips; the DO-block guard finds the
constraint by name and skips; `CREATE UNIQUE INDEX IF NOT EXISTS` skips. Purely additive
(a NOT NULL relaxation plus new column/CHECK/index; nothing tightened or dropped, so no
expand-then-contract staging applies). Between migrate and deploy, the running app only
inserts rows with `event_id` set, satisfying the CHECK and never touching the partial index.

### 5.2 Entity: `Services/Repositories/Data/PaymentData/SeasonPass.cs`

`SeasonPassReservation` (lines 165-174) changes:

```csharp
public class SeasonPassReservation
{
    public Guid Id { get; set; }
    public Guid SeasonPassPurchaseId { get; set; }
    public Guid? EventId { get; set; }          // CHANGED: Guid -> Guid?, NULL for no-event walk-ups
    public DateTime? CheckInDate { get; set; }  // NEW: tenant-local date, set only on no-event rows
    public string Status { get; set; } = "reserved";    // reserved | checked_in | cancelled
    public DateTime ReservedAt { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}

public class SeasonPassReservationWithContext : SeasonPassReservation
{
    public string? EventTitle { get; set; }       // CHANGED: string -> string?, null for no-event rows
    public DateTime? EventStartsAt { get; set; }  // CHANGED: DateTime -> DateTime?
    public DateTime? EventEndsAt { get; set; }    // CHANGED: DateTime -> DateTime?
}
```

`CheckInDate` is `DateTime?`, matching every sibling date property in this file (decision 5).
Changing `EventId` to `Guid?` makes the compiler flag every call site that assumed non-null;
the audit of those sites is covered by the reporting deltas in section 10 (the only
non-obvious consumers found were `ReportsRepository` and `LookupCheckInByToken`).

## 6. Backend gate redemption and mode enforcement

### 6.1 Retained-validation table for `RedeemPassAtGate`

Verified against `webapi/Controllers/SeasonPassController.cs` (method starts at line 1124).
"RETAINED, extended" means the same check runs but now operates on a shared `dayLocal`
(= the event's local day when an event was picked, tenant-local today otherwise).

| Lines | Check | Status |
|---|---|---|
| 1126 | `_tenantContext.IsResolved` | RETAINED UNCHANGED |
| 1127 | Resolve `staffId` from the JWT | RETAINED UNCHANGED |
| 1129-1134 | Token lookup + `pass.TenantId != _tenantContext.TenantId` (404, no tenant-existence leak) | RETAINED UNCHANGED |
| 1135-1136 | `pass.Status == "pending"` rejected | RETAINED UNCHANGED |
| 1137-1138 | `pass.Status != "paid"` rejected | RETAINED UNCHANGED |
| 1140-1141 | Product load, null-product guard | RETAINED UNCHANGED |
| 1143-1145 | Event lookup + `Status == "scheduled"` (only when `request.EventId` is present) | RETAINED UNCHANGED |
| 1147-1154 | Event must be running today in tenant tz (walk-up is same-day only) | RETAINED, extended: the tz/todayLocal computation moves a few lines earlier so the no-event branch can share it |
| 1156-1159 | Season window (`ValidFromDate`/`ValidToDate`) | RETAINED, extended: checked against `dayLocal` |
| 1161-1166 | `days_of_week` product restriction | RETAINED, extended: same substitution |
| 1168-1182 | Registration gate (photo + product waiver) via `RegistrationBlockReason`, then the event's rider waiver via `_waiverGate.BlockReason` | Registration half RETAINED in both branches. Event rider-waiver half RETAINED UNCHANGED when an event is present, explicitly SKIPPED when there is none (no event document to enforce) |
| 1184-1186 | Advisory lock keyed `season-pass-redeem:{pass.Id}` via `_db.AcquireAdvisoryLock` | RETAINED UNCHANGED. Keyed on the pass alone, so it already serializes the no-event branch with no key change |
| 1188-1199 | `GetReservation(pass.Id, eventId)`; if `checked_in`, idempotent `AlreadyAdmitted: true` | RETAINED UNCHANGED for the event branch; mirrored (new method) for the no-event branch |
| 1200-1214 | `reserved` flips to `checked_in` via `UpdateReservationStatus` (Reserve already burned the credit) | RETAINED UNCHANGED. This flip is Mode A's entire admission path |
| 1216-1222 | `burnCredit = product.Kind == "credits"`, `CreateGateCheckIn`, zero-credits rejection | RETAINED UNCHANGED for the event branch. The no-event branch uses the new sibling `CreateWalkUpGateCheckIn` |
| 1223-1229 | Success response shape | RETAINED UNCHANGED for the event branch; mirrored for the no-event branch |

New logic, all additive: the Mode A null-EventId rejection, the Mode A
no-live-reservation rejection (under the existing lock), and the no-event branch.

### 6.2 DTO: `webapi/Controllers/API/Data/SeasonPass/SeasonPassGateRedeemRequest.cs`

Current content is `[Required] public Guid EventId { get; set; }`. New content:

```csharp
namespace webapi.Controllers.API.Data.SeasonPass
{
    /// <summary>Gate redemption of a scanned season pass. When <see cref="EventId"/> is set the
    /// pass is admitted against one of today's events (walk-up, or the Mode A sign-up flip). When
    /// it is null the tenant is running Mode B (walk-up) with zero calendar events today, and the
    /// pass is admitted against the tenant-local operating day instead - see
    /// SeasonPassController.RedeemPassAtGate. The event is always chosen client-side (the scanner
    /// auto-selects when only one event is running, and offers a null/"no event today" option when
    /// zero are) so the server never has to guess.</summary>
    public class SeasonPassGateRedeemRequest
    {
        public Guid? EventId { get; set; }
    }
}
```

`[Required]` is removed; a null `EventId` is now a legal Mode B request shape.

### 6.3 Modified `RedeemPassAtGate`

```csharp
[Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
[HttpPost("Pass/{token:guid}/Redeem")]
public async Task<IActionResult> RedeemPassAtGate(Guid token, [FromBody] SeasonPassGateRedeemRequest request)
{
    if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved."); // RETAINED L1126

    Guid? staffId = TryGetUserId(out var sid) ? sid : (Guid?)null; // RETAINED L1127

    var pass = await _passes.GetPurchaseByRedemptionToken(token); // RETAINED L1129
    if (pass is null || pass.TenantId != _tenantContext.TenantId) // RETAINED L1130-1134
    {
        // Same shape as the lookup: don't reveal that the token exists on another tenant.
        return new ApiResponses().NotFoundResult("Pass not found.");
    }
    if (pass.Status == "pending") // RETAINED L1135-1136
        return new ApiResponses().BadRequestResult("This pass's payment hasn't settled yet, so it can't be used.");
    if (pass.Status != "paid") // RETAINED L1137-1138
        return new ApiResponses().BadRequestResult("This pass was refunded or cancelled and is no longer valid.");

    var product = await _passes.GetProduct(pass.ProductId, _tenantContext.TenantId); // RETAINED L1140
    if (product is null)
        return new ApiResponses().BadRequestResult("Pass product missing - contact support."); // RETAINED L1141 [dash replaced]

    // NEW: which admission mode this tenant runs (tenant.season_pass_admission_type_id).
    var admissionType = (SeasonPassAdmissionType)_tenantContext.Tenant.SeasonPassAdmissionTypeId;

    // RETAINED L1149-1150 (tz + "today"), moved a few lines earlier so both the event branch and
    // the new no-event branch can use it without duplicating the lookup.
    var tz = TimeZoneInfo.FindSystemTimeZoneById(_tenantContext.Tenant.Timezone);
    var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;

    // NEW: Mode A has no admission path without a prior sign-up, so a null EventId is rejected
    // up front, before any event/no-event branching.
    if (admissionType == SeasonPassAdmissionType.EventSignUp && request.EventId is null)
    {
        return new ApiResponses().BadRequestResult(
            "This track requires an event sign-up before riding. The rider must reserve a spot " +
            "for the event from My Passes first, then scan again.");
    }

    DateTime dayLocal; // NEW: the operating day this scan validates against
    Guid? eventId = request.EventId;

    if (eventId is Guid evId)
    {
        var ev = await _events.GetById(evId, _tenantContext.TenantId); // RETAINED L1143
        if (ev is null || ev.Status != "scheduled") // RETAINED L1144-1145
            return new ApiResponses().BadRequestResult("That event isn't available for check-in.");

        // RETAINED L1151-1154: walk-up (and Mode A's sign-up flip) is same-day only.
        var eventDayLocal = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(ev.StartsAt, DateTimeKind.Utc), tz).Date;
        if (eventDayLocal != todayLocal)
            return new ApiResponses().BadRequestResult(
                "That event isn't running today - walk-up redemption is same-day only."); // [dash replaced]
        dayLocal = eventDayLocal;
    }
    else
    {
        // NEW: Mode B, zero events running today. The rider's own admission window is validated
        // against the tenant-local operating day instead of an event's date.
        dayLocal = todayLocal;
    }

    // RETAINED L1156-1159 (season window), now checked against dayLocal so the same lines cover
    // both branches.
    if (dayLocal < pass.ValidFromDate.Date)
        return new ApiResponses().BadRequestResult(
            $"This pass's season hasn't started yet - it's valid from {pass.ValidFromDate:MMM d, yyyy}."); // [dash replaced]
    if (dayLocal > pass.ValidToDate.Date)
        return new ApiResponses().BadRequestResult($"This pass's season ended {pass.ValidToDate:MMM d, yyyy}.");

    // RETAINED L1161-1166 (days_of_week), same substitution.
    if (product.Kind == "days_of_week" && product.ValidDaysOfWeek is { Length: > 0 })
    {
        var dow = (int)dayLocal.DayOfWeek; // 0=Sun..6=Sat
        if (!product.ValidDaysOfWeek.Contains(dow))
            return new ApiResponses().BadRequestResult("This pass isn't valid on this day of the week.");
    }

    // RETAINED L1168-1182: registration gate is identical in both branches.
    var ctx = await _passes.GetPassForGateCheckIn(pass.Id, _tenantContext.TenantId);
    if (ctx is not null)
    {
        var registrationBlock = await RegistrationBlockReason(ctx);
        if (registrationBlock is not null) return new ApiResponses().BadRequestResult(registrationBlock);

        // RETAINED for the event branch. NEW: skipped entirely when there is no event - there is
        // no event document to carry a rider waiver, so nothing to enforce here. The pass
        // product's own waiver requirement was already covered by RegistrationBlockReason above.
        if (eventId is Guid waiverEventId && ctx.WaiverSignatureId is null)
        {
            var waiverBlock = await _waiverGate.BlockReason(_tenantContext.TenantId, waiverEventId,
                riderAudience: true, ctx.HolderUserId, ctx.HolderEmail, ctx.HolderName);
            if (waiverBlock is not null) return new ApiResponses().BadRequestResult(waiverBlock);
        }
    }

    // RETAINED L1184-1186: same per-pass advisory lock. Keyed on pass.Id only, so it already
    // serializes the no-event branch too - one pass has exactly one lock no matter how many
    // anchors (today's events, or the no-event day) it could be admitted against.
    await using var redeemLock = await _db.AcquireAdvisoryLock($"season-pass-redeem:{pass.Id}");

    if (eventId is Guid gateEventId)
    {
        var existing = await _passes.GetReservation(pass.Id, gateEventId); // RETAINED L1188

        // NEW: Mode A only admits a pass that was reserved ahead of time via Reserve. A live
        // reservation (reserved or checked_in) is the proof of sign-up; anything else (none, or
        // only a cancelled row) is rejected here, under the lock, so a raced cancel can't slip a
        // walk-up through between the earlier checks and this statement.
        if (admissionType == SeasonPassAdmissionType.EventSignUp
            && existing?.Status is not ("reserved" or "checked_in"))
        {
            return new ApiResponses().BadRequestResult(
                "This track requires an event sign-up before riding. The rider must reserve a spot " +
                "for the event from My Passes first, then scan again.");
        }

        if (existing is not null && existing.Status == "checked_in") // RETAINED L1189-1199
        {
            return new ApiResponses().OkResult(new
            {
                ReservationId = existing.Id,
                AlreadyAdmitted = true,
                CheckedInAtUtc = existing.CheckedInAt is null
                    ? null : (DateTime?)DateTime.SpecifyKind(existing.CheckedInAt.Value, DateTimeKind.Utc),
                pass.CreditsRemaining,
            });
        }
        if (existing is not null && existing.Status == "reserved") // RETAINED L1200-1214
        {
            // Pre-booked via Reserve, which already burned the credit - just flip it. For Mode A
            // this IS the admission; CreateGateCheckIn is never called in Mode A.
            var flipped = await _passes.UpdateReservationStatus(existing.Id, _tenantContext.TenantId, "checked_in", staffId);
            if (flipped == 0)
                return new ApiResponses().BadRequestResult(
                    "This pass can't be checked in. It may be refunded, cancelled, or already checked in.");
            return new ApiResponses().OkResult(new
            {
                ReservationId = existing.Id,
                AlreadyAdmitted = false,
                CheckedInAtUtc = (DateTime?)DateTime.UtcNow,
                pass.CreditsRemaining,
            });
        }

        // Only Mode B reaches here - Mode A already returned above when there was no live
        // reservation for this event.
        var burnCredit = product.Kind == "credits"; // RETAINED L1216
        var result = await _passes.CreateGateCheckIn(pass.Id, _tenantContext.TenantId, gateEventId, staffId, burnCredit); // RETAINED L1217
        if (result is null) // RETAINED L1218-1222
        {
            return new ApiResponses().BadRequestResult(
                "This pass has no ride credits left. If that's a mistake, credits can be adjusted from the customer's admin page.");
        }
        return new ApiResponses().OkResult(new // RETAINED L1223-1229
        {
            ReservationId = result.Value.ReservationId,
            AlreadyAdmitted = false,
            CheckedInAtUtc = (DateTime?)DateTime.UtcNow,
            CreditsRemaining = result.Value.CreditsRemaining,
        });
    }
    else
    {
        // NEW: Mode B, zero events today. Anchor is (pass, today's tenant-local date) instead of
        // (pass, event) - see Script0236.
        var existingWalkUp = await _passes.GetWalkUpCheckIn(pass.Id, _tenantContext.TenantId, dayLocal);
        if (existingWalkUp is not null && existingWalkUp.Status == "checked_in")
        {
            return new ApiResponses().OkResult(new
            {
                ReservationId = existingWalkUp.Id,
                AlreadyAdmitted = true,
                CheckedInAtUtc = existingWalkUp.CheckedInAt is null
                    ? null : (DateTime?)DateTime.SpecifyKind(existingWalkUp.CheckedInAt.Value, DateTimeKind.Utc),
                pass.CreditsRemaining,
            });
        }

        var burnCredit = product.Kind == "credits";
        var result = await _passes.CreateWalkUpGateCheckIn(pass.Id, _tenantContext.TenantId, dayLocal, staffId, burnCredit);
        if (result is null)
        {
            // Same message as the event branch, verbatim.
            return new ApiResponses().BadRequestResult(
                "This pass has no ride credits left. If that's a mistake, credits can be adjusted from the customer's admin page.");
        }
        return new ApiResponses().OkResult(new
        {
            ReservationId = result.Value.ReservationId,
            AlreadyAdmitted = false,
            CheckedInAtUtc = (DateTime?)DateTime.UtcNow,
            CreditsRemaining = result.Value.CreditsRemaining,
        });
    }
}
```

`_tenantContext.Tenant` is confirmed available on `ITenantContext`
(`webapi/Multitenancy/ITenantContext.cs`) and is already used at the current line 1149 for
`.Timezone`.

### 6.4 Repository additions: `Services/Repositories/SeasonPassRepository.cs`

The original `CreateGateCheckIn` (lines 540-558) burns and upserts in one statement with a
direct `season_pass_purchase.tenant_id = @tenantId` predicate. The new sibling mirrors it
with the no-event anchor; `ON CONFLICT` targets the Script0236 partial unique index, so it
can never collide with an event-anchored row:

```csharp
public async Task<(Guid ReservationId, int? CreditsRemaining)?> CreateWalkUpGateCheckIn(
    Guid passPurchaseId, Guid tenantId, DateTime checkInDate, Guid? staffUserId, bool burnCredit)
{
    const string sql = @"
        WITH burn AS (
            UPDATE season_pass_purchase
            SET credits_remaining = CASE WHEN @burnCredit THEN credits_remaining - 1
                                         ELSE credits_remaining END,
                updated_at = now()
            WHERE id = @passPurchaseId AND tenant_id = @tenantId AND status = 'paid'
              AND (NOT @burnCredit
                   OR (credits_remaining IS NOT NULL AND credits_remaining > 0))
            RETURNING id, credits_remaining
        )
        INSERT INTO season_pass_reservation
            (season_pass_purchase_id, event_id, check_in_date, status, checked_in_at, checked_in_by_user_id)
        SELECT id, NULL, @checkInDate, 'checked_in', now(), @staffUserId FROM burn
        ON CONFLICT (season_pass_purchase_id, check_in_date) WHERE event_id IS NULL DO UPDATE
            SET status = 'checked_in', checked_in_at = now(),
                checked_in_by_user_id = EXCLUDED.checked_in_by_user_id
            WHERE season_pass_reservation.status = 'cancelled'
        RETURNING id, (SELECT credits_remaining FROM burn)";
    var row = (await _db.Query<(Guid Id, int? CreditsRemaining)>(sql,
        new { passPurchaseId, tenantId, checkInDate = checkInDate.Date, staffUserId, burnCredit })).FirstOrDefault();
    return row.Id == Guid.Empty ? null : (row.Id, row.CreditsRemaining);
}
```

`GetWalkUpCheckIn`: `season_pass_reservation` has no tenant_id, so tenant scope is an
explicit join to `season_pass_purchase`, the same pattern `UpdateReservationStatus` already
uses (line 650):

```csharp
public async Task<SeasonPassReservation?> GetWalkUpCheckIn(Guid passPurchaseId, Guid tenantId, DateTime checkInDate)
{
    // Tenant scope via join: season_pass_reservation carries no tenant_id of its own.
    const string sql = @"
        SELECT r.id, r.season_pass_purchase_id AS SeasonPassPurchaseId,
               r.event_id AS EventId, r.check_in_date AS CheckInDate, r.status,
               r.reserved_at AS ReservedAt, r.checked_in_at AS CheckedInAt, r.cancelled_at AS CancelledAt
        FROM season_pass_reservation r
        JOIN season_pass_purchase p ON p.id = r.season_pass_purchase_id
        WHERE r.season_pass_purchase_id = @passPurchaseId
          AND p.tenant_id = @tenantId
          AND r.event_id IS NULL
          AND r.check_in_date = @checkInDate
        LIMIT 1";
    return (await _db.Query<SeasonPassReservation>(sql,
        new { passPurchaseId, tenantId, checkInDate = checkInDate.Date })).FirstOrDefault();
}
```

The caller inspects `.Status`: a `cancelled` row falls through to `CreateWalkUpGateCheckIn`,
which revives it via `ON CONFLICT ... WHERE status = 'cancelled'`, mirroring
`CreateGateCheckIn`'s existing revive-cancelled behavior.

`ListReservationsForPurchaseOnDate` (lines 617-631) currently inner-joins `event` and
carries no tenant predicate (it relies on its one caller, `LookupPassByToken`, having
verified `pass.TenantId`). It must become a LEFT JOIN once some rows have no event, and it
gains an explicit tenant join as defense in depth:

```csharp
public async Task<List<SeasonPassReservationWithContext>> ListReservationsForPurchaseOnDate(
    Guid purchaseId, Guid tenantId, DateTime atUtc, DateTime untilUtc, DateTime localDate)
{
    // event_id IS NOT NULL rows are matched by the event's UTC window (unchanged). event_id IS
    // NULL rows (no-event walk-up admissions) are matched by check_in_date in the tenant's local
    // calendar, since they have no event start/end to bound them. LEFT JOIN because a walk-up row
    // has no event to join to.
    const string sql = @"
        SELECT r.id, r.season_pass_purchase_id AS SeasonPassPurchaseId,
               r.event_id AS EventId, r.check_in_date AS CheckInDate, r.status,
               r.reserved_at AS ReservedAt, r.checked_in_at AS CheckedInAt,
               r.cancelled_at AS CancelledAt,
               e.title AS EventTitle, e.starts_at AS EventStartsAt, e.ends_at AS EventEndsAt
        FROM season_pass_reservation r
        JOIN season_pass_purchase p ON p.id = r.season_pass_purchase_id
        LEFT JOIN event e ON e.id = r.event_id
        WHERE r.season_pass_purchase_id = @purchaseId
          AND p.tenant_id = @tenantId
          AND (
                (r.event_id IS NOT NULL AND e.starts_at < @untilUtc AND e.ends_at >= @atUtc)
             OR (r.event_id IS NULL AND r.check_in_date = @localDate)
              )
        ORDER BY COALESCE(e.starts_at, r.checked_in_at)";
    return (await _db.Query<SeasonPassReservationWithContext>(sql,
        new { purchaseId, tenantId, atUtc, untilUtc, localDate = localDate.Date })).ToList();
}
```

The single call site (`LookupPassByToken`) passes `_tenantContext.TenantId` and its existing
`dayStartLocal` variable as the two new arguments.

`GetReservation(Guid purchaseId, Guid eventId)` (line 573) is untouched; it is only ever
called from the event branch with a real event id.

`ISeasonPassRepository` additions/changes:

```csharp
/// <summary>Resolve a no-event walk-up admission for one pass on one tenant-local calendar day.
/// Tenant-scoped through the join to season_pass_purchase - season_pass_reservation has no
/// tenant_id of its own. Null if no such row exists yet.</summary>
Task<SeasonPassReservation?> GetWalkUpCheckIn(Guid passPurchaseId, Guid tenantId, DateTime checkInDate);

/// <summary>
/// No-event walk-up gate admission in ONE atomic statement: optionally burns a credit (guarded
/// &gt; 0, paid, tenant-scoped) and upserts the (pass, check-in date) reservation straight to
/// checked_in (reviving a cancelled row if present). Returns null when the credit guard fails;
/// otherwise the reservation id and post-burn credits. Caller must hold the per-pass advisory
/// lock and have pre-checked GetWalkUpCheckIn for an existing checked_in row.
/// </summary>
Task<(Guid ReservationId, int? CreditsRemaining)?> CreateWalkUpGateCheckIn(
    Guid passPurchaseId, Guid tenantId, DateTime checkInDate, Guid? staffUserId, bool burnCredit);

// CHANGED signature (was (Guid purchaseId, DateTime atUtc, DateTime untilUtc)):
Task<List<SeasonPassReservationWithContext>> ListReservationsForPurchaseOnDate(
    Guid purchaseId, Guid tenantId, DateTime atUtc, DateTime untilUtc, DateTime localDate);
```

### 6.5 `LookupPassByToken` response additions

Current shape verified at lines 990-1027. Additions inside the method:

```csharp
// NEW: lets the scanner UI branch on mode without a second round trip.
var admissionType = (SeasonPassAdmissionType)_tenantContext.Tenant.SeasonPassAdmissionTypeId;

// NEW: only meaningful in Mode B - Mode A always requires picking a today's-event to sign up
// against, so "eligible today with no event" isn't a concept the UI needs from this tenant.
var walkUpEligibleToday = admissionType == SeasonPassAdmissionType.WalkUp
    && registrationComplete
    && dayStartLocal >= pass.ValidFromDate.Date
    && dayStartLocal <= pass.ValidToDate.Date
    && (product?.Kind != "days_of_week"
        || product.ValidDaysOfWeek is not { Length: > 0 }
        || product.ValidDaysOfWeek.Contains((int)dayStartLocal.DayOfWeek));
```

Response object additions:

```csharp
AdmissionTypeId = (int)admissionType,
WalkUpEligibleToday = walkUpEligibleToday,
```

`todaysReservations` now includes any no-event walk-up check-in for today because of the
`ListReservationsForPurchaseOnDate` change above. In the mapping of each reservation to the
response, `EventTitle` becomes `r.EventTitle ?? "Walk-up admission"` (decision 4), and
`EventStartsAtUtc`/`EventEndsAtUtc` map from the now-nullable entity fields. Everything else
in the response (`TodaysEvents`, `RegistrationComplete`, etc.) is unchanged.

### 6.6 Mode behavior for every other flow

- **`Reserve` (lines 867-957)**: unchanged in both modes, no gating added. Mode B tracks can
  still run capacity-limited events and riders reserve for those exactly as today. Reserve
  burns the credit before writing the reservation (lines 928-938); that burn is what makes
  the Mode A gate-side flip safe to never burn again.
- **`Reservations/{id}/CheckIn` (lines 1074-1113)**: unchanged in both modes. Pre-booked
  check-in by reservation id exists independently of walk-up.
- **`AdjustCredits` (lines 1237-1260)**: unchanged in both modes; support/admin override.
- **`RedeemPassAtGate`**: the only flow the setting branches (section 6.3).
- **`LookupPassByToken`**: unchanged validation, additive response fields only (section 6.5).

No flow is hidden or repurposed by the setting; Mode A tenants simply never see
`WalkUpEligibleToday: true` and their scanner never offers a no-event redemption.

## 7. Wristband generalization to pass admissions

Anchor rule: a band links to an ADMISSION, i.e. a `season_pass_reservation` row in status
`checked_in`, never to `season_pass_purchase` directly. That scopes the band's validity to
one day/event and mirrors the existing ticket anchor.

### 7.1 Migration: `RidePass.Migrator/Scripts/Script0237_SeasonPassWristbands.sql`

```sql
-- Extends event_wristband so a band can anchor to a season pass ADMISSION
-- (a season_pass_reservation row, status = checked_in) instead of only an
-- event_ticket_purchase row. A pass-linked band's scope is inherited from the
-- admission it was issued at: the event, when one ran, or the tenant-local
-- calendar date, when the rider walked in on a day with no event on the
-- calendar (the Script0236 no-event admission path).
--
-- Additive + idempotent. Depends on Script0189 (event_wristband) and Script0236
-- (season_pass_reservation.event_id nullable + check_in_date); this script does
-- not touch season_pass_reservation itself.

-- (a) A band no longer requires a ticket - a season-pass admission can anchor it instead.
--     Re-running this ALTER when the column is already nullable is a no-op; Postgres does
--     not error on dropping a constraint that is already absent.
ALTER TABLE event_wristband ALTER COLUMN ticket_id DROP NOT NULL;

-- (b) A band no longer requires an event - a no-event walk-up admission has none.
ALTER TABLE event_wristband ALTER COLUMN event_id DROP NOT NULL;

-- (c) The new anchor: which admission this band means.
ALTER TABLE event_wristband
    ADD COLUMN IF NOT EXISTS season_pass_reservation_id uuid NULL
        REFERENCES season_pass_reservation(id) ON DELETE CASCADE;

-- (d) Tenant-local admission date, copied from the reservation's check_in_date at link time.
--     NULL for every event-anchored row (ticket rows and event-day pass rows alike); only a
--     no-event pass row carries it, and it is what the walk-up-day uniqueness index below keys on.
ALTER TABLE event_wristband ADD COLUMN IF NOT EXISTS valid_on_date date NULL;

-- (e) Exactly one anchor per row: a ticket XOR a season-pass admission, never both, never neither.
--     True for every existing row today (ticket_id NOT NULL, season_pass_reservation_id always
--     NULL pre-migration -> 1 + 0 = 1), so adding this CHECK validates clean against current data.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_event_wristband_anchor') THEN
        ALTER TABLE event_wristband ADD CONSTRAINT chk_event_wristband_anchor
            CHECK (((ticket_id IS NOT NULL)::int + (season_pass_reservation_id IS NOT NULL)::int) = 1);
    END IF;
END $$;

-- (f) A row must be scoped to SOMETHING - an event or a calendar date - never neither.
--     True for every existing row today (event_id NOT NULL pre-migration).
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_event_wristband_scope') THEN
        ALTER TABLE event_wristband ADD CONSTRAINT chk_event_wristband_scope
            CHECK (event_id IS NOT NULL OR valid_on_date IS NOT NULL);
    END IF;
END $$;

-- (g) No-event walk-up uniqueness: one meaning per band per tenant per calendar day, mirroring
--     the per-event uniqueness rationale in Script0189 (cheap band packs repeat numbers) for the
--     case where there is no event to scope by.
CREATE UNIQUE INDEX IF NOT EXISTS uk_event_wristband_code_walkup
    ON event_wristband (tenant_id, valid_on_date, lower(code))
    WHERE event_id IS NULL;

-- (h) One admission wears one band (replacement deletes the old row first, same as tickets).
CREATE UNIQUE INDEX IF NOT EXISTS uk_event_wristband_reservation
    ON event_wristband (season_pass_reservation_id)
    WHERE season_pass_reservation_id IS NOT NULL;
```

**How uniqueness extends:**

- Event-day pass admission: the band row carries the event's `event_id`, so it participates
  in the existing `uk_event_wristband_code (tenant_id, event_id, lower(code))` exactly like
  a ticket row. No new index needed.
- No-event pass admission: `event_id IS NULL`, so the existing per-event index cannot apply.
  `uk_event_wristband_code_walkup` gives the same per-scope-unit guarantee with the scope
  unit being tenant + calendar day: band 0347 on July 24 and band 0347 on July 25 never
  collide, honoring the cheap-band-pack rationale Script0189 documents.
- One-band-per-wearer: extends via (h), mirroring `uk_event_wristband_ticket`. Re-linking a
  new band to an already-banded admission replaces the old row (delete-then-insert in the
  repository); the lost band stops resolving the instant the new one is linked.
- `uk_event_wristband_ticket (ticket_id)` is unaffected by `ticket_id` becoming nullable:
  Postgres unique indexes treat every NULL as distinct, so pass-anchored rows never collide
  under it.

Rerunnable: (a)/(b) no-op when already nullable; (c)/(d) `IF NOT EXISTS` skips; (e)/(f)
name-guarded DO blocks skip; (g)/(h) `IF NOT EXISTS` skips. Purely additive.

### 7.2 DTOs: `webapi/Controllers/API/Data/Redemption/WristbandDtos.cs`

The three wristband DTOs already share this one file; this design widens them in place
rather than splitting (splitting is a separable cleanup, noted in open questions).

```csharp
using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Redemption
{
    public class LinkWristbandRequest
    {
        // Exactly one of these two must be set - validated in the controller, not by attribute,
        // because "exactly one of two optional fields" isn't expressible with [Required] alone.
        public Guid? TicketId { get; set; }
        public Guid? SeasonPassReservationId { get; set; }
        [Required, MaxLength(200)] public string Code { get; set; } = null!;
    }

    public class UnlinkWristbandRequest
    {
        public Guid? TicketId { get; set; }
        public Guid? SeasonPassReservationId { get; set; }
    }

    public class WristbandCodesRequest
    {
        // Both default empty; the controller requires at least one to be non-empty.
        [MaxLength(200)] public List<Guid> TicketIds { get; set; } = new();
        [MaxLength(200)] public List<Guid> ReservationIds { get; set; } = new();
    }
}
```

### 7.3 Reservation link context (cross-repository read)

`Link` must load the reservation tenant-scoped through `season_pass_purchase` before
trusting its scope fields. This mirrors `GetReservationForCheckIn`
(`Services/Repositories/SeasonPassRepository.cs` lines 583-600), so it lives on
`ISeasonPassRepository`, not the wristband repository.

`Services/Repositories/Data/PaymentData/SeasonPassReservationLinkContext.cs` (NEW file):

```csharp
namespace Services.Repositories.Data.PaymentData
{
    /// <summary>Minimal tenant-scoped view of a reservation for band-link validation: is it
    /// actually checked in, and what does the band inherit as its scope (event or date)?</summary>
    public class SeasonPassReservationLinkContext
    {
        public string Status { get; set; } = null!;
        public Guid? EventId { get; set; }
        public DateOnly? CheckInDate { get; set; }
        public string PurchaserName { get; set; } = null!;
    }
}
```

`ISeasonPassRepository` addition and implementation:

```csharp
/// <summary>Tenant-scoped reservation read for wristband linking: confirms the admission is
/// checked_in and returns the event/date scope a band linked to it should inherit. Joins
/// through season_pass_purchase - season_pass_reservation has no tenant_id of its own.</summary>
Task<SeasonPassReservationLinkContext?> GetReservationForBandLink(Guid reservationId, Guid tenantId);
```

```csharp
public async Task<SeasonPassReservationLinkContext?> GetReservationForBandLink(Guid reservationId, Guid tenantId)
{
    const string sql = @"
        SELECT r.status, r.event_id AS EventId, r.check_in_date AS CheckInDate,
               p.purchaser_name AS PurchaserName
        FROM season_pass_reservation r
        JOIN season_pass_purchase p ON p.id = r.season_pass_purchase_id
        WHERE r.id = @reservationId AND p.tenant_id = @tenantId
        LIMIT 1";
    return (await _db.Query<SeasonPassReservationLinkContext>(sql, new { reservationId, tenantId })).FirstOrDefault();
}
```

Tenant scope proof: the only predicate on `season_pass_reservation` is `r.id`; tenant scope
comes exclusively through `p.tenant_id = @tenantId` on the joined purchase row.

### 7.4 `webapi/Controllers/WristbandController.cs`

The controller keeps its `Gate()` guard (`_tenantContext.IsResolved` +
`_tenantContext.Tenant.WristbandsEnabled`, lines 36-42) and its `SalesRedeem` policy, and
gains a season-pass branch in each action. Inject `ISeasonPassRepository` (already DI
registered). Key deltas per action:

**`Link`** validates exactly one anchor is set, then branches. Ticket branch unchanged.
Pass branch:

```csharp
var reservationId = req.SeasonPassReservationId!.Value;
var reservation = await _passes.GetReservationForBandLink(reservationId, TenantId);
if (reservation is null) return new ApiResponses().NotFoundResult("Season pass admission not found.");
if (reservation.Status != "checked_in")
    return new ApiResponses().BadRequestResult("A band can only be linked after the pass has been admitted at the gate.");

var conflictHolderPass = await _wristbands.LinkToReservation(
    TenantId, reservationId, reservation.EventId, reservation.CheckInDate, code, UserId);
if (conflictHolderPass is not null)
{
    var scope = reservation.EventId is not null ? "this event" : "today";
    return new ApiResponses().BadRequestResult(
        $"That band is already on {conflictHolderPass} for {scope}. Use a different band, or unlink theirs first.");
}
return new ApiResponses().OkResult(new { code });
```

**`Unlink`** validates exactly one anchor and dispatches to `UnlinkTicket` or
`UnlinkReservation`.

**`Resolve`** computes `todayLocal` (tenant timezone, `DateOnly`) and passes it into the
widened `ResolveCode`; when the hit's `Source == "season_pass"` it returns the pass shape
(`ReservationId`, `RedemptionToken` = the pass purchase's token, `Name` = purchaser name).

**`Codes`** accepts both id lists and returns `{ tickets: [...], reservations: [...] }`.
This response-shape change is coordinated in the same release with `WristbandService.codes()`
and `loadBands()` in `RedeemTickets.vue` (section 7.6), not a silent break.

### 7.5 `Services/Repositories/WristbandRepository.cs` + interface

`IWristbandRepository` additions:

```csharp
/// <summary>Links a band code to a season pass admission (a checked_in reservation), replacing
/// any band that admission already wears. Returns null on success, or the OTHER admission's
/// holder name when the code is already linked to someone else in the same scope (the reservation's
/// event when it has one, else the tenant's calendar date). Same code to the same reservation
/// again is an idempotent success.</summary>
Task<string?> LinkToReservation(Guid tenantId, Guid reservationId, Guid? eventId, DateOnly? validOnDate,
    string code, Guid? byUserId);

/// <summary>Removes a season pass admission's band link. Returns rows affected.</summary>
Task<int> UnlinkReservation(Guid reservationId, Guid tenantId);

/// <summary>Band codes for a set of season pass reservations (the pass card), keyed by reservation id.</summary>
Task<Dictionary<Guid, string>> GetCodesForReservations(IEnumerable<Guid> reservationIds, Guid tenantId);

// CHANGED signature: the controller now passes the tenant-local date for walk-up band matching.
Task<WristbandResolution?> ResolveCode(Guid tenantId, string code, DateOnly todayLocal);
```

`LinkToReservation` implementation (same pre-check + delete-then-insert + 23505 race
handling as the existing ticket `Link`):

```csharp
public async Task<string?> LinkToReservation(Guid tenantId, Guid reservationId, Guid? eventId,
    DateOnly? validOnDate, string code, Guid? byUserId)
{
    // Whose wrist is this code already on, in the SAME scope this row will occupy (the event,
    // when there is one, else the tenant-local date)? Same reservation = idempotent re-scan;
    // someone else = refuse with a name, mirroring the ticket branch's conflict check.
    var existing = eventId is not null
        ? (await _db.Query<WristbandResolution>(@"
            SELECT w.season_pass_reservation_id AS ReservationId, p.purchaser_name AS PurchaserName
            FROM event_wristband w
            JOIN season_pass_reservation r ON r.id = w.season_pass_reservation_id
            JOIN season_pass_purchase p ON p.id = r.season_pass_purchase_id
            WHERE w.tenant_id = @tenantId AND w.event_id = @eventId AND lower(w.code) = lower(@code)",
            new { tenantId, eventId, code })).FirstOrDefault()
        : (await _db.Query<WristbandResolution>(@"
            SELECT w.season_pass_reservation_id AS ReservationId, p.purchaser_name AS PurchaserName
            FROM event_wristband w
            JOIN season_pass_reservation r ON r.id = w.season_pass_reservation_id
            JOIN season_pass_purchase p ON p.id = r.season_pass_purchase_id
            WHERE w.tenant_id = @tenantId AND w.event_id IS NULL AND w.valid_on_date = @validOnDate
              AND lower(w.code) = lower(@code)",
            new { tenantId, validOnDate, code })).FirstOrDefault();

    if (existing is not null)
    {
        if (existing.ReservationId == reservationId) return null;   // already linked to this admission
        return existing.PurchaserName;
    }

    try
    {
        await _db.ExecuteBatch(new List<(string Sql, object? Param)>
        {
            ("DELETE FROM event_wristband WHERE season_pass_reservation_id = @reservationId AND tenant_id = @tenantId",
                new { reservationId, tenantId }),
            (@"INSERT INTO event_wristband
                   (tenant_id, event_id, season_pass_reservation_id, valid_on_date, code, linked_by_user_id)
               VALUES (@tenantId, @eventId, @reservationId, @validOnDate, @code, @byUserId)",
                new { tenantId, eventId, reservationId, validOnDate, code, byUserId }),
        });
        return null;
    }
    catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
    {
        return "another entrant (it was just linked)";
    }
}

public Task<int> UnlinkReservation(Guid reservationId, Guid tenantId) => _db.Execute(
    "DELETE FROM event_wristband WHERE season_pass_reservation_id = @reservationId AND tenant_id = @tenantId",
    new { reservationId, tenantId });

public async Task<Dictionary<Guid, string>> GetCodesForReservations(IEnumerable<Guid> reservationIds, Guid tenantId)
{
    var ids = reservationIds.Distinct().ToArray();
    if (ids.Length == 0) return new Dictionary<Guid, string>();
    var rows = await _db.Query<EventWristband>(@"
        SELECT season_pass_reservation_id AS SeasonPassReservationId, code
        FROM event_wristband
        WHERE season_pass_reservation_id = ANY(@ids) AND tenant_id = @tenantId",
        new { ids, tenantId });
    return rows.ToDictionary(r => r.SeasonPassReservationId!.Value, r => r.Code);
}
```

`ResolveCode`, widened to a UNION (ticket branch logic unchanged):

```csharp
public async Task<WristbandResolution?> ResolveCode(Guid tenantId, string code, DateOnly todayLocal)
{
    const string sql = @"
        SELECT 'ticket' AS Source, w.ticket_id AS TicketId, w.event_id AS EventId,
               NULL::uuid AS ReservationId, NULL::uuid AS PassPurchaseId,
               w.code, w.linked_at AS LinkedAt,
               t.redemption_token AS RedemptionToken, t.status AS Status,
               t.rider_first_name AS RiderFirstName, t.rider_last_name AS RiderLastName,
               t.purchaser_name AS PurchaserName, t.race_number AS RaceNumber,
               tt.name AS TierName, e.title AS EventTitle, e.starts_at AS SortKey
        FROM event_wristband w
        JOIN event_ticket_purchase t ON t.id = w.ticket_id
        JOIN event_ticket_tier tt ON tt.id = t.tier_id
        JOIN event e ON e.id = w.event_id
        WHERE w.tenant_id = @tenantId AND lower(w.code) = lower(@code)
          AND e.ends_at > now() - interval '1 day'

        UNION ALL

        SELECT 'season_pass' AS Source, NULL::uuid AS TicketId, w.event_id AS EventId,
               w.season_pass_reservation_id AS ReservationId, r.season_pass_purchase_id AS PassPurchaseId,
               w.code, w.linked_at AS LinkedAt,
               p.redemption_token AS RedemptionToken, p.status AS Status,
               NULL AS RiderFirstName, NULL AS RiderLastName,
               p.purchaser_name AS PurchaserName, NULL AS RaceNumber,
               NULL AS TierName, COALESCE(e2.title, 'Walk-up admission') AS EventTitle,
               COALESCE(e2.starts_at, w.linked_at) AS SortKey
        FROM event_wristband w
        JOIN season_pass_reservation r ON r.id = w.season_pass_reservation_id
        JOIN season_pass_purchase p ON p.id = r.season_pass_purchase_id
        LEFT JOIN event e2 ON e2.id = w.event_id
        WHERE w.tenant_id = @tenantId AND p.tenant_id = @tenantId AND lower(w.code) = lower(@code)
          AND (
                (w.event_id IS NOT NULL AND e2.ends_at > now() - interval '1 day')
                OR (w.event_id IS NULL AND w.valid_on_date = @todayLocal)
              )

        ORDER BY SortKey DESC
        LIMIT 1";
    return (await _db.Query<WristbandResolution>(sql, new { tenantId, code, todayLocal })).FirstOrDefault();
}
```

Both UNION branches carry `w.tenant_id = @tenantId`; the season-pass branch additionally
carries `p.tenant_id = @tenantId`, belt and suspenders since `season_pass_reservation` has
no tenant column of its own.

`Services/Repositories/Data/PaymentData/EventWristband.cs` widening: `EventWristband` gains
`Guid? SeasonPassReservationId` and `DateOnly? ValidOnDate`; `EventId` and `TicketId` become
nullable. `WristbandResolution` gains `string Source`, `Guid? ReservationId`,
`Guid? PassPurchaseId`; `TicketId`/`EventId` become nullable; `TicketStatus` is renamed to
`Status` (covers pass status too). The rename is confined to `WristbandController.cs`,
`WristbandRepository.cs`, `IWristbandRepository.cs`, and `EventWristband.cs`, verified as
the only backend files referencing `WristbandResolution`.

### 7.6 Frontend wristband deltas

`vueapp/src/services/WristbandService.ts` generalizes to a link-target union and the new
`Codes` shape:

```typescript
export type WristbandLinkTarget = { ticketId: string } | { seasonPassReservationId: string }

link(target: WristbandLinkTarget, code: string) {
    return axios.post(`${this.apiUrl}/Wristband/Link`, { ...target, code })
}
unlink(target: WristbandLinkTarget) {
    return axios.post(`${this.apiUrl}/Wristband/Unlink`, target)
}
codes(ticketIds: string[], reservationIds: string[] = []) {
    return axios.post<{ data: {
        tickets: { ticketId: string; code: string }[]
        reservations: { reservationId: string; code: string }[]
    } }>(`${this.apiUrl}/Wristband/Codes`, { ticketIds, reservationIds })
}
```

`vueapp/src/views/Admin/RedeemTickets.vue`: line 152's ticket gate is untouched. A new block
on the season pass card (after the "Today" reservations list at lines 319-332, before the
divider) offers the band chip or Link button once there is a checked-in admission:

```html
<div v-if="branding.wristbandsEnabled && todaysCheckedInReservationId"
     class="d-flex align-center ga-2 mt-3">
    <v-chip v-if="passBandCode" size="small" color="indigo" prepend-icon="mdi-watch" closable
        @click:close="unlinkPassBand">
        Band {{ passBandCode }}
    </v-chip>
    <v-tooltip v-else text="Scan or type a band to link it to this admission" location="top">
        <template #activator="{ props: tip }">
            <v-btn v-bind="tip" size="small" variant="tonal" prepend-icon="mdi-watch"
                @click="openLinkBandForPass">Link band</v-btn>
        </template>
    </v-tooltip>
</div>
```

The existing link dialog (lines 391-413) gains a `bandDialogMode` flag (`'ticket' |
'season_pass'`) instead of being duplicated, keeps its top-right X close button, and its
subtitle names the pass holder in pass mode. Script additions:

```typescript
const bandDialogMode = ref<'ticket' | 'season_pass'>('ticket')
const passBandCode = ref<string | null>(null)

// The one admission a staff member is looking at right now, if any is checked in today.
// todaysReservations includes the no-event walk-up row per section 6.5.
const todaysCheckedInReservationId = computed(() =>
    pass.value?.todaysReservations.find(r => r.status === 'checked_in')?.id ?? null)

async function loadPassBand() {
    passBandCode.value = null
    if (!branding.wristbandsEnabled) return
    const reservationId = todaysCheckedInReservationId.value
    if (!reservationId) return
    try {
        const r = await wristbands.codes([], [reservationId])
        passBandCode.value = r.data.data.reservations.find(x => x.reservationId === reservationId)?.code ?? null
    } catch { /* band chip is decoration on this card; the pass itself already loaded */ }
}

function openLinkBandForPass() {
    bandDialogMode.value = 'season_pass'
    bandTarget.value = null
    bandCodeInput.value = ''
    bandDialogError.value = ''
    bandDialogOpen.value = true
}

async function saveBandLink() {
    const code = bandCodeInput.value.trim()
    if (!code) { bandDialogError.value = 'Scan or type the band code first.'; return }
    if (bandDialogMode.value === 'ticket' && !bandTarget.value) return
    if (bandDialogMode.value === 'season_pass' && !todaysCheckedInReservationId.value) return

    bandDialogBusy.value = true
    bandDialogError.value = ''
    try {
        const target: WristbandLinkTarget = bandDialogMode.value === 'season_pass'
            ? { seasonPassReservationId: todaysCheckedInReservationId.value! }
            : { ticketId: bandTarget.value!.purchaseId }
        await wristbands.link(target, code)
        bandDialogOpen.value = false
        flash(`Band ${code} linked.`, 'success')
        if (bandDialogMode.value === 'season_pass') await loadPassBand()
        else await loadBands()
    } catch (err: any) {
        bandDialogError.value = err.response?.data?.error || (bandDialogMode.value === 'season_pass'
            ? "Couldn't link the band to this pass admission. Scan the band again or pick a different band."
            : 'Could not link the band. Please try again.')
    } finally { bandDialogBusy.value = false }
}

async function unlinkPassBand() {
    const reservationId = todaysCheckedInReservationId.value
    if (!reservationId) return
    try {
        await wristbands.unlink({ seasonPassReservationId: reservationId })
        flash('Band unlinked.', 'success')
        await loadPassBand()
    } catch (err: any) {
        flash(err.response?.data?.error || "Couldn't unlink the band from this pass admission.", 'error')
    }
}
```

`loadBands()` is updated for the new `codes()` response shape (reads `r.data.data.tickets`).
`tryLoadPass` gains one line, `await loadPassBand()`, after the pass loads. `loadPassBand`'s
empty catch is the same decoration-not-critical-path exception `loadBands()` already uses:
the pass card itself already loaded; a failed band-code fetch just means the chip does not
show yet.

**Silent-scan flow needs no new frontend code.** `lookupBand()` calls `wristbands.resolve(code)`
then `loadOrder(redemptionToken)`; `loadOrder` tries `orderLookup(token)` and on 404 falls
back to `tryLoadPass(token)`. Because `ResolveCode`'s season-pass branch returns the pass
purchase's `redemption_token`, a pass-linked band scan flows through that existing chain:
`orderLookup` 404s, the fallback fires, the pass card renders with `passBandCode` populated.

## 8. Frontend: scanner, settings, rider flows

### 8.1 `vueapp/src/views/Admin/RedeemTickets.vue` scanner deltas

Everything above the divider at line 334 is unchanged: registration alert (310-314), season
window alert (315-317), and the `todaysReservations` list (319-332), which renders a Mode B
no-event check-in ("Walk-up admission", per decision 4) with zero template changes.

**(a) Replace the dead-end alert at lines 336-338** with a mode-aware block:

```html
<!-- Zero events today. Mode B: walk-up admission with no calendar event. Mode A: dead end,
     but now an informative one instead of a bare "can't redeem" message. -->
<template v-if="pass.todaysEvents.length === 0">
    <v-alert v-if="branding.seasonPassAdmissionTypeId === 1" type="info" variant="tonal" density="compact">
        No event is running today. This track requires event sign-up, so passes can only be
        checked in for a scheduled event.
    </v-alert>
    <template v-else-if="walkUpAlreadyCheckedIn">
        <div class="d-flex align-center ga-2">
            <v-icon size="18" color="success">mdi-check-circle</v-icon>
            <span class="text-body-2">Already admitted today</span>
            <v-chip size="x-small" color="success" variant="tonal">
                {{ walkUpAlreadyCheckedIn.checkedInAtUtc
                    ? formatInTenant(walkUpAlreadyCheckedIn.checkedInAtUtc) : 'Checked in' }}
            </v-chip>
        </div>
    </template>
    <template v-else-if="pass.walkUpEligibleToday">
        <div class="text-body-2 text-medium-emphasis mb-2">
            <v-icon size="16" class="mr-1">mdi-information-outline</v-icon>
            No event today: walk-up admission
        </div>
        <div class="d-flex align-center ga-2">
            <v-spacer></v-spacer>
            <span v-if="passAdmitBlock" class="text-caption text-medium-emphasis">{{ passAdmitBlock }}</span>
            <v-btn color="success" :loading="admitting" :disabled="!!passAdmitBlock" @click="admitPass">
                {{ pass.productKind === 'credits'
                    ? `Admit - uses 1 ride credit (${pass.creditsRemaining ?? 0} left)`
                    : 'Admit' }}
            </v-btn>
        </div>
    </template>
    <v-alert v-else type="warning" variant="tonal" density="compact">
        This pass isn't eligible for walk-up admission today.
    </v-alert>
</template>
<template v-else>
    <!-- unchanged event-list branch, see (b) -->
</template>
```

(The Admit label reuses the existing button wording at lines 353-355. In the repo that line
joins `Admit` to the credit clause with an em dash character; the snippet above shows a
plain hyphen per this document's prose rule. Keep the file's existing separator; do not
introduce a second, differently styled Admit label in the same card.)

```ts
// Mode B, zero-event days: has this pass already been walked in today with no event attached?
const walkUpAlreadyCheckedIn = computed(() =>
    pass.value?.todaysReservations.find(r => r.eventId === null && r.status === 'checked_in') ?? null)
```

`passAdmitBlock` (lines 703-711) gains one line so the walk-up Admit button also respects it:

```ts
if (pass.value.todaysEvents.length === 0 && walkUpAlreadyCheckedIn.value) return 'Already admitted today.'
```

**(b) Mode A with events running: keep the event list, add a sign-up hint chip.** The
`v-else` branch at 339-358 is unchanged in structure; one informational chip is added so
staff see before clicking that the selected event has no reservation on file (the server
still enforces the rule):

```html
<v-chip v-if="branding.seasonPassAdmissionTypeId === 1 && !selectedEventReserved"
    size="x-small" color="warning" variant="tonal" prepend-icon="mdi-calendar-alert">
    Sign-up required
</v-chip>
```

```ts
const selectedEventReserved = computed(() =>
    pass.value?.todaysReservations.some(r => r.eventId === passEventId.value) ?? false)
```

**(c) `admitPass()` (lines 713-733).** The only deltas are the guard loosening and the
nullable eventId; the existing fallback string at line 729 is kept verbatim and is the only
fallback in this function:

```ts
async function admitPass() {
    if (!pass.value || !passToken.value) return
    if (pass.value.todaysEvents.length > 0 && !passEventId.value) return   // event mode still needs a selection
    admitting.value = true
    try {
        const r = await seasonPasses.redeemAtGate(passToken.value, passEventId.value)   // eventId: string | null
        const data = (r.data as any).data
        if (data.alreadyAdmitted) {
            flash(`Already admitted today${data.checkedInAtUtc ? ' at ' + formatInTenant(data.checkedInAtUtc) : ''}.`, 'warning')
        } else if (pass.value.productKind === 'credits') {
            const left = data.creditsRemaining ?? 0
            flash(`Admitted - ${left} ${left === 1 ? 'ride' : 'rides'} left on this pass.`, 'success')
        } else {
            flash('Admitted.', 'success')
        }
        await tryLoadPass(passToken.value)
    } catch (err: any) {
        flash(err.response?.data?.error || 'Couldn’t admit this pass. Check the connection and try again.', 'error')
    } finally {
        admitting.value = false
    }
}
```

`vueapp/src/services/SeasonPassService.ts` changes: `redeemAtGate(token, eventId)` (lines
241-243) widens its second parameter to `string | null`; `PassReservation.eventId`,
`eventStartsAtUtc`, `eventEndsAtUtc` widen to `string | null`; `PassLookup` gains
`admissionTypeId: number` and `walkUpEligibleToday: boolean`.

### 8.2 `vueapp/src/views/Admin/Settings/Features.vue`: admission mode card

This is a two-option mode, not a boolean, so it cannot use the generic `v-switch` +
`toggle()` path (lines 408-424). A `v-select` branch is added to the `#append` slot inside
the existing `v-for` (lines 11-40), keyed off `f.key`:

```html
<v-select v-else-if="f.key === 'seasonPassAdmissionType'"
    :model-value="branding.seasonPassAdmissionTypeId"
    :items="[
        { title: 'Walk-up: scan and ride', value: 2 },
        { title: 'Event sign-up required', value: 1 },
    ]"
    density="compact" hide-details variant="outlined" style="min-width: 240px"
    :loading="savingKey === 'seasonPassAdmissionType'"
    :disabled="savingKey !== null && savingKey !== 'seasonPassAdmissionType'"
    @update:model-value="(v: number) => applySeasonPassAdmissionType(v)"></v-select>
```

Entry in the `features` array (lines 125-326), placed next to `requireReservationForPasses`.
`enabled` is unused for this key (the select owns rendering) but is populated so
`visibleFeatures`'s filter keeps working; the key is not added to `PLATFORM_FEATURE_KEYS`
(lines 332-335), matching the other tenant-controlled policy settings:

```ts
{
    key: 'seasonPassAdmissionType',
    title: 'Season pass admission',
    description: 'Walk-up: any pass holder can scan in on any operating day, event or not. ' +
        'Event sign-up required: pass holders must reserve a spot at a scheduled event before they can be checked in.',
    icon: 'mdi-qrcode-scan',
    enabled: branding.seasonPassAdmissionTypeId === 2,
    apply: async () => { /* handled by applySeasonPassAdmissionType, not the generic toggle() */ },
},
```

Save handler. No manual revert is needed: the select's `:model-value` binds one-way from the
branding store, so a rejected save snaps back on the next render, identical to how a failed
`v-switch` toggle behaves today. The fallback message names the setting (ruling edit; the
three legacy `'Save failed.'` call sites in this file stay untouched):

```ts
async function applySeasonPassAdmissionType(next: number) {
    if (savingKey.value) return
    savingKey.value = 'seasonPassAdmissionType'
    try {
        await tenantService.updateSettings({
            timezone: branding.timezone,
            requireReservationForPasses: branding.requireReservationForPasses,
            requireEmergencyContact: branding.requireEmergencyContact,
            allowEventSubscriptions: branding.allowEventSubscriptions,
            requireIdAtCheckin: branding.requireIdAtCheckin,
            seasonPassAdmissionTypeId: next,
        })
        await loadBranding()
        flash(`Season pass admission set to ${next === 1 ? 'event sign-up required' : 'walk-up'}.`, 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || "Couldn't save the season pass admission setting.", 'error')
    } finally {
        savingKey.value = null
    }
}
```

### 8.3 Plumbing

`vueapp/src/services/TenantService.ts` (line 14): add `seasonPassAdmissionTypeId: number`
to `updateSettings`'s request type. Every existing `updateSettings` caller (the four call
sites in `Features.vue` at 269-276, 284-292, 300-308, 316-324) must pass
`seasonPassAdmissionTypeId: branding.seasonPassAdmissionTypeId` so the field round-trips
when a different setting is being edited, mirroring how those sites already pass through
every field they are not editing.

`vueapp/src/stores/branding.ts`, three insertions next to `requireReservationForPasses`:

```ts
// interface (near line 30)
seasonPassAdmissionTypeId: number
// default state (near line 144)
seasonPassAdmissionTypeId: 2,
// mapping from the branding response (near line 296)
branding.seasonPassAdmissionTypeId = data.seasonPassAdmissionTypeId ?? 2
```

Default 2 at both the store default and the `??` fallback matches the tenant-column default,
so rollout skew resolves to today's behavior.

### 8.4 Rider-facing: `vueapp/src/views/User/SeasonPasses.vue`

This is the dedicated season-pass management view (`/User/SeasonPasses`, nav label "Season
Passes"; the separate `/User/MyPasses` view is a broader purchases list and is not
modified). It already imports the branding store (line 125). Verified: `SeasonPassService.reserve()`
exists (lines 231-233) but has zero call sites; the Reserve UI below is NEW, not an edit.

Per-pass mode-aware copy and a Reserve trigger, added after the status chip (around lines
36-37):

```html
<div v-if="p.status === 'paid' && p.registrationComplete" class="mt-3">
    <v-alert v-if="branding.seasonPassAdmissionTypeId === 1" type="info" variant="tonal" density="compact">
        Sign up for an event to use your pass. This track requires a reservation before you ride -
        walking up without one will be turned away at the gate.
    </v-alert>
    <p v-else class="text-caption text-medium-emphasis">
        Walk-ups welcome: just show this QR at the gate on any operating day.
        Riding a capacity-limited event? Reserve your spot below.
    </p>
    <v-btn size="small" variant="tonal" color="primary" class="mt-2" prepend-icon="mdi-calendar-plus"
        @click="openReserve(p)">
        Reserve for an event
    </v-btn>
</div>
```

Reserve stays functional in both modes (decision 10); only the copy changes.

Dialog, a sibling of the existing `registerOpen` dialog (after line 112), same shape
(target-gated card, top-right X close, `v-card-actions` row). Event picker is a `v-select`
(rider lists can span the whole validity window, unlike the gate scanner's 1-3 events):

```html
<v-dialog v-model="reserveOpen" max-width="520">
    <v-card v-if="reserveTarget">
        <v-card-title class="d-flex align-center">
            <span>Reserve for an event</span>
            <v-spacer></v-spacer>
            <v-btn icon="mdi-close" variant="text" size="small" @click="reserveOpen = false"></v-btn>
        </v-card-title>
        <v-card-text>
            <p class="text-body-2 text-medium-emphasis mb-3">
                {{ reserveTarget.productName }} - pick an upcoming event to reserve your spot.
            </p>
            <v-progress-circular v-if="reserveLoading" indeterminate color="primary" class="mb-2"></v-progress-circular>
            <v-alert v-else-if="reserveEvents.length === 0" type="info" variant="tonal" density="compact">
                No upcoming events fall within this pass's validity window.
            </v-alert>
            <v-select v-else v-model="reserveEventId" :items="reserveEvents"
                item-title="title" item-value="id" label="Event" density="compact" variant="outlined"
                hide-details>
                <template #item="{ props, item }">
                    <v-list-item v-bind="props" :subtitle="formatInTenant(item.raw.startsAtUtc)"></v-list-item>
                </template>
            </v-select>
            <div v-if="reserveError" class="text-error text-body-2 mt-3">{{ reserveError }}</div>
        </v-card-text>
        <v-card-actions>
            <v-spacer></v-spacer>
            <v-btn :disabled="reserveSaving" @click="reserveOpen = false">Cancel</v-btn>
            <v-btn color="primary" :loading="reserveSaving" :disabled="reserveEvents.length === 0 || !reserveEventId"
                @click="submitReserve">
                Reserve
            </v-btn>
        </v-card-actions>
    </v-card>
</v-dialog>
```

Script. The event filter is a client-side mirror of the server's own `Reserve` validations
(`SeasonPassController.cs` lines 867-957): scheduled status, not yet ended, event start date
inside the pass validity window, and the `days_of_week` restriction. It does NOT use
`EventDto.eligiblePasses`, which is never populated (section 2); no backend population is
added, and no `MySeasonPass.productId` field is needed. All fields used below already exist
on `EventDto` (`status`, `startsAtUtc`, `endsAtUtc`) and `MySeasonPass` (`validFromDate`,
`validToDate`, `productKind`, `validDaysOfWeek`):

```ts
const reserveOpen = ref(false)
const reserveTarget = ref<MySeasonPass | null>(null)
const reserveEvents = ref<EventDto[]>([])
const reserveEventId = ref<string | null>(null)
const reserveLoading = ref(false)
const reserveSaving = ref(false)
const reserveError = ref('')   // inline, in-dialog error - mirrors formError (line 134) on the registerOpen dialog

async function openReserve(p: MySeasonPass) {
    reserveTarget.value = p
    reserveEventId.value = null
    reserveError.value = ''
    reserveOpen.value = true
    reserveLoading.value = true
    try {
        const fromUtc = dayjs().startOf('day').toISOString()
        const toUtc = dayjs(p.validToDate).endOf('day').toISOString()
        const r = await eventService.list(fromUtc, toUtc)
        // Client-side mirror of the server's Reserve validations (SeasonPassController.cs
        // 867-957). EventResponse.EligiblePasses is dead (never populated; its backing table
        // was dropped by Script0118_RemoveDayPass.sql line 57), so it is NOT used here.
        const now = dayjs()
        const from = p.validFromDate.slice(0, 10)
        const to = p.validToDate.slice(0, 10)
        reserveEvents.value = r.data.data.filter((e: EventDto) => {
            if (e.status !== 'scheduled') return false
            if (!dayjs.utc(e.endsAtUtc).isAfter(now)) return false
            const start = dayjs.utc(e.startsAtUtc).tz(branding.timezone || 'UTC')
            const day = start.format('YYYY-MM-DD')
            if (day < from || day > to) return false
            if (p.productKind === 'days_of_week' && p.validDaysOfWeek?.length)
                return p.validDaysOfWeek.includes(start.day())
            return true
        })
    } catch (err: any) {
        // Load failure happens before the rider sees any options; a snackbar plus an empty
        // reserveEvents (which renders the in-dialog empty-state alert) is enough here.
        flash(err.response?.data?.error || 'Could not load events to reserve. Check your connection and try again.', 'error')
        reserveEvents.value = []
    } finally {
        reserveLoading.value = false
    }
}

async function submitReserve() {
    if (!reserveTarget.value || !reserveEventId.value) return
    reserveError.value = ''
    reserveSaving.value = true
    try {
        await service.reserve(reserveTarget.value.id, reserveEventId.value)
        reserveOpen.value = false
        flash('Reserved. Show your pass QR at the gate for that event.', 'success')
    } catch (err: any) {
        // Submit failure keeps the dialog open for a retry: inline error, not a snackbar
        // that could go unnoticed behind the still-open dialog.
        reserveError.value = err.response?.data?.error || 'Could not reserve that event. It may be full - try another.'
    } finally {
        reserveSaving.value = false
    }
}
```

Two supporting additions to this file: `eventService = new EventService()` alongside the
existing `service = new SeasonPassService()` (line 127), importing `EventService`/`EventDto`
from `@/services/EventService`; and a `formatInTenant` helper copied verbatim from
`RedeemTickets.vue` lines 811-813 (`dayjs.utc(utc).tz(branding.timezone || 'UTC').format('YYYY-MM-DD HH:mm')`).
No dayjs plugin setup is needed here: `vueapp/src/main.ts` lines 12-13 already extend dayjs
with the utc/timezone plugins at app bootstrap, and the module instance is shared.

### 8.5 Staff-workflow narrative

**Highland walk-up day, no event on the calendar (Mode B).** Staff scan the pass QR;
`loadOrder` 404s, `tryLoadPass` succeeds. `todaysEvents` is empty; with
`branding.seasonPassAdmissionTypeId === 2` and `walkUpEligibleToday` true, staff see "No
event today: walk-up admission" and an Admit button. One tap burns a credit (or just records
the visit) and writes a check-in row with no event attached. A second scan the same day
finds `walkUpAlreadyCheckedIn` and shows the green "Already admitted today" state instead of
a second Admit button.

**Race-day Mode A track.** A pass holder who never reserved today's race walks up. The event
list renders as today; the "Sign-up required" chip shows because no matching row exists in
`todaysReservations`. If staff click Admit anyway, the server's 400 surfaces through the
existing `flash(err.response?.data?.error || ...)` path with the sign-up-required message. A
rider who did reserve shows in `todaysReservations` and Admit flips the reservation exactly
as today.

## 9. Credit burn and double-scan semantics

`product.Kind` is the existing property on `SeasonPassProduct`
(`Services/Repositories/Data/PaymentData/SeasonPass.cs` line 12); it is an existing
identifier and no new `Kind`-named member is introduced anywhere in this design.

| # | Scenario | Credit burned? | Result |
|---|---|---|---|
| a | Mode B, event running today, first scan | 1, if `product.Kind == "credits"` | `AlreadyAdmitted: false`, new `checked_in` row via `CreateGateCheckIn` |
| b | Mode B, same event, repeat scan | none | `AlreadyAdmitted: true`, existing row returned (lines 1189-1199) |
| c | Mode B, zero events today, first scan | 1, if `product.Kind == "credits"` | `AlreadyAdmitted: false`, new row with `event_id NULL`, `check_in_date = today` via `CreateWalkUpGateCheckIn` |
| d | Mode B, same no-event day, repeat scan | none | `AlreadyAdmitted: true`; caught by the `GetWalkUpCheckIn` pre-check, and backstopped by the statement's own `NOT EXISTS` guard if a race gets past it (see the resolved note below) |
| e | Mode B, staff scans a second, different event running the same day | 1 more (separate `(pass, event)` row) | Existing semantics, unchanged; each event is its own admission and its own burn |
| f | Mode B, no-event walk-up admission, then an event gets added/rescheduled onto today and staff scan them into it | 1 for the no-event row, 1 more for the event row; both admit | Accepted edge case. Mitigated operationally: the scanner only offers the no-event path when `todaysEvents` is empty, so this requires an event to appear after the walk-up already happened |
| g | Mode A, reserved ahead, then scanned at the gate | none at the gate; `Reserve` already burned it (lines 928-938) | `AlreadyAdmitted: false`, reservation flips `reserved` to `checked_in` (lines 1200-1214) |
| h | Mode A, no reservation, rider tries to walk up | none, nothing written | Rejected with the sign-up-required message, at the top-level null-EventId check or at the under-lock live-reservation check |
| i | Zero credits remaining, any burn path returns null | none (guarded, 0 rows) | Existing rejection: "This pass has no ride credits left. If that's a mistake, credits can be adjusted from the customer's admin page." |

**CORRECTION, verified by execution against a migrated copy of `ridepass_dev` (2026-07-24):**
the partial unique index stops a duplicate ROW but NOT a duplicate credit BURN. In both the new
`CreateWalkUpGateCheckIn` and the already-shipped `CreateGateCheckIn`, the `burn` CTE commits
even when `ON CONFLICT` filters the `INSERT` out, so a second call against an already-admitted
anchor decrements `credits_remaining` and still returns null (measured: 3 credits to 2 on the
first scan, 2 to 1 on a repeat scan that returned zero rows). The per-pass advisory lock plus
the caller's pre-check are therefore the ONLY thing preventing a double burn, not a second line
of defense. This is a pre-existing property of the shipped event path, not something the
walk-up work introduced, and it is unreachable while callers honor the contract.

**RESOLVED (2026-07-25).** Both statements now carry a `NOT EXISTS` guard on the burn, so the
credit is only decremented when there is no live reservation for that anchor. Verified by
re-running the measurement above: the first scan still goes 3 to 2, and repeat scans now return
zero rows with credits held at 2 instead of draining to 1 and then 0. The revive-a-cancelled-row
path still burns and admits correctly (2 to 1), and the zero-credit guard still refuses without
going negative. `RedeemPassAtGate` also re-reads on a null result before reporting "no ride
credits left", so a raced repeat is reported as already admitted rather than sending a rider with
a full pass to the office.

**Invariant:** at most one credit is burned per admission (one `(pass, event)` or
`(pass, check-in date)` anchor), and every burn + write for a given pass is serialized by
the single per-pass advisory lock (`season-pass-redeem:{pass.Id}`), so two concurrent scans
of the same pass can never both pass the pre-check and both burn.

## 10. Reporting and read-model impact

### 10.1 `v_recent_sales`: NO new branch

`Script0080_RecentSalesView.sql` admits purchase-shaped tables: `tenant_id`, a sale
`status`, `amount_cents`, a Stripe PaymentIntent id. `season_pass_reservation` fails every
leg: no tenant_id, no amount_cents, no PaymentIntent, and its status enum describes
admission-workflow state. A walk-up admission records the consumption of a pass that was
already sold; the sale is the `season_pass_purchase` row, already the `'season_pass'`
branch of the view (Script0080 lines 78-90). `event_wristband` is likewise not
purchase-shaped and this design's additions do not change that. **Script0080 is not touched
and no UNION ALL branch is added.** Stated explicitly so the silence is not read as an
oversight.

### 10.2 `Services/Repositories/ReportsRepository.cs`: `RiderSeasonPassBranch` (lines 312-330)

The current branch inner-joins `event`; with `event_id` nullable, walk-up rows would
silently vanish from rider reports. Rewrite:

```csharp
private const string RiderSeasonPassBranch = @"
        SELECT spr.id, 'season_pass',
               e.id, COALESCE(e.title, 'Walk-up'),
               COALESCE(e.starts_at, spr.check_in_date::timestamp AT TIME ZONE 'UTC'),
               COALESCE(NULLIF(TRIM(CONCAT_WS(' ', spp.holder_first_name, spp.holder_last_name)), ''),
                        spp.purchaser_name, '(unknown)'),
               spp.purchaser_email,
               spp.purchaser_user_id,
               sp.name,
               (spr.checked_in_at IS NOT NULL),
               spr.checked_in_at,
               NULL::text,
               false
        FROM season_pass_reservation spr
        JOIN season_pass_purchase spp ON spp.id = spr.season_pass_purchase_id
        JOIN season_pass_product sp ON sp.id = spp.product_id
        LEFT JOIN event e ON e.id = spr.event_id
        WHERE spp.tenant_id = @tenantId
          AND spr.status <> 'cancelled'
          AND ({EVENT_WINDOW} OR (spr.event_id IS NULL AND spr.check_in_date IS NOT NULL AND {WALKUP_WINDOW}))";
```

`{EVENT_WINDOW}` keeps its two existing forms (`GetRidersByRange` line 367,
`GetRiderRegistrations` line 384); both reference `e.*`, which is NULL for walk-up rows and
correctly falsifies that disjunct, so the OR is required. `{WALKUP_WINDOW}` is the per-caller
date analog:

- `GetRidersByRange`: `"spr.check_in_date >= @fromUtc::date AND spr.check_in_date < @toUtc::date"`
- `GetRiderRegistrations`: `"spr.check_in_date >= (now() - INTERVAL '365 days')::date"`

Tenant scope for the walk-up disjunct is the branch's own `spp.tenant_id = @tenantId`, which
does not depend on the now-optional event join.

Two documented caveats, accepted deliberately:

- `@fromUtc::date` casts using the DB session timezone, not the tenant's IANA zone, while
  `check_in_date` is tenant-local; at a report window's edge a walk-up row can misclassify by
  one day. Reporting-only imprecision; fixing it would mean threading a timezone parameter
  into both methods. Accepted as-is.
- `spr.check_in_date::timestamp AT TIME ZONE 'UTC'` is a type cast producing a local-midnight
  placeholder so the COALESCE type-checks against `e.starts_at` (timestamptz), not a real
  timezone conversion. Acceptable because season-pass rows render this value as a date only,
  never a clock time.

`Services/Repositories/Data/ReportData/ReportTypes.cs` line 180: `RiderReportRow.EventId`
becomes `Guid?`. `EventTitle` stays non-nullable (the COALESCE guarantees a value).

### 10.3 `LookupCheckInByToken` (`ReportsRepository.cs` line 453) same gap

The `regsSql` season-pass arm (lines 524-544) has the same inner join, feeding
`CheckInRegistration` (`ReportTypes.cs` lines 135-150) whose `EventId` is non-nullable and
whose `EventStartsAtUtc`/`EventEndsAtUtc` get unconditional `DateTime.SpecifyKind` calls at
lines 563-566. Two required fixes:

1. **DTO**: `CheckInRegistration.EventId` (line 139) becomes `Guid?`.
   `EventStartsAtUtc`/`EventEndsAtUtc` stay non-nullable `DateTime` (the SpecifyKind calls
   keep compiling unmodified) on the condition SQL never hands them NULL, which fix 2
   guarantees.
2. **SQL**: rewritten season-pass arm:

```sql
SELECT
    spr.id AS Id,
    'season_pass' AS Source,
    e.id AS EventId,
    COALESCE(e.title, 'Walk-up') AS EventTitle,
    COALESCE(e.starts_at, spr.check_in_date::timestamp AT TIME ZONE 'UTC') AS EventStartsAtUtc,
    COALESCE(e.ends_at, spr.check_in_date::timestamp AT TIME ZONE 'UTC') AS EventEndsAtUtc,
    sp.name AS ItemName,
    spr.status AS Status,
    (spr.checked_in_at IS NOT NULL) AS CheckedIn,
    spr.checked_in_at AS CheckedInAtUtc,
    NULL::uuid AS RedemptionToken
FROM season_pass_reservation spr
JOIN season_pass_purchase spp ON spp.id = spr.season_pass_purchase_id
JOIN season_pass_product sp ON sp.id = spp.product_id
LEFT JOIN event e ON e.id = spr.event_id
WHERE spp.tenant_id = @tenantId
  AND spp.purchaser_user_id = @userId
  AND spr.status <> 'cancelled'
  AND (
        (e.starts_at >= @fromUtc AND e.starts_at < @toUtc)
        OR (spr.event_id IS NULL AND spr.check_in_date IS NOT NULL
            AND spr.check_in_date >= @fromUtc::date AND spr.check_in_date < @toUtc::date)
      )
```

The today/future split at lines 568-569 works unmodified against the placeholder.

### 10.4 Other surfaces

- `EventRiders` (`ReportsRepository.cs` lines 249-276) filters `spr.event_id = @eventId`; a
  NULL event_id never matches, so walk-up rows are correctly excluded. No change.
- `DailyEvents` (`GetEventsInRange`, lines 577-620) drives from `FROM event e` and joins the
  reservation aggregate on `event_id`; walk-up rows never appear. No change.
- **`vueapp/src/views/Admin/RiderReport.vue` is where staff see walk-up admission counts.**
  With 10.2, walk-up rows render "Walk-up" as `eventTitle` (lines 72 and 121) with no
  template change. Two small script edits: (1) the grouping key at line 202 uses `r.eventId`,
  which is now null for every walk-up row; key by `` r.eventId ?? `walkup:${r.eventStartsAtUtc}` ``
  so distinct walk-up days stay distinct. (2) Format `r.eventStartsAtUtc` with the existing
  date-only `formatDay` for these rows, matching the placeholder caveat.
- The scanner's own pass card shows today's walk-up state at redeem time (section 8.1).

## 11. Rollout plan and edge cases

Single release. Run `Script0235`, `Script0236`, `Script0237` against the running database,
then deploy backend + frontend together. Between migrate and deploy the currently deployed
app keeps working unmodified:

- It never reads or writes `tenant.season_pass_admission_type_id`; the column sits at
  DEFAULT 2.
- It only calls `RedeemPassAtGate` with `EventId` populated, so every reservation row it
  inserts has `event_id NOT NULL`, satisfying the new CHECK and never touching the partial
  index.
- It only calls `WristbandController.Link` with a `TicketId`, so every wristband row it
  inserts satisfies the exactly-one-anchor CHECK.

No backfill is required and nothing is tightened or dropped, so no expand-then-contract
staging applies to this release. Any future narrowing (e.g. `check_in_date NOT NULL` on
walk-up rows) would need its own staged sequence: deploy code that always populates, verify
zero NULLs, then tighten in a later script. Not attempted here.

| Edge case | Resolution |
|---|---|
| Tenant flips Mode B to Mode A with existing walk-up history | Historical `event_id IS NULL` rows remain valid history (they satisfy every constraint regardless of mode); reports read history, not the live flag. Future walk-up attempts are rejected at `RedeemPassAtGate`. |
| Tenant flips Mode A to Mode B | Existing reservations keep working exactly as today; walk-up becomes available going forward with no data change. |
| DST / tenant-local `check_in_date` | Computed server-side exactly as lines 1149-1150 already do (tenant IANA zone, then `.Date`). A scan at 23:55 local and another at 00:05 the next local day are two dates and both succeed, matching the per-day admission model exactly as a real event-day boundary would. |
| Pass valid-window boundaries on a no-event day | Checked against `todayLocal` with the identical two comparisons the event branch uses (lines 1156-1159). |
| Mode B tenant has an event running AND a walk-up scan the same day | The scanner prefers the event path whenever `todaysEvents` is non-empty; the walk-up panel renders only with zero events. No dual-prompt state. |
| Refund of a pass after a walk-up check-in | A status refund leaves every reservation row intact as history; the existing `pass.Status != "paid"` check (line 1137) blocks future admissions. A hard delete of the purchase row (out-of-band only) cascades per Script0035 line 77's `ON DELETE CASCADE`; existing table behavior, not introduced here. |
| Band linked to a no-event admission, scanned the next day | `ResolveCode` matches `valid_on_date = today` only, so yesterday's walk-up band stops resolving, consistent with the per-event scoping philosophy. |

## 12. Master ordered task list

**T1** NEW `RidePass.Migrator/Scripts/Script0235_SeasonPassAdmissionType.sql` (section 4.1).

**T2** NEW `RidePass.Migrator/Scripts/Script0236_SeasonPassWalkUpAdmission.sql` (section 5.1).

**T3** NEW `RidePass.Migrator/Scripts/Script0237_SeasonPassWristbands.sql` (section 7.1).

**T4** Entities and enum:
- NEW `Services/Repositories/Data/TenantData/SeasonPassAdmissionType.cs` (section 4.2).
- `Services/Repositories/Data/TenantData/Tenant.cs`: add `SeasonPassAdmissionTypeId` (4.3).
- `Services/Repositories/Data/PaymentData/SeasonPass.cs`: `SeasonPassReservation.EventId`
  to `Guid?`, add `CheckInDate DateTime?`; `SeasonPassReservationWithContext` event fields
  nullable (5.2).
- `Services/Repositories/Data/PaymentData/EventWristband.cs`: widen `EventWristband` and
  `WristbandResolution`, rename `TicketStatus` to `Status` (7.5).
- NEW `Services/Repositories/Data/PaymentData/SeasonPassReservationLinkContext.cs` (7.3).

**T5** `Services/Repositories/TenantRepository.cs` + `Services/Repositories/Interfaces/ITenantRepository.cs`:
`SelectColumns` addition + `UpdateSeasonPassAdmissionType` (4.4).

**T6** `Services/Repositories/SeasonPassRepository.cs` + `Services/Repositories/Interfaces/ISeasonPassRepository.cs`:
`GetWalkUpCheckIn`, `CreateWalkUpGateCheckIn`, changed `ListReservationsForPurchaseOnDate`
signature (6.4), `GetReservationForBandLink` (7.3).

**T7** `Services/Repositories/WristbandRepository.cs` + `Services/Repositories/Interfaces/IWristbandRepository.cs`:
`LinkToReservation`, `UnlinkReservation`, `GetCodesForReservations`, `ResolveCode(Guid, string, DateOnly)`
UNION rewrite (7.5).

**T8** DTOs under `webapi/Controllers/API/Data/`:
- `SeasonPass/SeasonPassGateRedeemRequest.cs`: `EventId` to `Guid?`, drop `[Required]` (6.2).
- `Tenant/UpdateTenantRequest.cs`: add `SeasonPassAdmissionTypeId` with `[Range(1,2)]` (4.5).
- `Tenant/GetBrandingResponse.cs`: add `SeasonPassAdmissionTypeId` (4.5).
- `Redemption/WristbandDtos.cs`: widen the three request classes in place (7.2).

**T9** `webapi/Controllers/SeasonPassController.cs`: replace `RedeemPassAtGate` body (6.3);
`LookupPassByToken` additions and the changed `ListReservationsForPurchaseOnDate` call with
the `"Walk-up admission"` title mapping (6.5).

**T10** `webapi/Controllers/TenantController.cs`: one new call in `UpdateTenantSettings`,
one new mapped field in `GetBranding` (4.5).

**T11** `webapi/Controllers/WristbandController.cs`: inject `ISeasonPassRepository`, branch
`Link`/`Unlink`, pass `todayLocal` into `ResolveCode`, widen `Resolve` and `Codes` response
shapes (7.4).

**T12** `webapi/Program.cs`: verification only. `ISeasonPassRepository` (line 64),
`ITenantRepository` (line 54), and `IWristbandRepository` (line 67) are already registered;
no new registrations are needed unless a brand-new repository class appears during
implementation.

**T13** `Services/Repositories/ReportsRepository.cs` + `Services/Repositories/Data/ReportData/ReportTypes.cs`:
`RiderSeasonPassBranch` LEFT JOIN + `{WALKUP_WINDOW}` at both call sites (10.2);
`RiderReportRow.EventId` to `Guid?`; `LookupCheckInByToken` `regsSql` season-pass arm
rewrite + `CheckInRegistration.EventId` to `Guid?` (10.3).

**T14** Vue settings plumbing: `vueapp/src/stores/branding.ts` (three insertions, 8.3),
`vueapp/src/services/TenantService.ts` (`updateSettings` type + pass-through at the four
existing call sites, 8.3), `vueapp/src/views/Admin/Settings/Features.vue` (feature entry,
`v-select` append branch, `applySeasonPassAdmissionType`, 8.2).

**T15** `vueapp/src/services/SeasonPassService.ts`: widen `PassReservation` nullables,
`redeemAtGate(token, eventId: string | null)`, add `admissionTypeId`/`walkUpEligibleToday`
to `PassLookup` (8.1).

**T16** `vueapp/src/services/WristbandService.ts`: `WristbandLinkTarget` union, widened
`codes()` (7.6).

**T17** `vueapp/src/views/Admin/RedeemTickets.vue`: replace the dead-end alert with the
mode-aware block, `walkUpAlreadyCheckedIn`/`selectedEventReserved` computeds,
`passAdmitBlock` addition, `admitPass` guard loosening (8.1); pass-card band chip/button,
`bandDialogMode` dialog generalization, `loadPassBand`/`openLinkBandForPass`/`unlinkPassBand`,
`loadBands` response-shape update, one new line in `tryLoadPass` (7.6).

**T18** `vueapp/src/views/User/SeasonPasses.vue`: mode-aware copy + Reserve trigger, the
Reserve dialog, refs and `openReserve`/`submitReserve`, `formatInTenant` helper,
`EventService` import and instance (8.4).

**T19** `vueapp/src/views/Admin/RiderReport.vue`: null-safe grouping key and date-only
formatting for walk-up rows (10.4).

Dependency order: T1-T3 first (independent of each other, numbered consecutively), then
T4-T8 (compile-level prerequisites), then T9-T13, then T14-T19. T12 can run any time.

## 13. Open questions

1. **`RiderReportRow.EventStartsAtUtc` for walk-up rows**: keep the single property carrying
   the local-midnight placeholder, or add a separate `CheckInDate` DTO column? Default: keep
   the single column (avoids widening every `RiderReportRow` caller), accepting the
   documented placeholder caveat.
2. **Splitting `WristbandDtos.cs` into one file per class** to match the newer DTO
   convention: default is to leave it as one file, matching its current state; it is a
   pre-existing convention gap this design widens rather than fixes, and splitting is a
   separable rename.
3. **Scenario f in section 9** (double burn when an event is added onto a day after a
   walk-up admission already happened): accepted as-is with the operational mitigation, or
   should the no-event branch also refuse when any same-day event-anchored check-in exists
   for the pass? Default: accepted as-is; the reverse check adds a query per scan for an
   edge that requires a same-day calendar change.
