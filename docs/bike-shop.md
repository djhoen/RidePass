# Bike shop: unified retail + rentals + repairs

Status: ALL FOUR PHASES BUILT (migrations 0181-0192, applied to dev). Phase 1 catalog/inventory/
purchasing, Phase 2 retail register + refunds, Phase 3 rentals re-homed (shop_rental, window
availability, deposit holds, counter booking UI), Phase 4 work orders (estimates, parts consumed
on the bench, bill-out through the sale path, UI). Feature is a super-admin platform toggle; the
counter surfaces use their own shop.counter permission + tenant_shop_cashier role (0191).
Parity wave shipped: low-stock alerts, barcode scan, PO/receiving UI, stock takes, receipts,
and (0192) SPECIAL ORDERS + WO POLISH: a part line rides a supplier PO line (po_line_id;
receive stamps arrival, consumes for committed jobs, advances awaiting_parts to ready or
in_progress, emails the customer), repair DEPOSITS (set amount, email a public /PayDeposit/token
payment link or record cash; own shop_wo_deposit ledger entry; credited at bill-out with the
sale ledger recording only the remainder; refundable via SalesRefund), technician picker,
printable claim tag + estimate, and tips at bill-out. Deposits deliberately do NOT appear in
v_recent_sales (prepayments; the bill-out sale shows the full job).
STORE CREDIT (0193, phase 1): per-tenant customer balance (tenant_credit_account + append-only
tenant_credit_entry, floor-guarded cached balance), identity by user/email/phone so walk-ins
can hold credit. Funded by deposit overages (cashier chooses refund-or-credit at bill-out),
refunds-to-credit (refund dialog destination choice), and manual grants (Admin -> Sales ->
Store Credit page, CustomersView to see / SalesRefund to adjust). Redeemed as a TENDER at the
shop register (email/phone lookup, ShopCounter): applied up to the total, money path collects
the remainder. Accounting rule: credit never moves money and never re-charges the cut; the
sale's ledger entry books only what the PI/cash actually collected, refund-to-credit writes NO
ledger mirror (tenant keeps the cash, owes value). Partial deposit refunds are CAS-guarded
(deposit_refunded_cents); the one-refund-per-source ledger index carves out shop_wo_deposit.
Phase 2 (0194, BUILT for F&B): concession_sale takes credit as a tender (same remainder-based
money math; redeem references 'concession_sale', reversed on payment failure and on refund);
shared CreditLookupField component on both the F&B POS confirm screen and the shop register;
Credit/Lookup accepts sales.counter OR shop.counter (manual OR check, [Authorize] policies
can only AND). GATE COUNTER deliberately deferred: the counter sale has no order header (one
PI across many purchase rows, per-row ledger entries + refunds), so credit there needs either
per-row allocation columns on 4 purchase tables or a counter-order header; the header approach
is also what online checkout needs, so both land together in phase 3.
Phase 3 (0195, BUILT): checkout_credit_tender anchors credit on MULTI-ROW checkouts (gate
counter + online event checkout): per-row ledger entries stay untouched; one balancing entry
per tender (source 'credit_tender', gross -credit; net -credit only for platform-mode Stripe
rows; cash-convention and direct-mode rows get net 0) nets the books; payment_method 'credit'
joined the ledger CHECK. Gate counter takes credit via CreditLookupField (cash + card + fully-
covered paths); online event checkout offers signed-in riders a "use my store credit" checkbox
(applies after gift card, mirrors its reduce-PI/restore-on-failure mechanics); online F&B
ordering does the same on its single-sale path; GET Credit/Mine backs the offer and the rider's
balance + history card on User -> Rewards. KNOWN LIMIT: per-row refunds of credit-funded
multi-row checkouts refund off the (smaller) PI; heavy-credit orders can hit Stripe's
over-refund guard, and the credit share is restored by a manual grant, not automatically.
Phase 4 (0196, BUILT): loyalty CREDIT-BACK programs. reward_program gains reward_kind
('percent_off' voucher vs 'credit_rate'), credit_rate_bps (500 = 5% back) and
credit_qualifying_kind (any / event_ticket / concession / shop_sale). RewardEngine.
AwardCreditBack pays the rate on money actually collected (never on credit/deposit-funded
portions at single-sale surfaces; ticket carts use row amounts), once per settled purchase:
idempotent via the once-per-reference unique index (loyalty_award added) plus an advisory
lock + pre-check so double-fires don't double-email. Auto programs pay every customer,
walk-ins included (account minted from the sale's email/phone); opt-in pays joined riders.
Hooks: finalizer (tickets per-actor keyed to lowest ticket id, shop sale, concession) and
the cash paths (shop register, WO bill-out, F&B POS, gate counter). Awards >= $1 email the
customer. Admin editor has the reward-type switch; rider Rewards page shows earn-rate chips.
Awards are NOT clawed back when a purchase is refunded (manual adjust if it matters).
CUSTOMER HISTORY (BUILT): GET BikeShopRegister/CustomerHistory (CustomersView OR ShopCounter,
manual OR) returns a customer's sales + rentals + work orders + credit balance, matched by
account id, email, or phone digits (walk-ins findable by whatever they left). Shared
ShopHistoryPanel component renders it on Admin CustomerDetail (Bike Shop card, gated on
bike_shop_enabled) and in the work-order editor's history dialog (the bench's "what did we
do for this bike last time" lookup).
INVENTORY REPORTS (0197, BUILT): a Reports tab on the shop admin page with three reports plus
CSV export, served by BikeShopReportController (CatalogManage OR ReportsView so accountants
can pull them via API). Valuation: owned stock at cost + retail (serialized units counted while
available/maintenance/rented_out, each at its own acquired cost). Sales & margin: date-ranged
revenue (discounted pre-tax goods) vs COGS; Script0197 snapshots unit cost onto every new sale
line at ring-up/bill-out (serialized units use their acquired cost), with fallback to current
cost for pre-snapshot history; labor reads as pure margin; refunded sales excluded. Dead
stock: pool variants with stock but no sale inside the chosen window, with cost tied up and
last-sold date. All three queries verified by execution against seeded data.
CSV IMPORT + VARIANT MATRIX (BUILT, no migration): POST BikeShop/ImportCsv (dryRun preview
then commit; template downloadable from the dialog). One row per variant, rows sharing a
Product name become one product; categories/suppliers created by name on the fly; opening
stock writes 'adjustment' movements so imports reconcile like everything else; row-level
errors with line numbers; existing product names / SKUs / barcodes rejected, never merged;
serialized rows must import at 0 stock (units added afterward with serials). Parser verified
by an 18-check execution harness on the verbatim code (quoted fields, CRLF, money parsing,
dup detection). POST Products/{id}/GenerateVariants: size x color matrix (max 200), optional
SKU prefix (PREFIX-SIZE-COLOR), skips existing combos via the attr/SKU unique indexes.
"Import CSV" toolbar button + per-product "Size matrix" button on the shop admin Products tab.
ECOMMERCE (0198, BUILT): public /Shop storefront (browse anonymously, checkout signed-in).
Catalog endpoint ships a trimmed projection (no costs/thresholds); serialized products are
browse-only ("available in store") so unit picking stays at the counter. Orders are ordinary
shop_sale rows (order_channel 'online'): server re-prices, pass retail benefits + shop coupons
+ store credit all apply, PI settles through the normal finalizer (depletion, order number,
ledger, loyalty), and the finalizer emails the buyer their order number as the pickup claim.
Staff side: Shop Sales shows an awaiting-pickup count/filter + "Picked up" button
(picked_up_at, guarded flip verified by execution). NavBar "Shop" link gated on the feature.
Deliberate v1 limits: pay-online only (no unpaid reservations), stock depletes at payment
(same as counter card sales), pool variants only online.
CARD-PRESENT + GIFT CARDS AT THE REGISTER (0199, BUILT): the shop register connects a
WisePOS E via the same Terminal SDK flow as the F&B POS (own ShopCounter connection-token
endpoint + lazy Location provisioning); with a reader connected, card sales collect on the
reader (card-present PI for the remainder) and finalize immediately, else the Payment Element
as before. Gift cards join the tender chain (discounts -> gift card -> store credit -> money):
balance debited atomically up front, redemption row recorded per sale, restored on payment
failure (finalizer RestoreDiscountsFor), inline aborts, and refunds (value returns to the
CARD, never converted). Accounting: gift purchases book nothing, so the sale's ledger entry
recognizes the gift-funded portion as gross at redemption; the PI charges gross minus gift;
cash path books gross = cash + gift with net owing the gift funds to the tenant in platform
mode. Gift-funded spend earns loyalty like cash.
RECEIPT PRINTING (BUILT): the shop register prints ePOS receipts through the shared
ReceiptPrinter helper (same Epson TM-m30 XML flow as the F&B POS). Printer URL is per tablet
(localStorage 'shopPrinterUrl', seeded from 'concessionPrinterUrl' so an F&B tablet carries its
printer over), configured via a Printer dialog in the register header. Auto-prints on sale
complete (toast on failure, never blocks the line) with a reprint button in the done dialog.
Ring-up responses now return subtotalCents/taxCents/discountCents so the receipt shows
server-priced totals.
OLD RENTAL MODULE RETIRED (0200, BUILT): lessons re-pointed from rental_* to the shop catalog.
Lesson config (EventDialog + EventController) reads/writes shop_lesson_rentable (variant-based,
per-lesson price override; tenant-guarded INSERT..SELECT verified by execution). Online lesson
checkout (PurchaseController) and the gate counter (CounterController kind 'rental', ItemId =
shop variant id) validate against shop_lesson_rentable, check window availability
(GetPoolAvailability / GetFreeSerializedUnits), and create shop_rental rows (event_id set,
serialized units auto-picked one per line). Pricing is all-in (no rider service charge, no
per-bike waiver flag); online deposits stay manual-capture holds (sale_kind
'shop_rental_deposit_hold'); the gate counter no longer charges deposits into the sale (they
are recorded on the rental and handled at pickup). Finalizer: OnShopRentalPaid gained ownsFee
so a bike sharing the lesson PI books zero Stripe fee (tickets absorb it) and the flow
continues to ticket finalization. MyPasses reads GET /api/BikeShopRental/Mine (new, own rows
only). Pending-purchase reconciler now covers shop_sale + shop_rental (replacing
rental_purchase). Deleted: RentalController, RentalRepository + interface + RentalData,
RentalCharge, Rentals/RentalCounter/public-Rentals pages, RentalService.ts, rentalsEnabled
surfaces (tenant endpoint, branding, super-admin toggle, Features card). Script0200 removes
the 'rental' branch from v_recent_sales and zeroes rentals_enabled; rental_* TABLES remain
(expand-then-contract) and get dropped in a later release once this one is deployed.
CONDITION PHOTOS (0204, BUILT): photos on work orders AND rentals. Researched how Lightspeed
does it first: Lightspeed RETAIL (their bike shop product) allows up to 12 images per work order
and captures NO signature or formal approval at all (approval is just "Print Quote" / "Send As
Email"); their separate DMS product adds eSignature for per-job approve/decline on repair orders,
sent remotely by email or text. Photos are the cheap half of that protection and cover the same
dispute, so they came first. ONE table `shop_condition_photo` for both owners (work_order_id XOR
rental_id via a CHECK, NOT a polymorphic owner pair, so referential integrity and ON DELETE
CASCADE survive) + `stage` intake|return|progress. Rentals need BOTH ends: intake when gear goes
out, return when it comes back, because a damage capture against the deposit is exactly when
evidence is wanted. Shared BikeShopPhotoController (ShopCounter) with one upload path for both
owners: 5 MB, PNG/JPEG/WebP, capped at 12 PER STAGE (Lightspeed's number), file saved via
IImageStorage first then the row, and the file is cleaned up if the tenant-guarded INSERT..SELECT
matches no owner. Shared ConditionPhotos.vue used by the work-order editor (intake) and rentals
(a per-row photos dialog with both stages, plus return photos embedded in the Return dialog right
above the damage-to-keep field). Verified by execution: forged-tenant insert = 0 rows, both-owners
and no-owner and bad-stage all rejected, work-order delete cascades photos away.
PHONE PHOTO CAPTURE VIA QR (BUILT, no migration): the bench workflow is start the job on the
counter screen, shoot the photos on a phone. A "Take photos on your phone" QR (PhotoQrPanel) sits
in the work-order editor and on both rental surfaces (photos dialog + the Return dialog); it
encodes an ABSOLUTE deep link to /Admin/BikeShop/Photos/{work-order|rental}/{id}
(BikeShopPhotoCapture.vue, phone-first, shows which record it landed on before shooting).
DELIBERATELY NOT an upload token: the route is an ordinary authenticated admin route
(requiresAuth + shop.counter), the router already bounces to /Login?next= and Login already
guards against off-site next values, and auth persists in localStorage, so a phone signs in ONCE
and later scans open straight through. That keeps zero unauthenticated write paths and nothing
sensitive in a QR that may sit on a bench. Added GET BikeShopRental/Rentals/{id} (ShopCounter),
which the repo supported but no endpoint exposed, so the capture page can name the rental.
SERVICE FOLLOW-ON BOARD (from a Lightspeed DMS comparison, 2026-07-19)
Lightspeed splits into two products and we chased the wrong one for service. RETAIL is the bike
shop POS (12 work-order images, no signature, approval = print/email a quote). DMS is their
POWERSPORTS DEALERSHIP system, which is much closer to an MX track's shop. What DMS has that we
don't, ranked by fit:

1. STANDARD JOB TEMPLATES (BUILT, Script0208). DMS saves "job titles, descriptions, required parts,
   labor hours, and specific rates into an instantly accessible library", filterable by year /
   make / model. An MX shop repeats the same jobs endlessly (fork seals, top end, suspension
   service, tire + mousse), and today every work order is typed from scratch. Cheapest big win,
   and it makes the estimate + authorization flow materially faster to use.
   BUILT: shop_job_template + _line mirroring shop_work_order_line's shape (labor carries a
   description, part points at a variant; the same CHECK restated so a malformed template can't
   exist and then fail at apply time). Library managed on a "Saved jobs" tab (CatalogManage);
   applied from an "Add saved job" menu above Labor & parts in the work-order editor
   (ShopCounter). KEY BEHAVIOR: a part line's price is normally NULL and resolves to the
   variant's CURRENT sale price at apply time, so a job saved last season doesn't quote last
   season's prices; a pinned price wins when set. A part whose variant was since DEACTIVATED is
   SKIPPED and NAMED back to the counter rather than quoted at zero, because a silently missing
   part is how a job gets underbilled. Names are unique per tenant case-insensitively. Verified
   by execution: live-price resolution, pinned-price override, deactivated-variant skip,
   cross-tenant read = 0 rows, duplicate name rejected.
2. SERVICE REMINDERS + READY-FOR-PICKUP NOTICES (BUILT, Script0209). DMS texts on "Ready to Cashier", then a 30-day
   satisfaction check and 90-DAY SERVICE REMINDERS. We email on deposit request and parts arrival
   but never say "your bike is ready" and never invite them back. The reminder is recurring
   revenue and we already have email + SMS + notification infrastructure.
   BUILT: READY NOTICE fires when a work order first reaches 'ready' (email, plus SMS when the
   tenant has Twilio configured and we hold a mobile), showing any balance due at pickup. It is
   CLAIMED once via `UPDATE ... WHERE status='ready' AND ready_notified_at IS NULL`, so bouncing
   the status around can't re-notify, and the send is best-effort so a mail failure never fails
   the status change staff just made. SERVICE REMINDER is scheduled at pickup as
   now() + tenant.shop_service_reminder_days (DEFAULT 0 = OFF, so a track opts in rather than
   discovering it mailed its customers), swept every 6h by ShopServiceReminderWorker. The sweep
   claims each reminder BEFORE sending (a duplicate months later is worse than a miss), honours
   marketing suppression, and self-clears rows for tenants that since turned the shop or
   reminders off. Settings live on a new "Service" tab on the shop admin page, which is also
   where shop-supply fees (item 3) belong. PER-CHANNEL TENANT CONTROL (Script0210): the ready
   notice ships as two separate switches, `shop_ready_notify_email` (DEFAULT TRUE: transactional,
   free, the customer is waiting on it) and `shop_ready_notify_sms` (DEFAULT FALSE: every text
   bills the tenant, so it must never switch itself on merely because Twilio is configured for
   the gate). Note the asymmetry against the reminder's default-OFF: a reminder months later is
   marketing-adjacent, a ready notice is not. With both channels off the notice returns BEFORE
   claiming, so enabling a channel later doesn't find the once-only claim already spent. All
   three settings save through one PUT Tenant/ShopNotifications because they are one screen. Verified by execution: claim-once returns 1 then 0,
   90-day scheduling lands 90 days out, a not-yet-due row is invisible to the sweep, a backdated
   one appears, and claiming removes it.
3. SHOP SUPPLY / DISPOSAL FEES (BUILT, Script0211). DMS auto-computes "shop supplies, hazardous waste, freight,
   shipping" as a PERCENTAGE OF LABOR with customizable caps. Small feature, real money; MX shops
   burn solvent, chain lube, and oil disposal. Tenant setting + a computed line.
   BUILT: shop_supply_fee_bps / _cap_cents / _label on tenant, DEFAULT 0 bps = OFF so no existing
   tenant silently starts adding a line. Charged on LABOR ONLY, deliberately: a percentage of a
   $900 fork tracks how expensive the part was rather than the consumables the job burned, and
   that is the line customers notice. Added at bill-out as its OWN untaxed sale line (variant_id
   null, no frozen cost, pure margin like labor) so the customer can see what it is instead of
   finding it buried in a labor rate. Settings on the Service tab with a worked example.
   Verified with a harness mirroring the expression: 5% of $120 = $6.00, cap bites on a big job
   and not a small one, 0 bps off, a parts-only job pays nothing, half-away-from-zero rounding,
   and a $0 cap disables it.
4. REPAIR-ORDER AGING (BUILT, no migration). Their "Outstanding RO by Age" report exists so "out-of-sight jobs never
   slip through the cracks". Our WO list has no aging view, and a bike waiting three weeks on
   parts is exactly what gets forgotten. Nearly free.
   BUILT: an Age column on the work order list, amber at 7 days and red at 14, plus the promised
   date turning red once it has passed. Closed orders (picked_up / cancelled) stop aging rather
   than reporting "146 days old". Thresholds are hardcoded on purpose: a forgotten bike should be
   visible without a tenant configuring anything first.
5. JOBS WITHIN A REPAIR ORDER + PER-JOB APPROVE/DECLINE (bigger, structural). This is what their
   eSignature actually approves: an RO holds several JOBS and the customer accepts or declines
   each. Our lines are flat, so a customer can only take or refuse the whole thing. Unlocks
   DECLINED-WORK TRACKING, which DMS frames as turning overlooked maintenance into revenue.
   Touches estimate, signature, and bill-out. Do it when the shop outgrows flat lines.
6. TECHNICIAN TIME CLOCKING + EFFICIENCY. DMS clocks techs per job line and compares "clocked
   actual hours against flat-rate billed hours". We have ONE tech per work order and no time at
   all. Worth it once a track employs more than a mechanic or two.

DELIBERATELY SKIPPED: warranty claim management, manufacturer parts catalogs (Parts Manager Pro
/ RV Partfinder), and the service-bulletin + recall feed all assume a franchise relationship a
track shop doesn't have. Service tech VIDEO (5-minute bay walkarounds) is appealing but means
video storage + transcoding, and condition photos already cover most of the dispute case.

Remaining: old rental_* table DROP deferred to a future cleanup migration.

## The decision

One module, not two (or three). A bike shop that sells, rents, and repairs is unified by its
**inventory**: one physical bike can be sold, rented and returned, or taken in for service, and
often moves between those over its life. Splitting sales from rentals means two catalogs and two
stock counts for the same bike, and the first time you rent a unit you already sold, the split has
cost real money.

So the architecture is a shared **catalog + inventory core**, with three **transaction types**
layered on top: retail sale, rental, and repair work order. Not one undifferentiated blob, and not
three silos.

### What happens to the existing rental system

The current `rental_*` tables are well designed (pool-vs-serialized tracking, serial/condition on
units, half-open time-window reservations, deposit pre-auth with manual capture, lesson wiring).
But prod has **zero rental rows and zero tenants with rentals enabled**: it was built and never
adopted. With no data to migrate, we keep the *design* and scrap the *shape*: the rental-first
tables get replaced by transaction-neutral ones, and the good rental logic is re-homed onto them.

This does collide with in-flight, uncommitted rental work (Script0177 lessons + time-scoped
rentals, Script0179 deposit ledger, a modified `RentalController`). Per the agreed sequencing we
**absorb** that work into the unified design rather than ship it as-is. Its logic is carried over;
its tables are not.

---

## Layer 1: Catalog (what you offer)

```
shop_category            -- department: Bikes, Apparel, Parts, Service. Optional parent_id (flat is fine to start).
  id, tenant_id, name, parent_id?, sort_order, is_active

shop_product             -- a catalog entry. The neutral replacement for rental_product.
  id, tenant_id, category_id?, name, description, brand?, image_url?,
  is_sellable  bool,     -- can be rung up at the register
  is_rentable  bool,     -- can be booked for a window
  tax_category_id?,      -- NULL = tenant default
  supplier_id?,          -- for reorder/COGS (Phase 4)
  is_active, sort_order

shop_variant             -- the SKU. Price and stock live HERE, matching concession_variant + event_extra_variant.
  id, tenant_id, product_id,
  sku?, barcode?,        -- scannable at the register / receiving
  size?, color?, gender?,-- attribute columns (frozen onto sale lines at purchase, per event_extra_variant)
  sale_price_cents?,     -- when sellable
  daily_rate_cents?,     -- when rentable
  deposit_cents,         -- rental damage deposit (pre-auth), default 0
  cost_cents?,           -- last/avg cost, for margin + COGS
  tracking_kind,         -- 'pool' | 'serialized'  (from rental_product.tracking_kind)
  stock_on_hand int,     -- cached; authoritative movements live in shop_stock_movement
  is_active
```

Every product has at least one variant (a default). This is the concessions/extras pattern: the
product is the marketing object, the variant is the thing with a price and a stock count. A bike you
both rent and sell is one variant carrying both `sale_price_cents` and `daily_rate_cents`.

## Layer 2: Inventory (what you physically have)

Two tracking modes, exactly the split the rental system got right, now serving all three
transaction types:

- **pool** - `shop_variant.stock_on_hand` is the count. Helmets, tubes, apparel, repair parts.
- **serialized** - one `shop_item` row per physical unit. Bikes. Replaces `rental_item`.

```
shop_item                -- a distinct physical unit. Replaces rental_item.
  id, tenant_id, variant_id, label, serial?, notes?,
  status,                -- 'available' | 'rented_out' | 'sold' | 'maintenance' | 'retired'
  created_at, updated_at
```

The status enum widens from the rental version (`available/maintenance/retired`) to cover the whole
life: a serialized unit can now be `sold` (retail) or `rented_out` (on a rental), not just in/out of
the rental pool.

### The audit backbone (the Lightspeed part rentals lacked)

Concessions has two stock models: sellable items are "cap minus quantity sold" with **no receiving
and no audit trail**, and only its behind-the-counter ingredient model has true decrements,
receiving, stock takes, and low-stock alerts. A real bike shop needs the second kind for everything.

```
shop_stock_movement      -- append-only ledger. Every change to stock writes a row here.
  id, tenant_id, variant_id, item_id?,
  delta int,             -- signed: +5 received, -1 sold, -1 rented out, +1 returned
  reason,                -- 'receive' | 'sale' | 'rental_out' | 'rental_return' |
                         -- 'repair_consume' | 'adjustment' | 'stocktake' | 'transfer'
  reference_kind?, reference_id?,   -- the sale / rental / work_order / PO that caused it
  unit_cost_cents?,      -- for receiving and COGS
  created_by_user_id?, created_at
```

`stock_on_hand` on the variant stays as a cached read value; every mutation writes a movement row
AND updates the cache in the same transaction (mirroring concession `ReceiveStock` /
`DepleteInventoryForSale`, but with an audit row so you can answer "why is this 3 not 5?"). Stock
takes reconcile the cache against a physical count.

Purchasing is part of the foundation (Lightspeed parity): `shop_supplier`, `shop_purchase_order` +
`shop_po_line` (receiving at cost, updating each variant's `cost_cents` and writing `receive`
movements; receiving a serialized line mints `shop_item` rows). Physical counts follow close behind:
`shop_stock_count` + `shop_stock_count_line` (variance against the cached `stock_on_hand`, mirroring
`concession_inventory_count`).

## Layer 3: Transactions

### Retail sale (new build, but the register already exists)

```
shop_sale
  id, tenant_id, buyer_user_id?, buyer_email, buyer_name,   -- walk-ins have no account
  status,                -- 'pending' | 'paid' | 'failed' | 'refunded'
  subtotal_cents, discount_cents, tax_cents, tip_cents?, total_cents,
  payment_method, stripe_payment_intent_id?, stripe_connected_account_id?,
  order_number?, sold_by_user_id?, receipt_token, created_at, updated_at

shop_sale_line
  id, sale_id, variant_id, item_id?,      -- item_id set when a serialized unit sells
  quantity, name_snapshot, variant_label, -- frozen so catalog edits never rewrite history
  unit_price_cents, discount_cents, tax_cents, tax_rate_bps
```

On `paid`: decrement pool stock or flip the serialized item to `sold`, write a `shop_stock_movement`
(`reason='sale'`), assign an order number, and write the ledger entry. This is the concessions sale
flow with the kitchen/fulfillment layer removed.

### Rental (re-homed from rental_purchase, design preserved)

```
shop_rental
  id, tenant_id, renter_user_id?, renter_email, renter_name,
  waiver_signature_id?,
  starts_at, ends_at,          -- half-open [start, end), the single availability source (Script0177)
  status,                      -- pending | paid | out | returned | damaged | cancelled | failed
  amount_cents, service_charge_cents,
  deposit_cents, deposit_pi_id?, deposit_captured_cents,   -- pre-auth, manual capture (Script0048 + 0179)
  rental_pi_id?, checked_out_at?, returned_at?, condition_notes?,
  event_id?,                   -- set when booked as part of a lesson (Script0177)
  redemption_token, created_at, updated_at

shop_rental_line             -- what's on the rental; assigns pool qty or serialized units for the window
  id, rental_id, variant_id, item_id?, quantity,
  daily_rate_cents_frozen, days_count
```

This carries over verbatim: the half-open window model, the deposit pre-auth and its ledger fix
(Script0179's "held deposit is float, captured damage is revenue"), the checkout/return lifecycle,
and the lesson wiring. `event_rental_eligibility` becomes `shop_lesson_rentable
(event_id, variant_id, price_cents_override?)`.

### Repair / work order (genuinely new, no precedent)

```
shop_work_order
  id, tenant_id, customer_user_id?, customer_name, customer_phone?,
  subject_item_id?,            -- the shop's own bike, when servicing fleet
  customer_bike_desc?,         -- free text, when it's the customer's own bike
  status,                      -- intake | awaiting_parts | in_progress | ready | picked_up | cancelled
  assigned_tech_user_id?, intake_notes?,
  sale_id?,                    -- billed out as a shop_sale on pickup
  created_at, updated_at

shop_work_order_line
  id, work_order_id, line_kind,         -- 'labor' | 'part'
  -- labor: description, price_cents (flat, or hours * rate resolved at entry)
  -- part:  variant_id, quantity  (consumed from stock -> shop_stock_movement reason='repair_consume')
  description?, variant_id?, quantity?, unit_price_cents, tax_cents, tax_rate_bps
```

A work order accrues labor and parts, then bills out as a `shop_sale` when the customer picks up
(so payment, tax, ledger, and refunds all go through the one sale path). Parts consumed decrement
stock the moment they're added to the job, not at pickup, so inventory reflects reality on the
bench.

## Layer 4: Shared / integration

- **Register**: reuse the concessions POS machinery near-verbatim - Stripe Terminal (WisePOS E)
  plus cash, per-tenant-per-local-day order numbers, manager-PIN-gated refunds. A retail register is
  that POS minus the kitchen screen.
- **Payment routing**: `IChargeRouter.Plan(...) -> ChargePlan` reused as-is. Direct-charge tenants
  charge on their connected account with the service fee as the Stripe application fee; the sale row
  snapshots `stripe_connected_account_id` so refunds hit the right account.
- **Finalizer**: add `IShopRepository` lookups + a dispatch block in
  `StripePurchaseFinalizer.ProcessPaymentIntentAsync` (mirror the concession block): on succeeded,
  flip to paid, deplete stock, assign order number, insert the ledger `sale` entry (idempotent via
  the `(tenant_id, source_kind, source_id)` unique index, catching 23505).
- **Ledger SourceKind**: add `'shop_sale'`, `'shop_rental'`, `'shop_repair'`, and `'shop_deposit'`
  (mirroring `rental_deposit`) to the `tenant_ledger_entry` CHECK. Distinct kinds keep sale vs rental
  vs repair as separate revenue lines in reporting.
- **v_recent_sales**: add a `UNION ALL` branch per shop transaction kind (the module is invisible to
  the dashboard and Admin -> Purchases until this exists - the `recent-sales-view` radar enforces it).
- **Tax**: reuse the concession per-line pattern - `shop_tax_category (rate_bps, is_default)`,
  per-product `tax_category_id`, a `prices_include_tax` tenant flag, and the `ComputeLineTax`
  exclusive/inclusive helper. (Future: unify the concession and shop tax-category tables; separate
  for now.)
- **Discounts**: two mechanisms. Customer promo codes extend `coupon.applicable_scope` with a
  `'shop'` value. Counter staff get a concessions-style manager-gated quick discount. And critically,
  **season pass benefits apply here**: the `season_pass_benefit` model already reserves `'rental'` and
  (future) `'retail'` benefit types, so a pass that grants "10% off rentals" or "15% off the bike
  shop" is read and applied at shop checkout. That is the payoff of the benefits work already done.
- **Feature flag**: `tenant.bike_shop_enabled`, replacing `rentals_enabled` (migrate the flag value
  forward).

## Existing -> new mapping

| Existing (rental-shaped)        | New (unified)                                    |
|---------------------------------|--------------------------------------------------|
| `rental_product`                | `shop_product` + `shop_variant` (price/stock/rate on variant) |
| `rental_item`                   | `shop_item` (status gains `sold`, `rented_out`)  |
| `rental_purchase`               | `shop_rental`                                    |
| `rental_purchase_item`          | `shop_rental_line`                               |
| `event_rental_eligibility`      | `shop_lesson_rentable`                           |
| `rental_purchase.event_id`, sync-window trigger, deposit ledger (0179) | carried onto `shop_rental` |
| `tenant.rentals_enabled`        | `tenant.bike_shop_enabled`                       |
| coupon/ledger `source_kind='rental'` | kept for history; new `shop_*` kinds added  |

## Build order

Rentals and repairs both sit on the catalog + inventory core, so that lands first even though all
three are in scope.

1. **Catalog + inventory + purchasing foundation** (schema): `shop_category/supplier/product/variant/
   item/stock_movement/purchase_order/po_line` + the `bike_shop_enabled` flag. The data model
   everything else builds on. *This is the first migration.*
2. **Retail register**: the reused Terminal + cash + order-number + refund + ledger machinery behind
   a separate bike-shop POS UI, tax, and the `v_recent_sales` branch. This is "sell bikes and
   equipment." Receiving against POs feeds stock here.
3. **Rentals re-homed**: `shop_rental` absorbing `rental_purchase` + lessons + deposits. Retire the
   `rental_*` tables and the in-flight 0177/0179 work into this.
4. **Repairs**: `shop_work_order`, billing out through the sale path.
5. **Counts + benefits**: stock takes, season-pass benefit application at shop checkout (`rental` /
   `retail` benefit types), buddy-pass coupon generation.

## Settled decisions (2026-07-16)

1. **Repairs serve both.** A work order's subject is either a `shop_item` (your fleet) or free-text
   `customer_bike_desc` (the customer's own bike). Neither is "primary"; both are first-class.
2. **Full purchasing, Lightspeed parity.** Suppliers, purchase orders, and receiving are in from the
   start (not a Phase 4 nicety). Stock takes / physical counts follow close behind. "If Lightspeed
   supports it, we want it."
3. **Walk-ins allowed.** `buyer_user_id` is nullable; a walk-in is a name (and optional phone) with no
   account. App users can still be attached when known.
4. **Per-variant tracking, no forced default.** Each variant is `pool` or `serialized` by explicit
   choice. Bikes tend to serialized, consumables to pool, but nothing is defaulted by category.
5. **Separate UI.** The bike shop gets its own POS screen and admin surface, NOT bolted onto the
   concessions register. It reuses the register *backend* machinery (Terminal, cash, order numbers,
   refunds, ledger, charge routing) but is a distinct set of Vue views and its own controller.

### Hard boundary: catalogs never mix

Bike shop inventory must never appear as an option in F&B or add-ons, and concession items / event
extras must never appear in the bike shop. This is structural: `shop_*` is its own catalog, with no
FK from `concession_*` or `event_extra_*` into it (or the reverse), and no shared product picker.
The only thing that legitimately spans them is a **season pass benefit**, which discounts a surface
at that surface's own checkout without ever pulling one catalog's items into another.

---

## Lightspeed parity (researched 2026-07-17)

Lightspeed Retail is the dominant bike-shop POS (from $89/mo + 2.6% + 10c card-present; ecommerce
and NuORDER vendor catalogs bundled). The goal is match-or-beat. Feature map from their bike-shop
vertical, scored against what we've built.

### Where we already match

| Lightspeed | Us |
|---|---|
| Serialized inventory (serial numbers per unit) | `shop_item` — plus a full life status (sold/rented_out/maintenance/retired) |
| Variants (size/color matrices) | `shop_variant` size/color/gender + SKU/barcode, unique attr combos |
| Purchase orders, receiving, reorder from vendors | `shop_purchase_order` + receiving at cost (mints serialized units) |
| Per-line tax, tax categories | `shop_tax_category` + per-product override, frozen per-line snapshots |
| Register: cash + card, refunds, receipts-by-number | Register backend + POS UI; refunds with cashier-decided restock |
| Inventory audit trail | `shop_stock_movement` append-only ledger (LS logs are shallower) |

### Where we beat them (platform advantages to lean on)

- **Rentals.** LS native rentals are thin (most shops bolt on bike.rent Manager, a separate
  subscription). Our absorbed rental design — half-open time windows, deposit pre-auth with
  manual damage capture, lesson/event wiring — is stronger than their native offering out of the box.
- **One platform with the track.** Season-pass benefits ('rental'/'retail' types) discount at the
  shop register; the shop shares the tenant's Stripe, ledger, payouts, reporting, and rider
  accounts. LS can't see the track side at all.
- **Pricing.** Included in RidePass vs. $89+/mo plus separate rental software.

### Gaps, ranked (what "match/beat" requires)

1. **Work orders / service** — LS's killer bike-shop feature and our biggest gap. Their shape:
   labor items with store labor rates (time x rate, or flat override), parts consumed from
   inventory, **estimate** status before committing, technician assignment, status pipeline,
   **email payment requests for deposits**, tips on repairs. Our Phase-4 `shop_work_order` design
   covers labor + parts + billing through the sale path; add estimates, deposits (payment request),
   and technician assignment to that design.
2. **Special orders** — sell a part you don't stock: order line -> tied to a PO -> customer deposit
   -> notify on arrival. Not in our design yet; belongs in the work-order phase (a work order
   waiting on parts IS a special order).
3. **Reorder points / low-stock alerts** — LS has smart thresholds. Concessions already has this
   exact pattern (`low_stock_threshold` + `MarkAndGetNewlyLowStock` + notification); copy it onto
   `shop_variant`. Cheap win.
4. **Bulk import + matrix creation** — LS one-click imports vendor catalogs (NuORDER: UPC, MSRP,
   images) and generates size/color matrices in one step. Realistic near-term: CSV product import +
   a "generate variants from size list" helper in the product dialog. NuORDER-level vendor catalog
   integration is a non-goal for v1.
5. **Barcode scanning at the register** — we store `barcode`/`sku` with unique indexes but the POS
   search doesn't resolve an exact scan yet. Wire scanner input (exact barcode/SKU match -> add to
   cart). Cheap win.
6. **Customer purchase/service history** — LS profiles show full history. We have rider accounts +
   walk-ins; needs a shop-customer view (sales + future work orders per customer) — extend the
   existing admin CustomerDetail.
7. **Shop ecommerce** — LS bundles an online store. Our per-tenant public site is the natural home
   for a rider-facing shop page (catalog + buy online / reserve for pickup). Phase after repairs.
8. **Loyalty / marketing** — RidePass already has rewards + email campaigns; wire shop purchases
   into them (integration, not new build).
9. **Inventory reporting** — sales land in `v_recent_sales` + ledger today; add inventory
   valuation / COGS / margin reports (data already captured: variant `cost_cents`, movement
   `unit_cost_cents`).
10. **Discounts at the register** — coupon scope `'shop'` + a manager-gated quick discount
    (concessions pattern). Also where season-pass benefits surface at the shop register.
11. **Tips on repairs** — `tip_cents` column already exists on `shop_sale`; expose at payment when
    work orders land.

Deliberate non-goals for now: multi-location inventory (tracks have one shop), layaways,
NuORDER-style live vendor catalogs, social/marketplace selling.

### Revised sequencing

Rentals absorption stays next (it's our beat-them card). Then **work orders + special orders +
estimates** (closes the biggest LS gap), folding in the cheap wins (low-stock thresholds, barcode
scan, register discounts) along the way. Then customer history, shop ecommerce, and inventory
reports.
