# RidePass QA Test Plans

Manual regression test plans for every major piece of RidePass functionality, one file per area. Each plan is grounded in the actual controllers/services/migrations (cited inline) and follows the same shape: surface map, concepts under test, preconditions/test data, case tables (grouped, with `ID | Title | Steps | Expected`), and a known-risks section. Cases are tagged `[NN]` (net-new behavior) or `[R]` (regression guard) where useful.

Last updated: 2026-06-20.

## Commerce core
| Plan | Scope |
|------|-------|
| [events-pricing-registration.md](events-pricing-registration.md) | Event CRUD + duplication, ticket tiers, dynamic price ladders, online checkout, group registration, per-rider uniqueness |
| [waitlist.md](waitlist.md) | Join gate, class-aware bucketing for ladders, promotion on a freed spot, prepay auto-confirm, confirm-window expiry |
| [checkout-payments-discounts.md](checkout-payments-discounts.md) | Stripe PaymentIntent + webhook finalization, gift cards, coupons, reward-voucher application, free-cart path |
| [pos-counter-terminal.md](pos-counter-terminal.md) | In-person counter sales (cash/card), multi-item cart, ladder-at-POS, Stripe Terminal tap-to-pay |
| [refunds-cancellations-disputes.md](refunds-cancellations-disputes.md) | Rider self-cancel, admin cancel + refund, refund math, Stripe disputes + ledger impact |

## Products
| Plan | Scope |
|------|-------|
| [passes-and-season-passes.md](passes-and-season-passes.md) | Pass products + season passes, eligibility, reservations, redemption (notes the retired single-day pass subsystem) |
| [memberships.md](memberships.md) | Tenant membership config, online + POS purchase, validity windows |
| [extras-addons.md](extras-addons.md) | Add-on products + variants, layered inventory, three buy paths, per-unit QR |
| [rentals.md](rentals.md) | Rental products (pool vs per-item), availability/overlap, deposits, check-out/return |
| [concessions.md](concessions.md) | Concession products + variants, server-authoritative card-present sales |

## Access & compliance
| Plan | Scope |
|------|-------|
| [waivers.md](waivers.md) | Per-tenant waiver catalog, versioning, rider/spectator split, minor parent enforcement, sign-on-behalf |
| [gate-checkin-redemption.md](gate-checkin-redemption.md) | QR preview/redeem, scan-once-redeem-many, require-ID gate, redemption audit, Loam Pass QR check-in |

## Integrations
| Plan | Scope |
|------|-------|
| [loampass-credit-integration.md](loampass-credit-integration.md) | RidePass riders redeeming LoamMx credits: super-admin setup, linking, $0 redeem, un-redeem on refund |

## Engagement
| Plan | Scope |
|------|-------|
| [rewards-loyalty.md](rewards-loyalty.md) | Reward programs, voucher earning, one-time-use redemption, percent-off math |
| [surveys-feedback.md](surveys-feedback.md) | Survey build/publish/invite/respond/results, track feedback submit + moderation |
| [messaging-campaigns-notifications.md](messaging-campaigns-notifications.md) | Newsletter/campaigns, SMS (Twilio, opt-out), notifications + preferences, email suppression/unsubscribe |

## Insights
| Plan | Scope |
|------|-------|
| [reports-dashboard.md](reports-dashboard.md) | Sales/rider reports, date scoping, the v_recent_sales unified panel |

## Platform & tenancy
| Plan | Scope |
|------|-------|
| [tenant-settings-branding-home.md](tenant-settings-branding-home.md) | Branding/theme, home + nav config, published state, address, feature toggles, permission gating |
| [super-admin-platform.md](super-admin-platform.md) | Tenant provisioning + type seeding, platform settings, Stripe Connect, payouts/billing, stage promotion |
| [auth-accounts.md](auth-accounts.md) | Signup/login, JWT claim survival, password reset, global riders, profile fields, staff roles |
| [multitenancy-isolation.md](multitenancy-isolation.md) | Subdomain resolution, IsResolved guard, adversarial cross-tenant read/write probes |

---

## Open findings surfaced while authoring (triage candidates)
These are real code observations the plan authors flagged, not yet fixed. They are good first targets when testing each area; see the cited plan for the case that reproduces each.

- **Money: pre-payment discount burn** (checkout-payments-discounts, CP44/CP45): gift-card balance and coupon-redemption rows are written at PaymentIntent-creation time in `BuyEventTicket`, but a declined/abandoned card-funded cart never restores them, so balance/usage is consumed without a sale.
- **Money/data: revenue undercount** (reports-dashboard): `Reports/Summary` and the dashboard revenue blocks count only `event_ticket_purchase`, so season passes, memberships, gift cards, rentals, and gate fees are missing from "Total Revenue" even though `v_recent_sales` includes them.
- **Inventory leak** (concessions): `SumSoldVariant` counts `pending` sales, so abandoned in-flight card-present sales reserve capped-variant stock with no sweeper.
- **Refunds** (refunds-cancellations-disputes): self-cancel swallows a failed Stripe refund yet reports "cancelled"; the admin Refund endpoint never fires waitlist promotion (only Cancel does); day-pass disputes are never recorded.
- **Memberships**: `required_for_*` flags are inert at checkout, and there is no duplicate-active-membership guard on the standalone/POS buy paths (possible double charge).
- **LoamPass** (loampass-credit-integration): rider self-cancel does not un-redeem the credit (only admin refund does); refund marks the redemption `refunded` even if the LoamMx un-redeem call fails; the redeem path dedupes per-tier and checks `tier.Inventory` rather than class-wide `FindRaceClassConflict` + `event.capacity` (possible cross-step double-spend / oversell).
- **Waivers/gate** (waivers, gate-checkin-redemption): the photo-ID gate (`require_id_at_checkin`) is enforced only on bulk `Order/Redeem`, not on single redeem, admin check-in, or Loam Pass gate check-in; the card-buy and counter paths sign the tenant-default waiver rather than the event-pinned waiver.
- **Passes** (passes-and-season-passes): the single-day "Pass" subsystem was removed; season-pass reserve/check-in endpoints exist but are not wired into any Vue view; `requires_waiver` is not enforced at purchase.
- **Extras** (extras-addons): extras revenue may never write the `source_kind='extras'` ledger row despite the schema permitting it.
- **Rentals** (rentals): on a refund failure the booking stays `out` instead of flipping; pre-payment coupon/gift-card mutations are not rolled back on failure.
- **Tenancy** (multitenancy-isolation, auth-accounts): the entire authorization layer hinges on `DefaultMapInboundClaims = false` plus a per-action `IsResolved` guard with no central enforcement; a forgotten guard 500s rather than failing safe. Captured as permanent regression guards.

## Suggested execution order
1. **multitenancy-isolation** + **auth-accounts** (foundational; a leak here invalidates everything else).
2. **events-pricing-registration** + **waitlist** + **checkout-payments-discounts** (the recently-built core).
3. **pos-counter-terminal** + **refunds-cancellations-disputes**.
4. Products (**passes-and-season-passes**, **memberships**, **extras-addons**, **rentals**, **concessions**).
5. **waivers** + **gate-checkin-redemption** + **loampass-credit-integration**.
6. Engagement + insights + platform (**rewards-loyalty**, **surveys-feedback**, **messaging-campaigns-notifications**, **reports-dashboard**, **tenant-settings-branding-home**, **super-admin-platform**).
