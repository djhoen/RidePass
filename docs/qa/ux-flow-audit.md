# RidePass UX / flow logic audit

Started 2026-06-22. A read-only review of the Vue UI flows for places where the
logic does not make sense to a user, then a confirmation pass that re-read each
finding against the code (including the backend, to decide whether a UI gap is
already guarded server-side) and proposed a fix.

Status legend:
- `confirmed` = re-verified as a real, user-visible problem; fix proposed
- `guarded` = real in the UI, but the backend (or a worker/TTL) prevents the
  harmful outcome; remaining work is UX only, lower priority
- `intentional` / `rejected` = not a bug (none landed here this pass)

Verdict summary: of the items drilled into, the money/safety-critical ones
(over-refund, payout double-settle, self-lockout, abandoned PaymentIntents,
sticky impersonation) all came back **guarded** by a server-side check, worker,
or token TTL. The confirmed-and-unguarded issues are UX/correctness gaps:
stranded users, mislinks, misleading copy, and controls that do nothing.

---

## Cross-cutting patterns

### X1. Stripe redirect strands paid users  `confirmed` ✅ fixed 2026-06-22
`confirmPayment({ redirect: 'if_required', return_url: window.location.href })`
with no mount-time inspection of the returned `payment_intent` / `redirect_status`.
Inline (no-redirect) card payments work; redirect-required methods (some 3DS,
wallets, bank redirects) come back to a fresh page and the post-payment step never runs.
- `EventCheckout.vue` (remounts at `step='select'`; the rider-registration + waiver step is lost). FIX (done): `createIntent` captures `resumeToken` from the order's first ticket; `pay()` sets `return_url` to `/FinishRegistration/{resumeToken}` (router `:424`, public, backed by `GET Purchase/EventTicket/Registration/{token}`) when a token exists; inline fast path unchanged.
- `Membership.vue` mount. FIX (done): `onMounted` reads `payment_intent`/`redirect_status`, sets purchased + flashes accordingly, `history.replaceState` to strip params, then `load()`.
- `Counter.vue`. FIX (done): `pay()` stashes the receipt (line items + total) in `sessionStorage` keyed by PaymentIntent id before `confirmPayment`; `onMounted` detects the return, restores the stash, jumps to step 5 (or flashes failure), strips params. Degrades to an empty-but-correct receipt if the stash is missing.
- Sibling flows (season pass / rental / waitlist) already point `return_url` at a dedicated page, so this was an inconsistency, not unavoidable.

### X2. `catch { x = [] }` renders failures as empty states  `confirmed` ✅ fixed 2026-06-22
A load/action error looked identical to "no data," so users thought content vanished.
- `Upcoming.vue` (order-detail failure showed "No items found"). FIX (done): added `orderError` ref + an error branch in the dialog; the catch no longer fabricates empty `items`.
- `MyPasses.vue` (rentals/extras/waitlist catches swallowed real failures). FIX (done): each catch keeps the benign disabled-feature 404 silent but `flash()`es the server message on any other status; the always-run waitlist catch surfaces too. The page still renders whatever loaded.
- `Rewards.vue` (load failure left `programs=[]` -> "no rewards"; `loading` inited false -> empty-state flash). FIX (done): `loading=ref(true)`; added `loadError` ref + an error branch before the empty branch (replaces the transient toast).
- `Profile.vue` (newsletter status catch hid the whole toggle card). FIX (done): added `newsletterError`; the card stays visible showing a degraded `v-alert` notice. (The loampass-status catch is genuinely optional, left silent.)
- `EmailUnsubscribe.vue` (worst: a POST failure wrote the shared `errorText`, repainting a valid link as "Unsubscribe link invalid"). FIX (done): added a separate `actionError` rendered inline inside the `status` block with an informative message; `errorText` is now only the load/bad-link path.

### X3. Inconsistent editor save models lose work  `confirmed` ✅ fixed 2026-06-22
- `Concessions.vue` per-row Save + footer "Done"; an unsaved row was dropped on Done. FIX (done): removed the per-row Save, footer is now Cancel + "Save & Close" (`saveAllVariants` batch-upserts every row, keeping new ids), matching the Extras editor. Delete keeps its confirm.
- `SurveyEdit.vue` prompt/required auto-saved on blur, but choices needed a separate "Save choices" button; edits were lost on navigate-away. FIX (done): `saveChoices` is now silent (no reload/toast, error-only) like `saveQuestion`, and fires on label blur, "Other" toggle, remove, and reorder. The explicit button is gone.
- `HomePage.vue` five "Save X" buttons all called one `saveContent()` that persists every section together. FIX (done): collapsed to a single "Save home page" button after the content cards. (Status + Heroes still save independently.)

### X4. Missing start<=end / non-empty validation  `confirmed` ✅ fixed 2026-06-22
All let an admin save config that silently can't work. FIX (done): each uses the page's existing `flash`/snackbar + early return.
- `Coupons.vue` save() now rejects validFrom > validTo.
- `SeasonPasses.vue` save() now requires a name, validFromDate <= validToDate, at least one day for a days_of_week pass, and credits >= 1 for a credits pass.
- `Blackouts.vue` timed branch now requires both times and enforces ends > starts (mirrors the all-day guard).
- `HomePage.vue` saveContent() rejects any open day whose close <= open.
- `SalesSummary.vue` load() blocks an inverted From > To range with a message instead of a silently empty report. (The inclusive/exclusive display of the To field is the separate L10 item, left as-is.)

---

## Top individual issues

| ID | Verdict | Finding + fix |
|----|---------|---------------|
| T1 | `confirmed` ✅ fixed | Login ignores `?next=` (`Login.vue:99-116`, no `useRoute`); `Waiver.vue:115`, `WaitlistConfirm.vue:192` send `?next=`, and `main.ts:109-110` interceptor doesn't even set it. FIX: in Login honor a same-origin `next` (`startsWith('/') && !startsWith('//')`) before defaults; have the interceptor push `{path:'/Login', query:{next: currentRoute.fullPath}}` (skip when already on /Login). |
| T2 | `confirmed` ✅ fixed | Spectator/gate-fee-only events unbuyable: `Event.vue:278` loads tiers only if `hasRaceEntryTiers`, but the server DTO sets `HasActiveTiers` for any active tier (`EventController.cs:166-168`). FIX: gate the load on `event.hasActiveTiers`. |
| T3 | `guarded` ✅ fixed 2026-06-22 | Cancelled event still shows checkout (`Event.vue:26,86`), but backend rejects: `PurchaseController.cs:282-286` returns 400 for non-scheduled/ended events, surfaced via `EventCheckout.vue:569`. FIX (done, UI only): added a `v-else-if="event.status === 'cancelled'"` branch showing "Ticket sales are closed for this event." ahead of the checkout. |
| T4 | `confirmed` ✅ fixed | Counter card-payment dead-end: once `clientSecret` set, stepper locks (`Counter.vue:416-426`) and step 4 (`:321-327`) has only Charge, no Back/Cancel; reload orphans pending rows + uncaptured PI. FIX: add a `useConfirm`-gated "Start over" calling `reset()`; ideally a backend abandon endpoint that voids the PI + pending rows. |
| T5 | `confirmed` ✅ fixed | Upcoming season-pass/membership cards link to `/User/MyPasses` (`Upcoming.vue:42-43,282-286`), which never renders those kinds (they live at `/User/SeasonPasses` and `/Membership`). FIX: make `tenantUserUrl` kind-aware. |
| T6 | `confirmed` ✅ fixed | Notifications unreachable on mobile: `<NotificationBell>` only in `NavBar.vue:32` `!isMobile` branch; no drawer entry. FIX: render the bell in the mobile app-bar (`:80-81`) guarded by `isAuthenticated`. |
| T7 | `confirmed` (balances) / `guarded` (race) ✅ fixed 2026-06-22 | `Payouts.vue:305-322` mark-paid refreshed the dialog but not `loadBalances()` -> stale grid. Double-settle is impossible server-side (`SuperAdminController.cs:843-845` status guard + idempotency key `:876`). FIX (done): added `await loadBalances()` after mark-paid; disabled Mark paid + Void while `stripeSendingId === p.id`. |
| T8 | `confirmed` ✅ fixed (chose: hide) | Season-pass per-event-type discount % does nothing: copy at `SeasonPasses.vue:100-103` admits it, and no checkout/reserve path reads perks (QA doc `passes-and-season-passes.md:122` confirms "inert"). FIX: hide the discount field (keep the include checkbox) and correct the copy until the pricing path is wired. |
| T9 | `confirmed` ✅ fixed (chose: drop claim) | Membership "required" advertised (`Features.vue:140`) but no control in `Membership.vue` (fields hard-coded riders=true/spectators=false at `:87-93`, page copy says not required), and enforced nowhere (`PurchaseController.cs:377-379` "no longer required"). FIX: drop the Features claim + the dead fields, OR add `v-switch` controls and wire the gate into the buy paths (larger). |
| T10 | `confirmed` ✅ fixed | Surveys publish with no questions / zero-choice questions (`SurveyEdit.vue:442,462-488`). FIX: a `publish()` guard rejecting empty surveys and choice questions with <2 choices via `flash`. |
| T11 | `confirmed` ✅ fixed | Gift card success card has no next action (`BuyGiftCard.vue:74-86`). FIX: add "Send another" (reset to compose) + a home link. |
| T12 | `confirmed` ✅ fixed | Redeem toast hides partial success: `RedeemTickets.vue:191-194` shows only errors when any exist. Backend returns both count + errors (`RedemptionController.cs:336-359`). FIX: in the mixed case show "Redeemed N; M skipped: ..." (add `warning` to the snackbar color type). |

---

## Medium

| ID | Verdict | Finding + fix |
|----|---------|---------------|
| M1 | `guarded` ✅ fixed | Guest season-pass purchase: `/SeasonPasses` public but buy is `[Authorize]` (`SeasonPassController.cs:184`), so a guest 401s -> interceptor logs out, after filling the photo/waiver dialog. FIX: gate entry in `openPhotoStep` (`BuySeasonPass.vue:144`) -> push `/Login?next=/SeasonPasses` if not authed. |
| M2 | `confirmed` ✅ fixed (chose: cross-link + resubscribe) | Three independent unsubscribe pages + a Profile toggle, none cross-referencing; marketing (`EmailUnsubscribe.vue:34-41`) is the only one with no resubscribe. FIX: cross-link to the profile preference area on each; add resubscribe to marketing (needs a `SuppressionService.resubscribe` if absent). |
| M3 | `confirmed` ✅ fixed | CustomerDetail total (`CustomerDetail.vue:214-225`) is client-side over passes+tickets+seasonpasses, paid-only, so it contradicts the server total on `Customers.vue:31-32` and reads $0 for an all-redeemed customer. FIX: add server `totalPurchases`/`totalSpentCents` to `CustomerDetailDto` and use them; interim, relabel the figure. |
| M4 | `confirmed` ✅ fixed | RentalCounter filters don't auto-reload (`RentalCounter.vue:6-10`), unlike `Purchases.vue:274`. FIX: `watch([fromDate,toDate,statusFilter], () => load())`. |
| M5 | `confirmed` ✅ fixed (chose: auth-gate) | `/Membership` is `requiresAuth:false` (`router.ts:53-58`) but `load()` calls `getStatus()` which 401s a guest -> forced logout (`Membership.vue:178-190`). FIX: skip the status call for guests and show the public buy view (needs a public offer payload), or make the route `requiresAuth:true` + combine with T1. |
| M6 | `confirmed` (no-confirm) / `guarded` (self-lockout) ✅ fixed 2026-06-22 | Disable/enable was a single click, no confirm (`Users.vue:48-55,264-272`), while Reset Password beside it confirms. Self-lockout is blocked server-side (`UserController.cs:517-519,538-540`). FIX (done): added a `confirm()` gate in `setStatus` for the disable path only (re-enable stays unconfirmed). |
| M7 | `confirmed` ✅ fixed | Waiver body can be blank (`Waiver.vue:228` only checks name+title). FIX: add a stripped-HTML non-empty check to `canSave`. |
| M8 | `confirmed` ✅ fixed | Expiry dates use literal `T23:59:59Z` (`Extras.vue:411-418`, `Waiver.vue:220-226`), cutting off hours early for non-UTC tracks. FIX: use `dayjs.tz(...,branding.timezone).utc()` (pattern in `Blackouts.vue:122-129`). |
| M9 | `confirmed` ✅ fixed | Auto-geocode silently overwrites a manually chosen timezone (`General.vue:275-278`). FIX: track a `tzTouched` flag (set from the tz autocomplete) and don't overwrite when set. |
| M10 | `guarded` | Impersonation token has a 1-hour TTL (`SuperAdminController.cs:521-524`) and boot-time expiry clears it (`AuthHelper.ts:77-99`), so the sticky window is bounded to 1h. No required fix; optionally shorten TTL / show a countdown. |
| M11 | `confirmed` ✅ fixed | Super-admin sees the rider account dropdown (`NavBar.vue:45-72`, no `isSuperAdmin` guard) -> links to empty/error tenant pages; the drawer already hides them. FIX: wrap the rider items in `v-if="!isSuperAdmin"`. |
| M12 | `confirmed` ✅ fixed | Analytics is blank on first load and after error (`Analytics.vue:16,44`; only a 4s snackbar). FIX: add loading + error (carry `err.response.data.error`) + empty states. |
| M13 | `confirmed` ✅ fixed | WaitlistConfirm dead end: day-pass alternate with zero eligible passes leaves a permanently disabled Continue (`WaitlistConfirm.vue:60-69,81,171-175`). FIX: hide Continue and show a "Back to Events" link in that case. |
| M14 | `confirmed` ✅ fixed | Authed buyer with blank/1-char profile name is silently blocked at event checkout (name readonly `EventCheckout.vue:97-99`, `detailsValid` needs len>1 `:433`). FIX: only lock the name field when already valid; add a hint when authed-but-invalid. |
| M15 | `guarded` ✅ fixed 2026-06-22 | Refund/deposit had no UI upper clamp, but the server clamps both (`PurchaseController.cs:1388-1389`, `RentalController.cs:629`). FIX (done): added `:max` + `:rules` to both fields and clamped client-side in `confirmRefund` / `confirmReturn`; also corrected the rental return toast to distinguish full vs partial deposit kept. |
| M16 | `confirmed` ✅ fixed | "Feature disabled" handled three ways: SeasonPass `router.replace('/')` (`:188`), Rentals neutral empty card (`:9`), GiftCard explicit alert (`:9`). FIX: standardize on the explicit in-page info alert; split Rentals' disabled vs empty conditions; stop the silent SeasonPass redirect. |

---

## Low / latent

| ID | Verdict | Finding + fix |
|----|---------|---------------|
| L1 | `confirmed` ✅ fixed | `useConfirm.ts:58-69` overwrites the resolver on a concurrent call; the first await never settles. FIX: `if (confirmState.resolver) confirmState.resolver(false)` before reassigning. |
| L2 | `confirmed` ✅ fixed | `SocialShare.vue:105-115` clipboard fallback uses native `window.prompt` (banned). FIX: show the link in the existing snackbar / a styled read-only field instead. |
| L3 | `confirmed` ✅ fixed | `MiscSettings.vue:152-191` three catches discard `err.response.data.error`. FIX: surface the server message with a generic fallback. |
| L4 | `confirmed` ✅ fixed | `EmbedCalendar.vue:111-117` fetches a fixed window but `EventCalendar.vue:103-105` lets paging outside it -> blank grid. FIX: clamp chevrons to the window (or refetch on month change). |
| L5 | `confirmed` (minor) ✅ fixed | `NewsletterSignup.vue:13-14,33-50` locks to disabled "Subscribed" for the session; a wrong email can't be resubmitted. FIX: reset `subscribed` on email change; distinguish already-subscribed if the API reports it. |
| L6 | `confirmed` ✅ fixed | `Bootstrap.vue:8-10` claims the form self-disables after the first super admin; it doesn't (server-reject only). FIX: correct the copy (or add an existence probe + disable). |
| L7 | `confirmed` ✅ fixed | Counter cart only holds extras+membership (`Counter.vue:390`) but the voucher hint `:164` and a stale comment `:444` reference tickets/passes; event-ticket counter sale is wired server-side (`CounterController.cs:239-323`) but not surfaced, and "pass" is unimplemented (`:405`). FIX: correct the copy now; optionally add an event-ticket panel later. |
| L8 | `confirmed` ✅ fixed | Counter empty-catalog soft dead-end (no panels, Continue disabled, no message) when extras off + no membership (`Counter.vue:168-205`). FIX: add an explanatory `v-alert`. |
| L9 | `confirmed` ✅ fixed | Inbox copy says inbound texts "land here automatically" but there's no polling (`Inbox.vue:4-7`). FIX: soften the copy or add a 30s poll (gated on not-loading/sending; transient poll failures may stay quiet). |
| L10 | `confirmed` ✅ fixed | SalesSummary presets set `rangeTo` to an exclusive bound shown in the To field (`SalesSummary.vue:160-185`), so it reads one day past intended (same in `Blackouts.vue:98`). FIX: display an inclusive date, add the +1 day in `load()` instead. |
| L11 | `confirmed` ✅ fixed | SeasonPasses empty state offers Browse only when enabled; bare dead-end text otherwise (`SeasonPasses.vue:8-13`). FIX: add a `v-else` explaining the track isn't selling passes. |
| L12 | `guarded` | Pay-dialog X abandons a created PaymentIntent, but `PendingPurchaseReconciler` reconciles/cancels pending rows (20m/2h cutoffs). FIX (UX): `useConfirm` on close + a "nothing was charged" snackbar. |
| L13 | `confirmed` ✅ fixed | Waitlist countdown can swap the payment form out mid-pay (`WaitlistConfirm.vue:17,154-160`). FIX: don't show the expired card once `clientSecret` exists; let the server reject a genuinely-late confirm. |
| L14 | `confirmed` (cosmetic) ✅ fixed | `Refunds.vue:47,108` keys `processingId` by `id` only though rows are `kind:id`; a shared id double-spins both buttons. FIX: key `processingId` by `kind+':'+id`. |

---

## Additional findings (discovered while fixing)

### B1. Gift-card-paid order refunds incorrectly  `confirmed` ✅ fixed 2026-06-22
A gift card applied at checkout reconciles correctly (balance debited, `gift_card_redemption`
rows, Stripe charged only the remainder), and a failed/abandoned remainder restores the
balance. But the shared refund path `RefundOne` ignored the split: it refunded the full line
`AmountCents` to Stripe (over-refunding the gift-card-covered portion, or erroring on a
single-item PI) and never returned the gift-card value. Affected every kind through `RefundOne`
(tickets, season passes, memberships, add-ons). FIX (done, card-first policy): added
`IGiftCardRepository.SumRedemptionsForSource`; `RefundOne` now refunds `min(refundCents, amount - giftCardPortion)`
to Stripe and routes the overflow back to the gift card via the new `RestoreGiftCardOnRefund`
helper (deletes the redemption rows, restores the balance, re-records any still-applied remainder
on a partial refund). The refund ledger stays at `-refundCents` (full), consistent with the
sale recording full gross. Needs an end-to-end stage test (split-pay then full + partial refund).
