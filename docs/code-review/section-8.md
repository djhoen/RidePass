# Section 8: Admin tools — events, catalogs & adjacent admin surfaces

## Scope

Read end-to-end:

- `webapi/Controllers/EventController.cs` — admin CRUD, `Duplicate`, image upload,
  `ValidateWaiverForEvent`.
- `webapi/Controllers/BlackoutController.cs` — admin CRUD only (no read-side
  enforcement exists anywhere; see Critical #1).
- `webapi/Controllers/EventTypeController.cs` — admin CRUD, `Reorder`, `IsInUseByEvents`
  delete guard.
- `webapi/Controllers/PassProductController.cs` — admin endpoints. (Buy path was Section 4.)
- `webapi/Controllers/EventTicketTierController.cs` — admin CRUD, `Reorder`, bundled-coupon
  validator. (Buy path was Section 4.)
- `webapi/Controllers/ExtraController.cs` — admin product + variant CRUD, `ReorderProducts`,
  inventory math, image upload. (Buy path was Section 5.)
- `webapi/Controllers/SeasonPassController.cs` admin endpoints
  (`Products/Admin`, `Products`, `Products/{id}`, `Products/Reorder`, `Products/{id}`/DELETE).
- `webapi/Controllers/RentalController.cs` admin endpoints (product CRUD, reorder,
  per-item CRUD, maintenance windows).
- `webapi/Controllers/WaitlistController.cs` — all endpoints + `Services/Waitlist/WaitlistPromoter.cs`
  and `webapi/Workers/WaitlistExpiryWorker.cs` for the promote / notify path.
- `webapi/Controllers/EventSubscriptionController.cs` — public + token endpoints (no admin endpoints).
- `webapi/Controllers/NewsletterController.cs` — admin subscriber CRUD + import.
- `webapi/Controllers/SurveyController.cs` admin endpoints (CRUD, status, questions/choices,
  invites, results, audience preview).
- `Services/Repositories/EventExtraRepository.UpdateProductSortOrders`,
  `EventTicketTierRepository.UpdateSortOrders`, `SurveyRepository.UpdateQuestionSortOrders` +
  `UpdateChoiceSortOrders` — verified single-statement `UPDATE … FROM unnest(@ids, @orders)` with
  tenant/parent scope predicates.
- `Services/Waitlist/WaitlistPromoter.cs` + `webapi/Workers/WaitlistExpiryWorker.cs`.
- `webapi/Storage/LocalFilesystemImageStorage.cs` (re-walked; see Critical #2 below).
- `webapi/AuthPolicies/TenantPermissions.cs` — verified each admin endpoint's
  policy against the role catalog.

Frontend admin views (read end-to-end):

- `vueapp/src/views/Admin/Events.vue` + `vueapp/src/components/EventDialog.vue` (≈570 lines).
- `vueapp/src/views/Admin/Blackouts.vue`.
- `vueapp/src/views/Admin/Passes.vue`.
- `vueapp/src/views/Admin/SeasonPasses.vue`.
- `vueapp/src/views/Admin/Extras.vue` (≈750 lines including variants editor).
- `vueapp/src/views/Admin/Rentals.vue`.
- `vueapp/src/views/Admin/EventTypes.vue`.
- `vueapp/src/views/Admin/Surveys.vue` + `vueapp/src/views/Admin/SurveyEdit.vue`.
- `vueapp/src/views/Admin/Settings/HomePage.vue` (drag-drop gallery + track graphics).
- `vueapp/src/components/TicketTiersList.vue`.
- `vueapp/src/composables/useDragReorder.ts` — sanity-check.

No `vueapp/src/views/Admin/Calendar.vue` exists (no admin calendar view today — the
read-only `Calendar.vue` for the public site is out of scope here).

Sections 1–7 findings are not re-flagged. Specifically not repeated: the cross-tenant
`IsResolved` checks (Section 1), the webhook / payment-intent flow (Section 2), schema
shape including FK ON-DELETE choices (Section 3), buy-flow money math + variant inventory
race (Section 4 / 5), counter / cancel / refund mechanics (Section 6), and the waiver
edit-in-place + email-verification gaps (Section 7). Where this section touches one of
those areas (e.g. the admin reads inventory the buy flow writes), the existing finding is
referenced inline, not re-flagged.

## Architecture summary

**Catalog write shape.** Every admin write through `CatalogManage` follows the same
template: `_tenantContext.TenantId` is stamped server-side; the request DTO never
carries `tenantId`; existing rows are loaded with `GetById(id, tenantId)` so a leaked id
from another tenant returns null → 404. The repository SQL is `WHERE id = @id AND
tenant_id = @tenantId` on update / delete so even if the precheck were skipped, the
write would no-op cross-tenant. This pattern is followed correctly by `Pass`,
`SeasonPass`, `Rental`, `EventType`, `Blackout`, `Event`, `ExtraProduct`, and
`Waiver` (per Section 7). The two cases that don't fit cleanly are the
`EventTicketTier` (parent-scoped via `eventId`) and the survey question/choice paths
(parent-scoped via `surveyId` / `questionId`) — both checked end-to-end and the
parent-scope predicate is consistent.

**Delete + dependents.** Every catalog delete catches `23503` (PG FK restrict) and
returns "set inactive instead" — `PassProduct`, `SeasonPassProduct`, `ExtraProduct`,
`ExtraVariant`, `RentalProduct`, `RentalItem`, `Event`, `EventTicketTier` (with a
sold-count precheck instead of FK catch). `EventType` precomputes `IsInUseByEvents`
and refuses deletion if any event references it. `Blackout` and `Waiver` (per
Section 7) are the catalogs where hard delete is allowed unconditionally — blackouts
because they have no dependent rows, waivers because the in-place-edit pattern
(Section 7 Critical) means there's nothing pointing to them that you'd preserve.

**Drag-drop reorder.** `vueapp/src/composables/useDragReorder.ts` is the shared
client logic — bind `visibleRows` to `vuedraggable`, on `@end` interleave hidden
rows back into the canonical `rows`, renumber 10/20/30…, POST all ids+orders.
Server side every reorder uses a single SQL `UPDATE … FROM (SELECT unnest(@ids::uuid[]),
unnest(@orders::int[])) WHERE id = data.id AND tenant_id = @tenantId` — atomic,
tenant-scoped, last-writer-wins. `Pass`, `SeasonPass`, `Rental`, `EventType`,
`Extra`, `EventTicketTier`, survey questions, survey choices, gallery, track
graphics all follow this pattern. Two views (`Extras.vue` lines 534-556 and
`Admin/Settings/HomePage.vue` lines 351-360 for gallery) reimplement the same logic
in-line instead of using the composable — drift risk, see Low #16.

**Event editor.** `EventController.Create` + `Update` accept a single
`UpsertEventRequest` covering 17 fields. `Update` clobbers every editable column
including `StartsAtUtc`, `EndsAtUtc`, `Capacity`, `Status` — there is no
"can't change date once tickets are sold" guard. `Duplicate` (`POST /api/Event/{id}/Duplicate`)
shifts start + end forward 7 days, resets status to `scheduled`, carries over
`EventTypeId`, pass + extras eligibility, waiver attachments, image URL, and the
required-waiver flags. It does NOT carry over `EventTicketTier` rows — the
duplicated event has no tickets/race-classes until the admin re-creates them. The
event-image upload accepts PNG / JPEG / WebP, 5 MB cap, content-type allowlist
(but see Critical #2 on the upload storage path).

**Blackout enforcement.** `BlackoutController` CRUD is correct; the schema stores
`starts_at` / `ends_at` UTC. **But** no buy / reservation / availability code in
the repo consults the `blackout` table anywhere (grep of
`Services/Repositories` + `webapi/Controllers` confirms only the
`BlackoutRepository` and `BlackoutController` reference the table). The public
`Calendar.vue` reads blackouts cosmetically; the day-pass and season-pass
reservation flows, the event-ticket buy flow, and the rental buy flow do not. See
Critical #1.

**Waitlist promote path.** Promotion is automatic. Trigger: any cancel/refund path
(`MeController`, `PurchaseController`, `RentalController` not — rentals have no
waitlist) calls `_waitlistPromoter.PromoteNext(eventId, tierId)`. The promoter peeks
the front of the bucket; pre-paid alternates auto-confirm (creates the paid ticket
row + sends "you're in!" SMS); non-prepaid alternates flip to `promoted`, get a
confirm token + tenant-configured deadline (`WaitlistConfirmWindowMinutes`, min 5
enforced), and an SMS link to `/Waitlist/Confirm/{token}`. The
`WaitlistExpiryWorker` background service sweeps every minute for `promoted` rows
past their deadline, marks them `expired`, and rolls to the next person.
Notification is SMS-only — there is no email fallback.

**Survey state machine.** `draft` → `published` → `closed`. `UpdateStatus` accepts any
transition with no guard (`draft → closed`, `closed → published` reopen,
`published → draft` un-publish). The SQL clears a stale `closes_at_utc` when
re-publishing (good); no other transition has special logic. There is NO
`DeleteSurvey` endpoint (intentional? — surveys with response data should
arguably never be deleted, but the absence isn't documented). Choice reorder is
local-only in the UI until "Save choices" is clicked — confirmed by reading
`SurveyEdit.vue` lines 81-106. Public submission accepts either an invite token
(per-recipient tracking) or a survey-level `public_token` (broad share). Both
paths tenant-scope the survey lookup, so a leaked invite token can't be replayed
under a different subdomain.

**Newsletter subscribers.** `Newsletter/Admin/Subscribers` is the only admin subscriber
list. Gated by `CampaignsManage`. Returns `{Id, Email, Name, Source,
SubscribedAtUtc, UnsubscribedAtUtc}` — `Source` distinguishes self-signup vs.
admin-add vs. import. Bulk import accepts CSV-shaped raw lines and emits
`{Added, Reactivated, Skipped}` counts. There's no admin endpoint on
`EventSubscriptionController` at all — admins manage event subscriptions only
indirectly (via the public token they'd see in a sent email).

**Permission policy mapping.** Verified against the role catalog:
`CatalogManage` covers events, blackouts, event types, pass products, ticket tiers,
extras, rentals, season passes, waivers. `CampaignsManage` covers newsletter
subscribers + surveys. `SalesCounter` covers rental counter (mark out / mark
returned). `SalesRedeem` covers season-pass gate check-in. No miswired policies
found in the catalog endpoints (e.g. no `ReportsView` on a write endpoint).

## Findings

| Severity | Location | Description | Suggested fix |
|---|---|---|---|
| **Critical** | `webapi/Controllers/BlackoutController.cs` (entire file) — and the absence of any reader | Blackout dates close the calendar to bookings — that's the explicit product promise on `vueapp/src/views/Admin/Blackouts.vue` line 14 ("Blackouts close the calendar to bookings") and surfaced to admins as a primary admin nav item. **But** no buy / reservation flow anywhere consults the `blackout` table. Grep of `Services/Repositories` + `webapi/Controllers` for `blackout` returns the repository / controller / DTO trio and zero callers in the price / capacity / reservation paths. `PassPurchaseRepository` happily creates a reservation for a date that's blackout-covered. `SeasonPassController.Reserve` checks event status + capacity + day-of-week (line 388-401) but never asks "is this date blacked out?" `RentalController.Buy` checks per-item maintenance windows but not tenant-wide blackouts. The admin marks the track closed for the weekend, riders show up on the gate-day having paid for a pass, and the only thing standing between them and the closed gate is whatever signage the tenant put on the website. | Wire blackout enforcement into every reservation-shaped endpoint: `SeasonPassController.Reserve` (date check vs. `blackout`), `PassPurchaseRepository.Create` (called from the day-pass buy flow), `EventController.Create` / `Update` (warn — events on a blackout date are odd, but might be a "members-only" exception; let admin override with a confirmation), and `RentalController.Buy` (overlapping check against `blackout` in addition to `rental_item_maintenance`). The simplest read pattern: `BlackoutRepository.IsDateBlackedOut(tenantId, dayUtc)` returning bool, called from each entry point. Without this fix, the entire blackout feature is data-entry-only. |
| **Critical** | `webapi/Storage/LocalFilesystemImageStorage.cs:14-27` (`SaveAsync`) — re-flagged from Section 7 H#8 but bumped to Critical for admin context | This is shared by **eight** admin image-upload endpoints (`EventController.UploadImage`, `EventTypeController.UploadImage`, `ExtraController.UploadImage`, the rental product/variant flows, plus the branding / hero / gallery / track-graphics paths in `TenantController`). The `fileExtension` parameter is concatenated verbatim into the filename. Although every caller in this section's scope routes the extension through a `Dictionary<string, string>` allowlist (`["image/png"] = ".png", ["image/jpeg"] = ".jpg", ["image/webp"] = ".webp"`) keyed on Content-Type, **other callers** (gallery, track graphics, hero — out of this section's scope but on the same storage path) accept `Path.GetExtension(file.FileName)` directly from the upload, with no allowlist. A malicious admin (or a compromised admin account) can upload `image.png/../../etc/passwd` and depending on `Directory.CreateDirectory` semantics traverse out of the tenant folder. Defense-in-depth would have caught this; per Section 7 the recommendation was "validate against an allowlist + lowercase + canonicalize," and it still hasn't shipped. The Section 7 finding noted this; promoting because every admin image surface in Section 8 funnels through here and the surface is bigger than Section 7's signature-only scope. | At `SaveAsync` entry: validate `fileExtension` against a hardcoded allowlist (`.png`, `.jpg`, `.jpeg`, `.webp`), lowercase, reject anything else with `ArgumentException`. Use `Path.Combine` + `Path.GetFullPath` and verify the result starts with the canonicalized `dir` before opening the file. Reject any `kind` that isn't in a known set so the prefix can't be poisoned either. Long-term, move to S3 / DO Spaces (per Section 7 open question 2). |
| **High** | `webapi/Controllers/EventController.cs:277-333` (`Update`) | Event `Update` accepts every field including `StartsAtUtc`, `EndsAtUtc`, and `Capacity` with no check against existing paid purchases. An admin can drag an event from Saturday to Sunday after 200 riders have bought race entries — the tickets move silently, the riders learn at the gate, the tenant carries the chargeback risk. Same for shrinking `Capacity` below current `SpotsReserved`: nothing rejects it. Same for flipping `Status` from `scheduled` to `cancelled` with no refund handling (Section 6 covers refund mechanics; here the admin just sets the column and walks away — the rider's QR still says "valid" and they'll learn at the gate). The dialog warns nobody. | At minimum, refuse `StartsAtUtc` / `EndsAtUtc` changes that move the event date when there is at least one `paid` purchase on it (`pass_purchase`, `event_ticket_purchase`, `event_extra_purchase` joined via `event_id` or `tier_id`). For `Capacity` shrinks, require `request.Capacity >= ActiveSpotsReservedForEvent`. For `Status = 'cancelled'`, branch into a structured cancel flow that triggers an email + refund offer for every paid attendee — or at minimum block status flip and tell the admin "go to Cancel Event flow" which doesn't exist yet. The dialog also needs to warn the admin loudly when any of these constraints fire. |
| **High** | `webapi/Controllers/ExtraController.cs:321-341` (`Update` extra product) + `:137-164` (`UpdateVariant`) | Section 5 flagged the buy-time inventory race; this is the admin-side counterpart. The admin can set `product.Inventory` (tenant-wide cap) to a value lower than what's already been sold — `existing.Inventory = req.Inventory` blindly assigns whatever came in, including `null`. Same for `UpdateVariant` (line 153): `existing.Inventory = req.Inventory` with no sold-count comparison. Net result: the admin types "5" in the inventory field, the product has 12 sold across events, the next rider sees `Remaining = max(0, 5-12) = 0` and the variant looks sold out — but the 12 paid purchasers still have valid claims, and any inventory math on the buy side will report "sold out" even though physically the admin meant to allow 5 *more* sales. The admin had no way to know the value was already exceeded; the API didn't even surface the current `sold` count in the request response shape until *after* the write. | Validate `req.Inventory >= SumSoldProduct(id)` (or `SumSoldVariant` for the variant path) before persisting; if violated, return 400 with the concrete `sold` count and the smallest legal value the admin can set. The same protection should apply to `RentalProduct.InventoryPool` shrinks (`RentalController.UpdateProduct` line 126 — `existing.InventoryPool = req.InventoryPool` with no overlap check against active bookings). The `EventTicketTier` path already has a `SoldCount` query (`EventTicketTierController.cs:117`) — wire the same check into `Update` for tier inventory shrinks. |
| **High** | `webapi/Controllers/SurveyController.cs:121-130` (`UpdateStatus`) — no state-machine validation | The status endpoint accepts any of `draft|published|closed` and transitions in any direction with no guard: `published → draft` (un-publish a survey that already has responses — the invites still work, the public token still works, but admins see `status: draft` which the public submit gate then rejects, breaking out-the-door links until they re-flip), `closed → draft` (same shape — invites in flight will get "not accepting responses"), `closed → published` (reopen after closing — the SQL helpfully clears a stale `closes_at_utc` so submissions resume, but this gives admins zero visibility into the original close reason). There is no audit log on the transition either, so "who closed the survey on me" is unanswerable. Combine with the fact that there's no `DeleteSurvey` endpoint at all — admins who want to retire a survey have only `closed` as a soft option, and any admin can quietly reopen it. | Enforce a state machine: `draft → published` (must have ≥ 1 question), `published → closed`, `closed → published` (allowed but audit-logged). Reject `published → draft` and `closed → draft` explicitly with "Surveys can be closed but not un-published once invites have gone out." Plumb `IAuditLogger` so every status flip records the admin user id + old/new status. Add a `DELETE Admin/{id}` endpoint that requires `status = 'draft'` (or no responses on file) for true cleanup. |
| **High** | `webapi/Controllers/EventTypeController.cs:60-76` (`Update`) | System event types (`IsSystem = true`) can be renamed, recolored, and have their image replaced freely — `Update` has no `if (existing.IsSystem) reject` branch despite `Delete` carefully blocking system-type deletion (line 100). Admin can rename "Race Day" to "Anything" or recolor it to white-on-white so it becomes invisible; system code that lookups by `code = 'race'` (e.g. `EventDialog.vue:233` keying off `t.code === 'race'` to decide tier-vs-pass UX) is unaffected because the code is immutable, but every admin-facing label is corruptible. More concerning: the `Reorder` endpoint also has no system-type carve-out, so an admin can push system types to the bottom of the list — non-destructive but surprising. | Add `if (existing.IsSystem && (existing.Name != request.Name)) return BadRequestResult("System event types can be recolored but not renamed.")` — choose the policy that matches product intent. If renaming is supposed to be allowed for localization, document it; the current implicit "anything goes" reads like an oversight. Reorder is probably fine as-is; document it. |
| **High** | `webapi/Controllers/EventController.cs:361-414` (`Duplicate`) | The Duplicate flow copies pass eligibility + extras eligibility + waiver attachments + image URL, but does NOT copy the source event's `EventTicketTier` rows. Race events live or die on their tier list (the dialog flow even says "save the event first, then add race classes" at `EventDialog.vue:11`). The admin who duplicates a race-day event for next weekend gets the title, date+7d, and image, then has to recreate every race-class tier from scratch — meanwhile every other facet of the event was copied. Riders shopping the duplicated event between the duplicate-click and the admin re-adding tiers see a tier-less event with the misleading admin-side "Save the event first, then add admissions here" flow stuck open. There's no warning in the UI that the duplicate strips the tiers. | Carry the tier list across in `Duplicate`: `var srcTiers = await _tiers.GetForEvent(source.Id, ...); foreach (...) await _tiers.Create(new EventTicketTier { ...source columns, EventId = clone.Id })`. Reset the bundled-coupon ids (they need fresh codes) but keep the count + discount config. If carrying tiers across is product-incorrect (e.g. the admin frequently wants different pricing for the next week), at minimum surface a checkbox in the UI ("Also duplicate ticket tiers?") so the admin makes the choice explicitly. |
| **High** | `webapi/Controllers/WaitlistController.cs` (entire file) — no admin endpoints | `WaitlistController` has only rider-facing endpoints: `Join`, `ListMine`, `ConfirmDetails`, `ConfirmAndPay`, `Cancel`. There is NO admin endpoint to (a) list the waitlist for an event (so a tenant can see "we have 17 alternates for tomorrow's race"), (b) manually promote a specific entry out-of-order, (c) cancel a stale entry, (d) view promotion / SMS-delivery history. Combined with the worker's "SMS-only, no email fallback" (`WaitlistPromoter.cs:114-120`), a rider with a bad / out-of-network phone never knows they were promoted and the spot expires silently after the tenant-configured window — and the tenant has no visibility into the failure either. Section 1's audit logger isn't called for promotions / expiries either, so post-incident there's no record beyond `logger.LogInformation`. | Add `GET /api/Waitlist/Admin?eventId=...` (gated by `CatalogManage` or a new `SalesView`) returning the bucket with statuses + positions + last-SMS-sent timestamps + rider name/email/phone. Add `POST /api/Waitlist/Admin/{id}/Promote` (manual override). Add `POST /api/Waitlist/Admin/{id}/Cancel` with a reason. Plumb `IAuditLogger` into the promoter for `waitlist.promoted` / `waitlist.expired` / `waitlist.auto_confirmed` events. For the SMS-only blind-spot, add an email fallback when the rider has no phone or when SMS send returns failure. |
| **High** | `webapi/Controllers/EventSubscriptionController.cs:89-107` (`StatusByEmail`) | This was flagged in Section 1 as an unauth email-presence oracle. **Re-flagging here for the admin lens:** any tenant subdomain exposes `GET /api/EventSubscription/Status?email=<anything>` with no auth. The response shape leaks `Subscribed=true/false` based on whether the email exists in `event_subscription` for that tenant. Combined with the public `Subscribe` endpoint's `Upsert` (which silently re-activates a previously-unsubscribed row), an attacker can enumerate every email the tenant has ever interacted with via event subscription. This is a Section 1 finding by category but it's worth re-flagging because admins routinely link to this status endpoint from email footers ("[Manage your event subscription](.../EventSubscription/Status?email=...)" — confirmed by inspection of `vueapp/src/views/EventSubscription/Manage.vue` which is the consumer). | Per Section 1: require an opaque token bound to the subscriber row, not the email. Change the link shape in the email footers to `/EventSubscription/Manage/<token>` and have the Manage view exchange that token for the status. Drop the `?email=` query path entirely. |
| **High** | `webapi/Controllers/NewsletterController.cs:222-252` (`ImportSubscribers`) | The CSV import accepts `RawLines` — `[Required] public string RawLines` is the only DTO field — with no upper bound and no row limit. A tenant admin (or a compromised admin account) can paste a 10 MB CSV; the controller `Split('\n')` allocates the full array in memory, then runs an `await GetByEmail + UpsertFromSignup` for every line **sequentially** (await in a loop). 100K rows = 200K database round-trips = the request times out, the API droplet's connection pool starves, every other request in flight blocks. Adjacent concern: there's no progress reporting and no batching, so a partial failure leaves the tenant guessing what got imported. | Cap `RawLines.Length` at e.g. 1 MB at the model-binding layer (`[MaxLength]`), cap line count at e.g. 5,000 per request, and either run the upserts in batches via a single bulk INSERT … ON CONFLICT … or move large imports to a background job. Surface progress via a job-id + poll endpoint. At minimum, return a 400 above the cap with "Split into smaller batches." |
| **Medium** | `vueapp/src/views/Admin/Events.vue:164-193` (`load`) — wide deep-link fetch | When a deep-link `?edit=<id>` is on the URL and the event isn't in the default 6-month window, the page does a "wide window" fetch with `subtract(2, 'year') → add(2, 'year')` = 4 years of events. For a tenant with weekly events that's 200+ rows. The endpoint hydrates pass + extras eligibility per event (see `EventController.cs:91-164` — the bulk GET batches the lookups, but still: the eligibility + product + sold-count batches all scale with the number of events). A large tenant deep-linked into the editor will sit through a slow load. | Add a dedicated `GET /api/Event/{id}` admin endpoint that fetches a single event hydrated for the editor, instead of widening the bulk-list query. The same endpoint can power the "open editor from dashboard" flow without ever touching the calendar query. |
| **Medium** | `webapi/Controllers/EventController.cs:266-275` (`FireAndForgetNotify`) | `Task.Run(async () => { try { await ... } catch { /* logged inside */ } })` is the explicit "fire and forget" pattern. Two failure modes: (a) if the process dies mid-fan-out, some subscribers don't get notified (admin sees the event as created, but for half the list the notification never went out — no retry); (b) the swallowed `catch` means even per-subscriber send failures are invisible to the admin who clicked Save (so a misconfigured Twilio account silently no-ops every notify). For event creation this is probably acceptable for v1; for `Duplicate` (line 407) it's surprising — the admin who duplicates an event for the same set of subscribers may not realize they all just got a duplicate notification. | Move notifications to a real background queue (`Hangfire`, or even a simple `event_notification_outbox` table polled by an `IHostedService`). Until that ships, at minimum add an admin-side toggle on `Duplicate` ("Also notify subscribers?") so the side effect is explicit, and route notify failures through `IAuditLogger` so they're at least recoverable post-hoc. |
| **Medium** | `webapi/Controllers/EventController.cs:209-264` (`Create`) — no overlap warning | Two events can be created with overlapping start/end times in the same tenant. For a single-track operation this is usually a mistake — admins schedule "Race Day 10-2" then accidentally also schedule "Open Practice 1-3" overlapping the last hour. The API accepts both happily; the calendar UI shows the overlap visually but doesn't refuse it. No warning during create / update. Combine with the blackout-not-enforced finding (Critical #1) — admins have no signal that they're double-booking themselves. | Optional, low-impact: in `Create` / `Update`, compute the count of events overlapping `(StartsAtUtc, EndsAtUtc)` and if > 0, return the existing events' titles in the response shape as a `warnings` array (not a hard reject — overlapping events are legitimate for multi-track venues). The dialog can surface "Heads up — this overlaps with: [Race Day] [Open Practice]" so the admin confirms. |
| **Medium** | `webapi/Controllers/BlackoutController.cs:42-60` (`Create`) | Two blackouts can overlap in time with no warning. Two admin staff each create "Memorial Day Weekend Closed" — both rows go in, both show on the public calendar (probably rendered twice). On admin-side `Blackouts.vue` the list shows both. There's no de-dup, no merge, no overlap detection. Combined with Critical #1 (blackouts are decorative anyway), this is low-impact today but will be high-impact the day blackouts are wired into enforcement. | Optional: in `Create` / `Update`, detect any other blackout whose range overlaps the requested range and return a `warnings` field listing them. Don't hard-reject (admin might want a long blackout + a tighter exception inside it for a documented reason). |
| **Medium** | `vueapp/src/views/Admin/Extras.vue:534-556` (`onReorderEnd`) re-implementing the composable | This file has its own copy of the drag-drop logic (same code as `useDragReorder.ts` lines 43-60) inline. The composable was added later and the migration didn't touch this file. Drift risk: a future fix to the composable (e.g. handle the "drop in same slot" edge case better, or batch across slow networks) won't propagate. Same shape exists in `Admin/Settings/HomePage.vue` lines 113-134 (gallery drag-drop is `useDragReorder` but the `onGalleryFileSelected` flow uses `(gallery.value[gallery.value.length - 1]?.sortOrder ?? 0) + 10` which won't survive a hidden-row filter). | Migrate `Extras.vue` to `useDragReorder<ExtraProduct>` with the `filter` predicate `r => showInactive.value || (r.isActive && !isExpired(r))` and `filterDeps: [showInactive]`. Drop the local copy. Bonus: the `admin-list-draggable` skill in `~/.claude/skills/` could be extended to flag pre-composable inline implementations next time someone touches one of these files. |
| **Medium** | `webapi/Controllers/SurveyController.cs:240-302` (`SendInvites`) | Per-recipient send is a sequential `await _emailer.Send(...)` loop. For a 1000-recipient list at 200ms per SMTP send = 200 seconds, well past the request timeout. The admin clicks Send, the page spins, the request times out, the admin clicks Send again, every already-sent invite gets re-sent (the upsert returns the existing invite id so no duplicate token; but `MarkInviteSent` re-stamps `sent_at_utc`, and the same email goes out again because the existing-row branch still calls `_emailer.Send` if the invite wasn't already marked sent successfully on the first pass). No batching, no per-recipient retry, no background-queue handoff. | Move the actual `_emailer.Send` calls to a background job (same shape as the notifier outbox suggested for Medium #12). The admin's `Send` request should return immediately with "Sending 1000 invites — track progress at …" and the job processes them. At minimum, batch via the EmailerService's bulk-send capability if it exists, and skip any invite whose `sent_at_utc` is non-null on a re-send. |
| **Medium** | `vueapp/src/views/Admin/SurveyEdit.vue:127-145` (`addQOpen` choice editor) doesn't use drag-drop for new questions | The "Add question" dialog lets the admin add choices in order via plus / minus buttons, but unlike the per-existing-question choice editor (lines 81-96) it doesn't expose drag-drop. The admin creating a question with 8 choices that need a specific order has to delete + re-add to fix mistakes. Saved questions get the drag handle (good), but the create flow doesn't. | Add `vuedraggable` to the new-question dialog's choices list too. Tiny code, large UX win for the workflow. |
| **Medium** | `vueapp/src/views/Admin/Extras.vue:674-713` (`saveVariants`) — partial-failure half-state | The variants editor batches deletes first, then upserts. A failure midway through (e.g. PG `23505` on a duplicate size/color/gender combination at row 5 of 10) leaves the tenant with rows 1-4 deleted + upserted, row 5 errored, rows 6-10 untouched. The code calls `await load()` in the `finally` (line 705) which resyncs the UI, so the admin can see the partial state — but they can't easily figure out which row failed without the error message naming the row (currently `variantSaveError.value = err.response?.data?.error` shows the BE message without row context). | Wrap the batch in a server-side transaction: add `POST /Products/{productId}/Variants/Batch` taking a `{deletes: [], upserts: []}` payload, run in `BEGIN ... COMMIT` (or roll back on first failure with a structured `{ row_index, error }` response). The client can then highlight the failing row and let the admin fix it before re-submitting. |
| **Medium** | `webapi/Controllers/PassProductController.cs:99-109` (`Delete`), `SeasonPassController.cs:163-175`, `ExtraController.cs:358-369` — admin-toast UX | All three catch PG `23503` and return "set inactive instead." Good. **But** every Delete also takes no precheck on the FE, so the admin clicks Delete on a pass with 47 purchases, the modal posts, the API rejects, the toast flashes red, the admin scrolls up confused. The Pass admin (`Admin/Passes.vue:185-194`) has no Delete-button disabled state, no warning "X purchases reference this pass," and the only feedback is the API error string. EventTicketTier (`EventTicketTierController.cs:145-149`) is the gold standard — it precomputes `SoldCount` and returns a specific message; the FE pattern around it could mirror that on every catalog list view. | Hydrate a `purchaseCount` (or similar) on the admin list responses for every catalog so the Delete button can render disabled with a tooltip ("Has 47 purchases — set inactive instead"), and the click flow short-circuits. Server stays as the authoritative gate; this is purely UX defense-in-depth. |
| **Medium** | `vueapp/src/views/Admin/Surveys.vue` — no pagination | Returns `_surveys.ListByTenant(_tenantContext.TenantId)` with no `LIMIT` / `OFFSET`, and the FE renders all rows. For a tenant with quarterly customer surveys over five years (20 surveys) this is fine; for a tenant using surveys as feature-flag-style mini-polls it could grow unbounded. Same shape on `Admin/NewsletterSubscribers` (`NewsletterController.ListSubscribers` returns all rows — `_subscribers.ListByTenant` has no paging). A 50K-subscriber list is a 5-10 MB JSON payload + a hung browser tab. | Add server-side paging on both: `?page=1&pageSize=50` (with total count for the FE), wired through `ListByTenant`. The newsletter case is more pressing because subscriber lists realistically scale; surveys are less likely. |
| **Medium** | `webapi/Controllers/SurveyController.cs:121-130` (`UpdateStatus`) — no email when surveys close | Publishing a survey doesn't notify subscribers; closing doesn't either. That's fine for `published` (admins explicitly choose when to mail invites). But closing a survey while there are riders mid-fill — they click Submit, the API rejects with "Survey is closed", their answers are lost. There's no "soft close" / "no new responses but in-flight submissions OK" middle state. | Optional: add an `allowInFlightCompletion` window — e.g. when status flips to `closed`, allow `Submit` from invite-token paths for the next 24 hours so anyone already mid-form can finish. Or, less invasive, just document it in the admin UI ("Closing the survey rejects new and in-flight submissions immediately"). |
| **Medium** | `webapi/Controllers/EventController.cs:266-275` (`FireAndForgetNotify` — fire path) + `Duplicate` | `Duplicate` fires `FireAndForgetNotify(clone)` (line 407) — every duplicated event triggers a brand-new "new event!" SMS+email blast to every subscriber. Admin who duplicates 4 race events at once just sent 4 notifications to every subscriber. The admin probably wants this for the first one (the rider needs to know there's a new race) but not necessarily for "edited copy of existing event a week out" duplicates. | At minimum, add a checkbox to the Duplicate dialog ("Notify subscribers about the new event?", default true). Long-term, dedupe in the notifier — if the same event was notified about in the last 24 hours, suppress. |
| **Medium** | `vueapp/src/views/Admin/Events.vue:200-203` (`openEdit`) + `EventDialog.vue` | The Edit dialog doesn't refetch the row before opening. It opens on the snapshot the admin saw at list-load time. If a second admin saved the same event 30 seconds ago, the editing admin's "Save" will clobber their changes (last-writer-wins, no `If-Match` / version check). For a single-admin tenant this is fine; for a 5-staff race weekend it isn't. | Cheapest: refetch on `openEdit` and warn if `updatedAt` differs from list snapshot. Slightly better: add an `updated_at` column → ETag header → `If-Match` request header → 412 on conflict. Probably overkill for v1; documenting as defense-in-depth. |
| **Medium** | `vueapp/src/views/Admin/SurveyEdit.vue:74-77` (per-question prompt `@blur="saveQuestion(q)"`) | The question-prompt input saves on blur with no debouncing. An admin tabbing between questions fires a save for each blur; an admin who clicks away mid-typing fires a save for the partial value. There's no save-in-flight indicator, so a failed save flashes the error toast briefly then disappears — the admin doesn't necessarily realize their last edit didn't persist. The same on-blur pattern is used for `Required` (line 77), gallery captions (`Admin/Settings/HomePage.vue:124-127`), and track graphic title/description (`Admin/Settings/HomePage.vue:168-172`). | Debounce 300-500ms; show a small "saving…" indicator near the field; on error keep the field highlighted and the toast visible until the admin acknowledges. Or batch via an explicit Save button (matches the dialog pattern used elsewhere — surveys are the outlier here). |
| **Low** | `webapi/Controllers/EventTypeController.cs:42-58` (`Create`) — auto-generated `code` | New event types get `code = $"custom_{Guid.NewGuid():N}"`. Long-lived strings nobody can read; nobody filters by it. The original system codes (`race`, `practice`, `open_ride`) are short, readable, and load-bearing (`EventDialog.vue:233` keys race-vs-non-race UX off `t.code === 'race'`). Custom codes can't drive any code path because the code is unpredictable. | Cosmetic. Either drop `code` from the response shape for custom types (so admins don't see opaque GUIDs) or let admins pick the code at create time (with a slugified default from `name`). Lowest impact: stay as-is and document. |
| **Low** | `vueapp/src/composables/useDragReorder.ts:46` (`if (evt.oldIndex === evt.newIndex) return`) | Skips the round-trip when nothing moved. Good. But SortableJS can fire `@end` with `oldIndex === undefined` when the drag was cancelled (e.g. dropped outside the container) — the comparison `undefined === undefined` is true, so the early return covers it. Behavioral edge case worth a comment. | Add a one-line comment confirming the early return covers the cancel-drag case too (`undefined === undefined`), so a future contributor doesn't add a separate `if (evt.oldIndex == null) return` thinking it's missing. |
| **Low** | `webapi/Controllers/SeasonPassController.cs:121-149` (`UpdateProduct`) | `ValidFromDate` / `ValidToDate` can be edited even after purchases exist — same shape as Event editing dates (High #3 above) but for season passes, which are date-range-anchored. An admin shortening the validity window from "all season" to "March only" after riders have already bought retroactively invalidates their passes. | Disallow validity-range narrowing when paid purchases exist (`SUM(...) > 0`). Widening is fine. |
| **Low** | `webapi/Controllers/NewsletterController.cs:36-58` (`Subscribe`) — no rate limit | Public endpoint, no captcha, no rate limit, no tenant-side flag for "is this tenant accepting newsletter signups." A bot or a malicious actor can spray a tenant's subscriber list with thousands of fake emails. The `UpsertFromSignup` is idempotent so each unique email is one row, but a script can iterate aaa@x.com → zzz@x.com and pollute the subscriber list. | Add an IP-based rate limit on this endpoint (same pattern as Section 7's signup recommendation). Optional: gate by a tenant feature flag `AllowNewsletterSubscriptions` paralleling `AllowEventSubscriptions`. |

## Patterns worth replicating

- **`EventController.ValidateWaiverForEvent` (lines 448-458)** — refuses to attach
  a waiver that expires before the event ends. Tight, well-commented, the right
  level of defensive. Same shape should apply to season passes if they ever pin
  per-event waivers.
- **`EventTicketTierController.Delete` (lines 136-153)** — precomputes
  `SoldCount` and returns a specific "set inactive instead" message *without*
  relying on the FK exception. This is the better-than-catching-`23503` pattern
  every catalog Delete should adopt.
- **`useDragReorder` composable** — clean separation of concerns; bind to
  `visibleRows`, server stays the authority via the bulk-update endpoint,
  rebuilds the canonical list with hidden rows held in place. The interleave
  logic (lines 47-50) is the load-bearing part and it's the right shape.
- **`UpdateProductSortOrders` SQL (every catalog repo)** — single statement
  `UPDATE … FROM (SELECT unnest(@ids), unnest(@orders))` with tenant predicate.
  Atomic (single statement in PG is implicitly transactional), tenant-safe
  (a rogue id from another tenant just doesn't match any row), last-writer-wins
  which is correct for sort_order.
- **`SurveyController.GetPublic` + `SubmitPublic` token branching** — try invite
  token first (gives per-recipient open + complete tracking), fall back to public
  share token. Both paths tenant-scope the survey lookup so a leaked token from
  tenant A can't be replayed on tenant B's subdomain. Subtle, correct.
- **`EventController.UploadImage` content-type allowlist** (lines 431-438) — the
  right shape; just needs the `LocalFilesystemImageStorage` `SaveAsync` fix to
  close the gap (Critical #2 above). Other admin image uploads (`EventType`,
  `Extra`) copy the same pattern, which is good.
- **`WaitlistPromoter` pre-paid auto-confirm branch** (lines 75-106) — riders
  who pre-paid skip the timer entirely; the promoter creates the paid ticket row,
  charges via the existing PI, and sends a "you're in!" SMS in a single
  atomic-ish flow. Clear logic, well-commented.

## Open questions

1. **Are blackouts intended to actually enforce closures, or are they a calendar
   decoration?** Critical #1 — the UI promises "close the calendar to bookings"
   but no buy code checks them. The fix is small; the question is whether it's
   product-correct to refuse paid bookings on blackout days vs. just warn.
2. **Should `Duplicate` carry over ticket tiers by default?** High #7 — current
   behavior strips them, which surprises admins for race events but might be
   intentional for "starting a new event template."
3. **Survey state machine — what's the intended set of transitions?** High #5 —
   today any → any is allowed. If `closed → published` is supposed to be
   reopenable, that should be documented; if not, it should be rejected.
4. **Should admins be able to delete surveys?** No `DeleteSurvey` endpoint
   exists. If retention-by-default is the intent, fine. If admins are expected
   to delete drafts, the endpoint is missing.
5. **What's the admin recovery flow for a stuck waitlist promotion?** High #8 —
   today, none. If a rider's phone is wrong, the spot expires silently and
   rolls to the next person; the admin has no visibility. Worth a follow-up
   spec for the admin-side waitlist view.
6. **Why doesn't `EventSubscriptionController` have admin endpoints?**
   Newsletter has the admin list + import; event subscriptions don't. If the
   intent is "events fan-out is automatic, no admin curation needed," fine;
   if admins ever need to see who's subscribed to event notifications,
   parallel endpoints are missing.
7. **Should `EventController.Update` enforce a frozen-after-sale field set?**
   High #3 — today every field is editable. Pinning a frozen set (date, capacity,
   status) post-first-sale is a real product question; documenting the intended
   policy would let us hard-code it.
8. **`LocalFilesystemImageStorage` — is the move to S3/DO Spaces blocked on
   anything specific?** Section 7 raised this; Section 8 promotes the path-traversal
   risk to Critical because of the wider admin surface. If S3 is a few weeks
   out, the in-place hardening (allowlist + canonicalize) is a one-PR fix that
   buys time.

## Coverage notes

- Read all 11 controllers in scope end-to-end. Re-read `EventController`,
  `SurveyController`, and `ExtraController` (the longest) twice for the
  CRUD-invariant lens after the first pass had spotted the inventory + date
  gaps.
- Verified `_tenantContext.IsResolved` is checked at the top of every
  controller's GET endpoints (gates the data read). Write endpoints rely on
  the `[Authorize(Policy = CatalogManage)]` + the tenant-scoped repository
  predicate; spot-checked half-a-dozen for cross-tenant safety against ids
  pulled from a parallel tenant in pgAdmin. All correctly null-out / no-op.
- Verified every drag-drop reorder is in a single SQL `UPDATE … unnest` with
  tenant predicate (or parent-predicate, for tier/question/choice).
- Verified the `useDragReorder` composable's `visibleRows` interleave behaviour
  against three different consumers (`Passes`, `SeasonPasses`, `Rentals`) plus
  the bespoke implementation in `Extras.vue` (which behaves the same way but
  has its own copy — Medium #14).
- Verified the waitlist promote → expire → re-promote loop end-to-end via
  `WaitlistPromoter` + `WaitlistExpiryWorker`. Confirmed there is no admin
  surface area.
- Verified every catalog Delete catches `23503` and returns a helpful message,
  except `Blackout.Delete` (no dependents) and `Waiver.Delete` (covered by
  Section 7).
- Spot-checked every `[Authorize(Policy = ...)]` against the role catalog in
  `TenantPermissions.cs`. No miswired policies in this section's endpoints.
- Confirmed `EventSubscriptionController` has no admin endpoints (Open #6).
- Confirmed no admin Calendar view exists (the public `Calendar.vue` was checked
  and is read-only).
