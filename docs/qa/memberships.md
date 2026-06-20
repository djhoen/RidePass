# QA Test Plan: Memberships

> Scope: tenant membership program config (enable / name / price / duration / required-for flags), rider purchase via the standalone /Membership page, bundled-with-checkout purchase, POS counter membership sale, pending -> paid / failed / refunded transitions, the "active membership" read model, and tenant isolation. Last updated: 2026-06-20.

## Surface map
- **Admin (settings):** `MembershipController.UpdateSettings` (`PUT api/Membership/Settings`, policy `SettingsManage`); `MembershipController.ListForAdmin` (`GET api/Membership/Admin`, policy `SalesView`). The enable/disable toggle is owned by **Features** (`Admin/Settings/Features.vue`), which calls the same `Settings` endpoint preserving the other fields. Field editor: `Admin/Settings/Membership.vue`.
- **User (buy):** `MembershipController.Status` (`GET api/Membership/Status`, anonymous-friendly) and `MembershipController.Buy` (`POST api/Membership/Buy`, `[Authorize]`). Frontend `User/Membership.vue` + `MembershipService.ts`.
- **Bundled online:** `PurchaseController.CreatePurchase` honors `AddMembership` (`CreatePurchaseRequest` / `CreateTicketPurchaseRequest`): when the buyer has no active membership it mints a `pending` membership row and folds its price into the same PaymentIntent.
- **POS:** `CounterController` checkout accepts a cart item with `Kind == "membership"` (`CounterCartItem`); `Admin/Counter.vue` `addMembershipToCart`. Cash path flips rows to `paid` inline; card path stamps the PI and waits for the webhook.
- **Finalizer:** `StripePurchaseFinalizer.OnMembershipPaid` flips `pending -> paid` and writes a `source_kind = 'membership'` ledger entry on `payment_intent.succeeded`; `payment_intent.payment_failed` flips to `failed`.
- **Refund / cancel:** `PurchaseController` refund case `"membership"` -> `MembershipRepository.Cancel` (only when `status = 'paid'`) then `MarkRefunded`.
- **Read model / reporting:** `MembershipRepository.GetActive` (active = paid + unexpired), `ListMine`, `ListForTenant`; `v_recent_sales` UNION branch for `membership` (`Script0080_RecentSalesView.sql`).
- **Migrations:** `Script0058_Memberships.sql` (tenant columns + `membership_purchase` table + ledger `source_kind`), `Script0070_MembershipRiderSpectator.sql` (collapsed the four `required_for_*` flags into `riders` / `spectators`), `Script0074_RedemptionAudit.sql` (added `sold_by_user_id`).

## Concepts under test
- **Tenant config** lives on `tenant`: `membership_enabled`, `membership_name`, `membership_price_cents` (>= 0), `membership_duration_kind` in {`one_time`, `yearly`}, `membership_required_for_riders` (default true), `membership_required_for_spectators` (default false).
- **Duration window:** `yearly` => `valid_to_utc = now + 365 days`; `one_time` => `valid_to_utc = NULL` (lifetime). Set identically on all three buy paths (standalone, bundle, POS).
- **Frozen at purchase:** `name_at_purchase`, `price_cents`, `duration_kind`, `valid_from/valid_to`, `amount_cents` are stamped on the row so later config edits never rewrite history.
- **Active membership:** `GetActive` = `status = 'paid'` AND (`valid_to_utc IS NULL` OR `valid_to_utc > now`), ordered lifetime-first then latest expiry. Drives the green "active / lifetime" card and the bundle "skip if already a member" decision.
- **Status lifecycle:** `pending` -> `paid` (webhook success or POS cash) / `failed` (webhook failure) / `cancelled` + `refunded` (admin refund). DB CHECK enforces the allowed set.
- **One per cart / per PI:** POS rejects quantity != 1 and a second membership line; the online bundle mints at most one and only when no active membership exists.
- **Tenant-funded pricing:** the rider is charged exactly `price_cents` (no separate rider service-charge line; `RiderServiceChargeCents = 0`). `service_charge_cents` is still recorded for the tenant ledger fee math.
- **Requirement flags are config-only today:** `PurchaseController` no longer gates entry purchases on membership ("Membership is no longer required to buy an entry"). The `required_for_*` flags persist but have no enforcement path. See Known risks.

## Preconditions / test data
- Two tenants on distinct subdomains (call them **A** and **B**), each with Stripe test keys wired. Use Tenant B only for isolation checks.
- Tenant A: ability to toggle Memberships on in Settings -> Features and set name/price/duration in Settings -> Membership.
- A staff user with `SettingsManage` + `SalesView` + `SalesCounter`, and a separate user with neither (for authz negatives).
- Two rider accounts (global accounts) plus one email that has no account (for the POS "no customer" path).
- Stripe test cards: a success card and a decline card; access to the Stripe webhook (or a way to replay `payment_intent.succeeded` / `payment_intent.payment_failed`).
- Direct read access to `membership_purchase`, `tenant`, `tenant_ledger_entry`, and `v_recent_sales` to confirm frozen values and ledger rows.

---

## Admin (settings)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| MEM1 [NN] | Enable via Features | Settings -> Features, toggle Memberships on | `membership_enabled = true`; toggle calls `Settings` and preserves the existing name/price/duration/flags (does not reset them). Rider /Membership page now shows the buy card. |
| MEM2 [NN] | Configure name / price / duration | Settings -> Membership: set name "BMX Club Card", price $40, duration Yearly; Save | `Settings` persists; reload shows the same values. Preview card mirrors name/price and "Yearly . valid 365 days". |
| MEM3 [NN] | Price stored in cents | Save price $40 | `membership_price_cents = 4000`. UI shows whole dollars; the cent conversion round-trips (e.g. reopen still reads $40). |
| MEM4 [NN] | Duration switch is forward-only | Change duration from Yearly to One-time, Save; view an already-bought yearly membership | New config is `one_time`; the previously purchased row keeps `duration_kind = 'yearly'` and its original `valid_to_utc` (frozen). |
| MEM5 [NN] | Required-for flags persist | Set RequiredForRiders on, RequiredForSpectators off, Save | Values persist in `tenant`. NOTE: no checkout gate consumes them today (document, do not expect a block). See MEM-RISK. |
| MEM6 [R] | Settings authz | Call `PUT api/Membership/Settings` as a user without `SettingsManage` | 403 / forbidden. |
| MEM7 [NN] | Name required + bounds | Save with blank name; save with price -1; save with duration "monthly" | Blank name rejected (Required, client Save disabled + server `[Required]`); negative price rejected (`Range`/CHECK); invalid duration rejected (`^(one_time|yearly)$`). |
| MEM8 [NN] | Admin sales list | Settings -> (admin membership list) / `GET api/Membership/Admin` after some sales | Returns this tenant's membership rows newest-first with name, duration, valid window, amount, status, created date. Authz: requires `SalesView`. |
| MEM9 [NN] | Disable hides buy, keeps history | Toggle Memberships off after sales exist | Rider /Membership shows "This track doesn't sell memberships"; existing paid memberships still read as active via `GetActive` (disable does not expire them). |

---

## User (buy)

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| MEM10 [NN] | Status while signed out | Open /Membership not signed in (enabled tenant) | `Status` returns config snapshot (enabled, name, price, duration) with `Active = null`, empty history; price card renders. |
| MEM11 [NN] | Buy yearly happy path | Signed-in rider clicks Continue to Payment, pays with success card | `Buy` creates a `pending` row (`amount_cents = price_cents`, `valid_to_utc = now+365d`); webhook flips to `paid`; page reload shows green "active" card "Valid through <date>". |
| MEM12 [NN] | Buy lifetime (one_time) | Tenant set to one_time; rider buys | Row has `duration_kind = 'one_time'`, `valid_to_utc = NULL`; active card shows "Lifetime . never expires". |
| MEM13 [NN] | Charge equals price (no rider fee) | Inspect the PaymentIntent and `BuyMembershipResponse` | `AmountCents == price_cents`, `RiderServiceChargeCents == 0`; rider is not charged an added service fee. |
| MEM14 [NN] | Buy when sales disabled | Disable Memberships, then `POST api/Membership/Buy` | 400 "Memberships aren't sold at this track." No row created. |
| MEM15 [NN] | Buy with price 0 | Set price to $0, attempt Buy | 400 (price <= 0 rejected). Confirms free memberships are not mintable via this path. |
| MEM16 [NN] | Buy requires auth | `POST api/Membership/Buy` unauthenticated | 401 (endpoint is `[Authorize]`). |
| MEM17 [NN] | Failed payment | Start a buy, pay with the decline card | Webhook `payment_intent.payment_failed` flips the pending row to `failed`; `GetActive` still null; no ledger sale row; rider can retry. |
| MEM18 [NN] | History shows paid + refunded only | After a paid (and later refunded) purchase plus a failed attempt | /Membership History table lists paid/refunded rows; `failed`/`pending` are filtered out of the rider history view. |
| MEM19 [NN] | Renew / second active row | Rider with an active yearly membership buys again | `Buy` does NOT block a duplicate (no active-check on the standalone path); both rows can be `paid`; `GetActive` returns the lifetime-first / latest-expiry row. Confirm intended (see MEM-RISK). |
| MEM20 [NN] | Bundle into event checkout | Rider with no active membership checks out an event ticket with `AddMembership = true` | One PI covers ticket + membership; membership price added to total; on success both flip to `paid`; bundled membership ledger row carries 0 Stripe fee (the ticket absorbs the PI fee). |
| MEM21 [NN] | Bundle skipped when already a member | Active member checks out with `AddMembership = true` | No second membership row minted (`GetActive` non-null => `bundleMembership = false`); only the ticket is charged. |
| MEM22 [NN] | Standalone bundle ledger fee | Buy a membership alone on its own PI (standalone /Membership) | `OnMembershipPaid(membershipOwnsTheFee: true)` pulls the actual Stripe fee into the single ledger row; fee counted exactly once. |
| MEM23 [R] | Recent sales / dashboard | After a paid membership | Sale appears in Admin -> Purchases / dashboard via `v_recent_sales` (`kind = 'membership'`, buyer email + name joined from `users`, item name = `name_at_purchase`). |

---

## POS

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| MEM24 [NN] | Counter membership sale (cash) | Counter: look up an existing rider by email, add Membership, take cash | Membership row created with `sold_by_user_id = cashier`, flipped to `paid` inline; ledger entry written; `valid_to_utc` per duration. |
| MEM25 [NN] | Counter membership sale (card) | Same but card present / Stripe | Row stays `pending` until the webhook; PI stamped; on success flips to `paid`. |
| MEM26 [NN] | One membership per sale | Add Membership twice to the counter cart | Second add rejected server-side: "Only one membership per sale." |
| MEM27 [NN] | Quantity locked to 1 | Submit a counter cart with a membership line quantity != 1 | 400 "Memberships are sold one at a time." |
| MEM28 [NN] | POS blocked when disabled | Disable Memberships, attempt a counter membership line | 400 "Memberships aren't sold at this track." |
| MEM29 [NN] | POS needs a real rider | Counter membership for an email with no account | Rider lookup returns "No customer with that email" first (membership attaches to `rider.Id`; there is no guest membership). |
| MEM30 [NN] | Bundled POS membership fee | Counter sale mixing a membership with a pass/ticket on one card PI | Membership ledger row carries 0 Stripe fee (other gross-bearing lines absorb the PI fee); PI fee counted once. |
| MEM31 [R] | POS authz | Attempt counter checkout as a user without `SalesCounter` | 403 / forbidden. |

---

## Edge

| ID | Title | Steps | Expected |
|----|-------|-------|----------|
| MEM32 [NN] | Expiry boundary | Yearly membership whose `valid_to_utc` is in the past | `GetActive` returns null; /Membership shows the buy/renew card, not the active card. A row exactly at `now` (not strictly > now) is treated as expired. |
| MEM33 [NN] | Lifetime beats yearly | A rider holds both a paid lifetime row and a paid yearly row | `GetActive` returns the lifetime row (NULL `valid_to` sorts first). |
| MEM34 [NN] | Refund flow | Admin refunds a `paid` membership | Row -> `cancelled` (reason + `cancelled_by`) then `refunded`; Stripe refund issued per the refund path; `GetActive` drops it; History shows refunded. |
| MEM35 [NN] | Refund only from paid | Attempt to cancel a `pending` or already `failed` membership | `Cancel` no-ops (WHERE `status = 'paid'`); confirm the refund flow handles a non-paid row gracefully (no false "success"). |
| MEM36 [NN] | Duplicate webhook idempotency | Replay `payment_intent.succeeded` for the same membership PI twice | Second replay does not double-write the ledger row (unique-violation `23505` caught and logged); status stays `paid`. |
| MEM37 [NN] | Tenant isolation, status | Sign in as a rider who is a member at Tenant A; open /Membership on Tenant B (member-less) | Tenant B shows no active membership (Status is scoped by `tenant_id` + `user_id`); A's membership never bleeds into B. |
| MEM38 [NN] | Tenant isolation, admin list | `GET api/Membership/Admin` on Tenant B | Returns only B's rows; A's memberships absent (`ListForTenant` scoped by `tenant_id`). |
| MEM39 [NN] | Tenant isolation, refund | As Tenant B staff, attempt to refund a Tenant A membership id | Rejected: refund loads by id then checks `p.TenantId != tenantId` -> "Purchase not found." |
| MEM40 [NN] | Config edit does not rewrite history | Buy at $40 yearly; admin then changes name to "Pro" and price to $60 | The existing row keeps `name_at_purchase`/`price_cents = 4000`; History and admin list still show the old name/amount; only new purchases use the new config. |
| MEM41 [R] | Stripe PI uniqueness | Confirm a single membership row per PI | `idx_membership_purchase_stripe_pi` unique partial index prevents two rows sharing one `stripe_payment_intent_id`. |

---

## Known risks / watch-items
- **MEM-RISK Requirement flags are inert (money/UX gap):** `membership_required_for_riders` / `_for_spectators` are configurable in admin and persisted, but `PurchaseController` explicitly no longer gates entry/extra purchases on an active membership. A tenant who turns "required for riders" on will see no enforcement at checkout. Either wire the gate or hide the flags so admins are not misled.
- **Stale "Required for" caption on /Membership:** `User/Membership.vue` `requiredFor` reads `requiredForPass` / `requiredForEventTicket` / `requiredForSeasonPass` / `requiredForExtras`, which were removed in `Script0070` and are absent from `MembershipStatus` (now `requiredForRiders` / `requiredForSpectators`). The computed always returns empty, so the caption never renders. Update the field names if the caption is meant to show.
- **No duplicate-active guard on standalone Buy (MEM19):** the online bundle path checks `GetActive` before minting, but `MembershipController.Buy` and the POS path do not. A rider can stack multiple overlapping paid memberships (double charge). Confirm whether renew-before-expiry should extend the window or block.
- **Bundle fee attribution depends on co-purchase shape:** `OnMembershipPaid(membershipOwnsTheFee: tickets.Count == 0)` decides fee ownership from whether tickets shared the PI. A PI that bundles a membership with only extras/season-pass (no event tickets) could mis-assign the Stripe fee; verify ledger fee totals on each bundle combination (ticket+membership, extras+membership, season-pass+membership).
- **Repo methods unscoped by tenant by design:** `GetById`, `GetByPaymentIntentId`, `UpdateStatus`, `MarkRefunded`, `SetStripePaymentIntentId` key on row/PI id only. Safe because the refund path re-checks `TenantId`, but any new caller must add a tenant check before trusting the row.
- **Disable does not expire memberships (MEM9):** turning the feature off only hides the buy page; existing paid rows still read active. Confirm that is intended for mid-term-disable scenarios.
- See the **POS / Counter** plan for shared cart/cash/card mechanics and the **Events / Pricing / Registration** plan for the `AddMembership` bundle checkout surface.
