# QA Results: Memberships

Verified by static trace against current code (no live browser). No recent membership change: the duplicate-active guard and `required_for_*` enforcement remain OPEN, so cases asserting those are graded against the still-open state (matching the plan's MEM-RISK notes).

## Admin (settings)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| MEM1 | PASS | Features toggle calls `updateSettings` preserving name/price/duration/flags (Admin/Settings/Features.vue:145-151); endpoint at MembershipController.cs:165-179. |
| MEM2 | PASS | Form + preview mirror name/price/"Yearly . valid 365 days" (Admin/Settings/Membership.vue:16-72); persisted via `UpdateMembershipSettings`. |
| MEM3 | PASS | UI saves `Math.round(priceDollars*100)` (Membership.vue:122) and reads `/100` (:108); round-trips to whole dollars. |
| MEM4 | PASS | Duration/valid_to frozen on the row at buy (MembershipController.cs:107,120-121); config change is forward-only. |
| MEM5 | PASS (with caveats) | `UpdateSettings` persists `RequiredForRiders/Spectators` (MembershipController.cs:170-177). CAVEATS: (1) admin Membership.vue exposes NO UI control for these flags (form fields exist but no input, Membership.vue:91-92), so they can only change via API/Features defaults; (2) no checkout gate consumes them (MEM-RISK); (3) the /Membership `requiredFor` caption reads removed fields `requiredForPass/EventTicket/SeasonPass/Extras` (User/Membership.vue:151-154) absent from `MembershipStatus` (now `requiredForRiders/Spectators`), so it never renders. |
| MEM6 | PASS | `[Authorize(Policy = SettingsManage)]` on Settings (MembershipController.cs:165). |
| MEM7 | PASS | DTO `[Required]` name, `[Range(0,10_000_000)]` price (negatives rejected), `RegularExpression("^(one_time|yearly)$")` duration (MembershipDtos.cs:55-60); client `canSave` blocks blank name/negative price (Membership.vue:100-104). |
| MEM8 | PASS | `ListForAdmin` (SalesView) returns tenant rows newest-first (MembershipController.cs:182-200; MembershipRepository.cs:100-107). |
| MEM9 | PASS | Disabled tenant => Status `enabled=false` and UI "doesn't sell memberships" (User/Membership.vue:9-12); `GetActive` ignores the enabled flag (MembershipRepository.cs:76-89). |

## User (buy)

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| MEM10 | PASS | `Status` is anonymous-friendly; config returned, `Active` null when not signed in (MembershipController.cs:39-86). |
| MEM11 | PASS | Buy pending `amount=price`, yearly `valid_to=now+365` (MembershipController.cs:107,114-128); webhook flips paid (StripePurchaseFinalizer.cs:147-154). |
| MEM12 | PASS | one_time => `validTo=null` (MembershipController.cs:107); UI shows "Lifetime" (User/Membership.vue:25). |
| MEM13 | PASS | `AmountCents = price`, `RiderServiceChargeCents = 0` (MembershipController.cs:112,159-160). |
| MEM14 | PASS | Disabled/zero-price => "Memberships aren't sold at this track." (MembershipController.cs:97-100). |
| MEM15 | PASS | `MembershipPriceCents <= 0` rejected (MembershipController.cs:97-100). |
| MEM16 | PASS | Buy is `[Authorize]` (MembershipController.cs:88). |
| MEM17 | PASS | `payment_intent.payment_failed` flips pending to `failed` (StripePurchaseFinalizer.cs:156-159); no ledger row written on failure. |
| MEM18 | PASS | History filters to `paid`/`refunded` (User/Membership.vue:158-159). |
| MEM19 | PASS (no guard, as documented) | `MembershipController.Buy` has NO active-membership check, so a duplicate paid row can be minted (double charge); `GetActive` returns lifetime-first/latest-expiry (MembershipRepository.cs:80-88). Matches MEM-RISK. |
| MEM20 | PASS | Bundle mints one pending membership folded into the combined PI (PurchaseController.cs:710-734); webhook `membershipOwnsTheFee: tickets.Count==0` => 0 fee when a ticket shares the PI (StripePurchaseFinalizer.cs:154,265-269). |
| MEM21 | PASS | `bundleMembership = existing is null` via `GetActive` (PurchaseController.cs:365-371); active member mints nothing. |
| MEM22 | PASS | Standalone membership PI has no tickets => `membershipOwnsTheFee=true`, pulls actual Stripe fee once (StripePurchaseFinalizer.cs:154,269). |
| MEM23 | PASS | `v_recent_sales` membership branch joins users, item = name_at_purchase (Script0080_RecentSalesView.sql:97-108). |

## POS

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| MEM24 | PASS | Cash: row `sold_by_user_id=cashier`, flipped paid inline, ledger written, valid_to per duration (CounterController.cs:656-687,693-719). |
| MEM25 | PASS | Card: row stays pending, PI stamped (CounterController.cs:809-810), webhook flips paid. |
| MEM26 | PASS | Second membership line => "Only one membership per sale." (CounterController.cs:390-392). |
| MEM27 | PASS | Quantity != 1 => "Memberships are sold one at a time." (CounterController.cs:386-388). |
| MEM28 | PASS | Disabled/zero-price => "Memberships aren't sold at this track." (CounterController.cs:382-384). |
| MEM29 | PASS | Rider lookup returns "No customer with that email." (CounterController.cs:90-94); sale loads rider by id and attaches membership to `rider.Id` (:211-213,664). |
| MEM30 | PASS | Card webhook `membershipOwnsTheFee: tickets.Count==0` (StripePurchaseFinalizer.cs:154): ticket+membership => membership 0 fee; extras+membership => extras carry 0 (extrasOwnTheFee false, :178-179) and membership owns fee; counted once. NOTE: counter cart has no `season_pass` kind (CounterController.cs:234,319,380), so the pass+membership double-count flagged in MEM-RISK is not reachable via POS. |
| MEM31 | PASS | Class-level `[Authorize(Policy = SalesCounter)]` (CounterController.cs:26). |

## Edge

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| MEM32 | PASS | `GetActive` requires `valid_to_utc > now` (strict) (MembershipRepository.cs:85); a row at exactly now is expired. |
| MEM33 | PASS | `ORDER BY valid_to_utc IS NULL DESC, valid_to_utc DESC` => lifetime first (MembershipRepository.cs:86). |
| MEM34 | PASS | Refund: `Cancel` (paid only) + `MarkRefunded` + Stripe refund (PurchaseController.cs:1302-1304,1264-1277); status `refunded` drops from `GetActive`. |
| MEM35 | PASS | Refund rejects non-paid: "Only a paid purchase can be refunded." (PurchaseController.cs:1238-1239); `Cancel` is `WHERE status='paid'` (MembershipRepository.cs:38). |
| MEM36 | PASS | Replay guarded by `membership.Status == "pending"` (StripePurchaseFinalizer.cs:148) and ledger 23505 catch (:289-292). |
| MEM37 | PASS | `GetActive` scoped by tenant_id + user_id (MembershipRepository.cs:82-84). |
| MEM38 | PASS | `ListForTenant` scoped by tenant_id (MembershipRepository.cs:100-107). |
| MEM39 | PASS | Refund loads by id then `p.TenantId != tenantId` => "Purchase not found." (PurchaseController.cs:1220-1221). |
| MEM40 | PASS | Frozen `name_at_purchase`/`price_cents`/`amount_cents` on the row (MembershipController.cs:118-123; Create at MembershipRepository.cs:48-61). |
| MEM41 | PASS | Unique partial index `idx_membership_purchase_stripe_pi` (Script0058_Memberships.sql:62-63). |

Counts: PASS 41, FAIL 0, NEEDS-LIVE 0, N/A 0. (MEM5 PASS carries caveats: no admin UI toggle, inert gate, stale /Membership caption.)
</content>
