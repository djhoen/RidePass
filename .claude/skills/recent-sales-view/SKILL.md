---
name: recent-sales-view
description: Keep the `v_recent_sales` Postgres view in lockstep with the per-kind purchase tables. TRIGGER when adding a new purchase-shaped table (anything that records a sale with tenant_id + status + amount_cents + a Stripe PaymentIntent), when adding a new RidePass.Migrator/Scripts migration that creates a `*_purchase` table or `gift_card`-style sale row, or when renaming/dropping a column on an existing table that v_recent_sales selects. Surfaces a one-paragraph reminder + a ready-to-paste UNION ALL branch. Does NOT modify the view automatically.
---

# recent-sales-view

`v_recent_sales` (defined in `RidePass.Migrator/Scripts/Script0080_RecentSalesView.sql`) is the unified read model across every per-kind purchase table. The admin dashboard's Recent Purchases panel, the Admin → Purchases list, and any future cross-cutting sales feature reads from it. If a new purchase kind ships without a UNION ALL branch in this view, **it silently disappears from those screens** — exactly the bug that prompted the view's creation.

This skill exists so that never happens again.

## When to surface the reminder

Before your end-of-turn summary, when **any** of these holds:

1. You created a new migration in `RidePass.Migrator/Scripts/` that adds a `*_purchase` table (or any table that stores a sale — has `tenant_id`, `status`, `amount_cents`, and a `*_payment_intent_id` column).
2. You added a new repository under `Services/Repositories/` that has both a `Create` method writing to a sale-shaped table AND a `GetByStripePaymentIntentId`-style lookup (so the webhook handler will be wired to it).
3. You changed a column referenced by the view on one of the existing seven tables: `pass_purchase`, `event_ticket_purchase`, `event_extra_purchase`, `season_pass_purchase`, `membership_purchase`, `gift_card`, `rental_purchase` — specifically renaming/dropping any of `tenant_id`, `status`, `amount_cents`, `purchaser_user_id`/`user_id`/`buyer_user_id`, `purchaser_email`/`buyer_email`, `purchaser_name`/`buyer_name`, `stripe_payment_intent_id`/`rental_pi_id`, `created_at`, or the item-name FK column.
4. You added a new column to one of those tables that the view should also expose (e.g., a category, a refund flag, etc.). Surface as a "consider adding to v_recent_sales" prompt — the user decides.

Stay silent when:

- The migration only touches indexes, comments, RLS policies, or seed data on an existing tracked table.
- The new table is sale-adjacent but not a sale itself (e.g., `gift_card_redemption`, `coupon_redemption`, `tenant_ledger_entry`, `tenant_payout`). Those summarise or trace sales — they don't replace a row in the unified view.
- The change is to a view, function, or trigger that doesn't change column shape.

## Suggestion shape

Append to your end-of-turn summary, two sentences plus the patch:

> **Update `v_recent_sales`?** This change adds `<new_kind>_purchase` (or renames `<old_col>` → `<new_col>` on `<table>`). The view in `RidePass.Migrator/Scripts/Script0080_RecentSalesView.sql` currently doesn't know about it, so these sales will be invisible on the admin dashboard's Recent Purchases panel and the Admin → Purchases list. Suggested branch:
>
> ```sql
> UNION ALL
> SELECT '<kind>'::text,
>        x.id, x.tenant_id, x.status, x.amount_cents,
>        x.purchaser_user_id, x.purchaser_email, x.purchaser_name,
>        x.stripe_payment_intent_id,
>        <item_name_expression>,
>        x.created_at
> FROM <table> x
> [LEFT JOIN <product_table> p ON p.id = x.<product_fk>]
> ```

Then write a **new migration** (next four-digit number) that `CREATE OR REPLACE VIEW v_recent_sales AS …` with every existing branch plus the new one. Don't edit Script0080 in place — view migrations are append-only, just like every other schema change.

## Required columns the new branch MUST produce

Order matters in `CREATE OR REPLACE VIEW` — all branches must yield the same column list in the same order:

1. `kind` text — short slug, e.g. `'rental'`, `'membership'`
2. `id` uuid — the purchase row's primary key
3. `tenant_id` uuid — MUST come from the new table directly, not a join
4. `status` text — the row's status; if your kind uses a different enum, map it to `pending|paid|cancelled|refunded|...` in the SELECT
5. `amount_cents` int — what was charged
6. `purchaser_user_id` uuid? — buyer's user id; rename in the SELECT if the table calls it `buyer_user_id` or `user_id`
7. `purchaser_email` text? — JOIN `users` if the table doesn't store it
8. `purchaser_name` text? — JOIN `users` and concat first+last if needed
9. `stripe_payment_intent_id` text? — main charge PI; for tables that store multiple PIs (rental has rental_pi_id + deposit_pi_id), use the charge one
10. `item_name` text? — display label; FROM a product table when possible, otherwise synthesize
11. `created_at` timestamptz

## Tenant scope

The view is read-only and joins only to global/tenant-scoped tables (`users`, `*_product`). The repository (`RecentSalesRepository.List`) always filters by `tenant_id = @tenantId`, so the view itself doesn't need RLS or per-row scope. Don't add a new branch that pulls in cross-tenant data — `tenant_id` must come from the sale-bearing table, not a derived join.

## After updating the view

- Re-run the migrator locally to recreate the view.
- Spot-check the admin dashboard's Recent Purchases panel — a paid row of the new kind should appear.
- Spot-check the Admin → Purchases list — same row, with the right `kind` chip label. Add the new slug to `KIND_LABELS` in `Admin/Dashboard.vue` and `Admin/Purchases.vue` so the chip reads as a proper label, not the slug.
- If the new kind has an admin cancel endpoint, extend `canCancel(p)` in `Admin/Purchases.vue` so the Cancel button renders for it, and add a dispatch branch in `confirmCancel`.

## What the offer is NOT

This skill does not edit the view automatically. It surfaces the gap at the moment new purchase shapes land — when it's cheapest to keep things in sync.
