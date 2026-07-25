# Employee Passes and Employee Discounts

Implementation-ready design. Status: proposed, not implemented. 2026-07-25.

## 1. Requirements

1. Employee passes, **free or discounted**.
2. Employees **sign the waiver and verify ID like everyone else**. No bypass.
3. An **admin page** to manage employee passes.
4. When an employee **becomes inactive their pass is immediately invalid** and cannot be used.
5. The **same page manages employee discounts**.
6. An employee is **anyone with an active account on the tenant**, but eligibility is not
   entitlement: a pass is never automatic and must be **approved by an admin**.

## 2. Current state, verified

There is no employee-pass concept. "Employee" appears only as a reporting label
(`ConcessionEmployeeSalesRow`) and as an example comp reason ("Employee meal").

What exists and this design builds on:

- **Staff are users on the tenant.** `users.tenant_id = <tenant>` with `role` plus a roles list
  and `status` in `('active','disabled')`. On local dev today: 8 tenant-scoped staff across
  admin / manager / cashier / scanner, and 52 global riders with `tenant_id IS NULL`.
- **There is already an off switch.** `PUT User/Tenant/{id}/Status`
  (`UserController.cs` line 648, `UsersManage`) sets `active` / `disabled`, refusing to let an
  admin disable themselves. Requirement 4 should hang off this, not off a parallel concept.
- **The season pass stack already does everything an employee pass needs**: QR redemption,
  gate check-in, walk-up admission (Script0236), photo, waiver, wristbands, reservations,
  reporting, and a benefit model for discounts.
- **ID verification is a first-class, persisted feature as of `Script0238_RiderIdVerification.sql`**
  (in the working tree, landed while this document was being written). `POST SeasonPass/Pass/{token}/VerifyId`
  (`SeasonPassController.cs` line 1091, `SalesRedeem`) cards a rider **once**: it records
  `id_verified_at`, `id_verified_by_user_id`, and `id_verified_dob` (what the document actually
  said, kept deliberately separate from the self-reported birthdate), and every later scan shows
  the tick. A tenant switch can make a verified ID plus a signed waiver a precondition for
  issuing a wristband. This supersedes the older per-scan attestation
  (`tenant.require_id_at_checkin`, Script0120), which recorded nothing.
- **Registration is also enforced**: `SeasonPassPurchase.IsRegistered` (`SeasonPass.cs` line 139)
  requires `PhotoDataUrl` plus a waiver signature where the product requires one, and an
  unregistered pass grants nothing (`SeasonPassRepository.cs` lines 260-263).

Requirement 2 therefore needs **no new mechanism, only the discipline not to add an exemption**:
an employee pass is carded through the same endpoint, by the same staff, recorded the same way.

## 3. Core decision: an employee pass is a season pass

Model it as a `season_pass_product` flagged as employee-only, issued to a staff user as an
ordinary `season_pass_purchase`. Not a new entity.

The reuse is not incidental, it is the entire value: gate scan, walk-up admission, photo, waiver,
wristband linking, reservations, the Rider Report, and the benefit model all work with zero new
code. A parallel `employee_pass` table would mean reimplementing admission, and the second
implementation is the one that quietly drifts out of step with the waiver rules.

The deltas are exactly three: it must never be publicly purchasable, it is issued by an admin
rather than bought, and its validity is additionally gated on employment.

## 4. Product flag, and the leak this creates

```sql
ALTER TABLE season_pass_product
    ADD COLUMN IF NOT EXISTS is_employee boolean NOT NULL DEFAULT false;
```

**This is the highest-risk part of the feature.** A `$0` product that reaches a public read
path is free season passes for the entire internet, and pass products are public by default:
`GET SeasonPass/Products` (`SeasonPassController.cs` line 88) is anonymous, as is
`GET SeasonPass/Landing/{slugOrId}` (line 98).

Every one of these must exclude `is_employee` products, and the exclusion belongs in the
**repository**, not in each caller, so a future endpoint cannot forget it:

| Path | Required behavior |
|---|---|
| `ListProductsForTenant(activeOnly: true)` public list | Excluded |
| `GetLanding` by slug **or id** | 404, same message as not-found so its existence is not leaked |
| `POST SeasonPass/Buy` | Rejected: "This pass isn't available for purchase." |
| Embed widgets (`/embed/*`) | Excluded (they read the same product list) |
| Admin product list | **Included**, that is where it is configured |

Recommended shape: `ListProductsForTenant(Guid tenantId, bool activeOnly, bool includeEmployee = false)`
so the default is safe and only the admin path opts in. A boolean defaulting to "hide" means a
new caller written a year from now is secure by omission.

Verified this is a two-line change, not a sweep: `ListProductsForTenant` has exactly two
callers, `SeasonPassController.cs` line 92 (the public list, stays default) and line 158 (the
admin list, gated on `CatalogManage`, passes `includeEmployee: true`). Also verified the
cross-tenant discovery hub does **not** surface pass products at all, so it is not a fourth
leak path.

## 5. Approval and issuance

Requirement 6 makes approval an explicit act, so the design has to keep two things apart that
are easy to conflate:

- **Eligibility** is derived and automatic: an active account on this tenant. It costs nothing
  and grants nothing.
- **Approval** is a deliberate admin decision, recorded, and is what actually creates the pass.

Being eligible therefore puts an employee on the roster with **no pass**, which is the correct
default state and the one most of the roster will sit in.

Two rules follow, and both are worth stating because they are the ways this gets accidentally
undone later:

1. **Nothing auto-issues.** Creating a staff account, assigning a role, or re-activating a
   leaver must never mint a pass as a side effect. If someone later adds "give every cashier a
   pass" as a convenience, that is a policy change requiring a decision, not a shortcut.
2. **Bulk selection is still approval.** An admin ticking twelve names and confirming has
   approved twelve passes, and that is fine for onboarding a seasonal crew. A background rule
   that grants on account creation is not, even though it saves the same clicks. The difference
   is whether a human decided.

Record who approved it. `season_pass_purchase` already carries `cancelled_by_user_id` for the
other end of the lifecycle, so add the matching column rather than inventing a new shape:

```sql
ALTER TABLE season_pass_purchase
    ADD COLUMN IF NOT EXISTS issued_by_user_id uuid NULL REFERENCES users(id);
```

`issued_by_user_id` plus the existing `created_at` is the approval record the admin page shows
("Approved by Dana, May 3"). The audit log entry is the tamper-evident trail; this column is
the queryable one, and the page needs both for different reasons.

An employee pass is then issued from the admin page, not bought:

- Create a `season_pass_purchase` with `product_id` = the employee product,
  `purchaser_user_id` = the staff user, no Stripe PaymentIntent.
- **Free product (`price_cents = 0`)**: issue directly as `status = 'paid'`,
  `amount_cents = 0`. No ledger entry, because no money moved. This mirrors how the concession
  path already treats a fully-comped sale.
- **Discounted product (`price_cents > 0`)**: issue as `status = 'pending'`. Everything else
  falls out for free, because every admission and benefit path already requires
  `status = 'paid'`, so an uncollected employee pass grants nothing until it is settled.
  **How it gets settled is an open decision, not a solved one** — see section 12.2. An earlier
  draft of this document said "collect at the counter", which is wrong: verified that
  `Counter/Sale` handles only `event_ticket`, `extras`, `rental`, and `membership` and rejects
  everything else (`CounterController.cs` line 525). **There is no counter path for season
  passes at all**, so there is nothing to collect on today.
- The pass is **not registered** at issue time: no photo, no signature. The employee completes
  registration exactly as a customer does, which is what satisfies requirement 2.

Audit both issue and revoke through `IAuditLogger`, following the `shop.refund` shape at
`BikeShopRegisterController.cs` line 558.

## 6. Requirement 4: derive employment, never copy it

**Do not** stamp a `revoked` flag onto the pass when a user is disabled. A copied flag is
correct exactly until someone disables a user through a path that forgot to update passes, and
then it is silently wrong in the direction that lets a former employee keep riding.

**Derive it.** An employee pass is usable only when **both** conditions hold, and neither
implies the other:

| Condition | Source | Changes when |
|---|---|---|
| **Approved** | the pass row exists, `status = 'paid'`, not revoked | an admin issues or revokes |
| **Eligible** | holder is an active account on this tenant | HR/ops toggles the user |

Approval is already enforced by the existing rules: no pass row, or a revoked one, means
nothing to scan. Eligibility is the new predicate. For a purchase whose product `is_employee`,
validity additionally requires the holder to still be an active staff member of this tenant:

```sql
-- Employee passes are only valid while their holder is active staff at this tenant.
-- Non-employee passes are unaffected.
(NOT p.is_employee OR EXISTS (
    SELECT 1 FROM users hu
    WHERE hu.id = sp.purchaser_user_id
      AND hu.tenant_id = @tenantId
      AND hu.status = 'active'))
```

Disabling the user in Admin > Users then invalidates the pass **instantly and everywhere**,
with no second write to go wrong and no job to run.

**Re-activation resumes the pass**, because the admin's approval was never withdrawn: only
eligibility lapsed. That is the right default and it is safe for a concrete reason rather than
a hopeful one: `season_pass_purchase.valid_from_date` and `valid_to_date` are both `NOT NULL`,
so a pass approved for last season cannot come back to life when a seasonal employee returns in
May. It has already expired on its own terms. Where a track genuinely wants the approval itself
to lapse with employment, the explicit revoke on the admin page is the tool, and revoking is
strictly clearer than a rule that silently expires approvals on a status change.

Note the predicate requires `hu.tenant_id = @tenantId`, not merely `status = 'active'`. A
staff member removed from the tenant (rather than disabled) must also lose the pass, and a
global rider account can never satisfy it.

### 6.1 Every place it has to go

Missing one of these is a free-admission bug, so enumerate rather than trust a grep later:

1. `GetPassForGateCheckIn` — the gate's admission decision.
2. `LookupPassByToken` (`SeasonPassController.cs` line 966) — the scan **display**. Staff must
   see "employment ended" on the scan, not a mystery failure when they press the button.
3. `CreateGateCheckIn` and `CreateWalkUpGateCheckIn` (`SeasonPassRepository.cs` lines 540, 577)
   — the writes. Guarding only the read leaves a race and a direct-call bypass.
4. `ListActiveBenefitGrantsForUser` (line 253) — the discount path, which is section 8.
5. `GetWalkUpCheckIn` (line 604) — the idempotent re-scan lookup.
6. `Reserve` — a disabled employee must not hold event capacity.

Implement it as **one shared SQL constant** applied at each site, in the style the repository
already uses for `ActiveWaiverCte` and `TenantZoneExpr`, with a comment marking it load-bearing.
Six hand-written copies of a security predicate is five opportunities to paraphrase it wrong.

### 6.2 What deliberately does not happen

An admission already taken stands. A rider who was admitted this morning and disabled this
afternoon keeps this morning's gate record: it happened. Requirement 4 is about future use.

## 7. Requirement 2: no exemptions, stated explicitly

The employee product must be created with `requires_waiver = true`, nothing in this feature may
bypass `IsRegistered`, and employees are carded through the same
`POST SeasonPass/Pass/{token}/VerifyId` as customers. If the tenant turns on the Script0238
switch that gates wristbands on a verified ID, it applies to staff too. The pressure to add "staff skip the waiver" will come from someone
who finds it silly to make the lift operator sign a form. The waiver is a liability instrument
and the employee is the person most likely to be on the hill every day; the exemption is
exactly backwards. The photo requirement is already unconditional and should stay that way,
because it is what makes the pass non-transferable at the gate.

## 8. Employee discounts, and the dependency nobody will expect

Requirement 5 wants discounts managed on the same page. The mechanism is `season_pass_benefit`
rows on the employee product: the model already covers `event`, `concession`, `rental`, and
`retail` with a discount kind and value.

**But most of those surfaces do not read it yet.** `Script0178_SeasonPassBenefits.sql`
lines 20-24 say so outright: `'concession'` and `'rental'` are permitted "so the surfaces can
be wired without another constraint rebuild, but NOTHING writes them yet", F&B still reads its
own tenant-wide `concession_menu_settings.season_pass_discount_*`, and the only live consumer
is the event path (`PurchaseController.cs` line 601, `benefitType: "event"`).

So the honest scoping is:

| Surface | Employee discount in Phase 1? |
|---|---|
| Event tickets | **Yes**, the benefit path already works |
| F&B | **No**, needs the concession POS switched from its tenant-wide config to the benefit model |
| Rentals | **No**, nothing reads rental benefits |
| Bike shop retail | **No**, same |

An employee F&B discount is the one a track will ask for first, and it is a prerequisite piece
of work, not a checkbox. It is also the same unification already contemplated for season pass
perks generally, so doing it once serves both. Scope it separately and do not let this document
imply it is included.

Interim option worth offering the tenant: F&B already has manager-PIN comps with a tenant-defined
"Employee meal" reason. That is a real workflow today, just a manual one.

## 9. Admin page

`/Admin/EmployeePasses`, gated on `UsersManage` — the same bar as disabling staff, because
issuing one grants free admission.

- **Roster**: every tenant user, their role, employment status, and pass state. The states are
  the cross-product of section 6's two conditions, and the page should name them plainly:

  | Pass state | Meaning |
  |---|---|
  | **No pass** | Eligible, never approved. The default, and most of the roster. |
  | **Pending payment** | Approved, priced, not yet collected at the counter |
  | **Not registered** | Approved and paid, but no photo or waiver yet, so it will not scan |
  | **Active** | Approved and eligible |
  | **Invalid, inactive employee** | Approved but the account is disabled |
  | **Revoked** | Approval withdrawn by an admin |

- **Approve** per employee (and multi-select for onboarding a crew), and **revoke** for the
  leaver who is staying on the books. Show **who approved it and when**, from
  `issued_by_user_id` + `created_at`.
- No control anywhere that grants passes as a side effect of a role or account change.
- **Registration state** per row, since an issued-but-unregistered pass will not scan and the
  employee needs telling.
- **Employee pass product editor**: price (0 = free), validity dates, and the benefit rows that
  are section 8's discounts, with the not-yet-wired surfaces visibly disabled and labelled
  rather than silently absent. Offering an F&B percentage that the POS ignores would be worse
  than not offering it, which is the exact reasoning Script0178 already recorded.
- Show inactive employees with an invalid pass explicitly. "Their pass stopped working" is a
  question the manager will be asked, and the page should answer it without a support ticket.

## 10. Reporting

- An `employee_pass` bucket in the Rider Report purchase type, so employee admissions are
  distinguishable from paid ones. This is a small addition to `TicketPurchaseTypeExpr` and the
  season-pass branch now that both derive a purchase type.
- Employee admissions carry `amount_cents = 0` and write no ledger entry, so they cannot
  inflate revenue. Worth asserting in a test: a free pass that accidentally books a sale row is
  the kind of error that surfaces at year-end.

## 11. Settled

- **Who counts as an employee**: anyone with an active account on the tenant. No separate
  eligibility flag, no role filter. Eligibility is cheap and grants nothing on its own.
- **Passes are never automatic**: approval is an explicit admin act, recorded in
  `issued_by_user_id`, and section 5 lists the two ways that rule tends to get undone later.
- **Leavers who return**: the pass resumes, because approval was never withdrawn and the pass's
  own `valid_to_date` already bounds it (section 6).
- **One pass per employee.** `purchaser_user_id` is the employee, so their pass appears under
  their own account in `GET SeasonPass/Mine` (which keys on that column,
  `SeasonPassRepository.cs` line 414).
- **Dependent / family passes are out of scope**, decided 2026-07-25. Not "later, maybe" as a
  hedge: deliberately not now, possibly revisited. See section 14 for what a future version
  would inherit, kept short on purpose so this document describes what is being built.

## 12. Open decision

### 12.1 How a priced employee pass gets paid for

Section 5 issues a priced pass as `pending`, which grants nothing until settled. The question is
what settles it, and the answer is genuinely open because **there is no counter path for season
passes**: `Counter/Sale` rejects any cart kind outside `event_ticket`, `extras`, `rental`, and
`membership` (`CounterController.cs` line 525), and `SeasonPass/Buy` (line 434) is the rider
buying for themselves. Four options, in ascending cost:

**(a) Free only in Phase 1.** Ship employee passes at `price_cents = 0` and defer priced ones
entirely. Nothing to build, nothing to get wrong, and it covers the common case: most tracks
comp staff outright. Recommended for Phase 1 regardless of which of the others is chosen later.

**(b) Payroll deduction, i.e. mark as settled out of band.** Almost certainly what a resort
actually does for a discounted staff pass: "we will take $150 out of your first cheque." This is
not a payment at all, it is an out-of-band settlement, and modelling it as one is both cheaper
and more truthful than pretending a card was charged. An admin action flips the pending pass to
paid with a `payroll` method, audited, with the amount recorded for the tenant's own books.

  The real question it raises is accounting, not plumbing: **does it book revenue?** No money
  touches Stripe or the till. There is an established convention for tenant-held money — the
  concession cash path deliberately sets `net_to_tenant = -RidepassCut` so the platform never
  pays out money it never held. A payroll deduction is the same category, with the extra
  question of whether the platform takes a cut on a staff pass at all. Recommend **no ledger
  entry and no platform cut** for employee passes, amount recorded on the purchase for tenant
  reporting only. That is a business decision to confirm, not an engineering one.

**(c) Employee self-serve checkout of an approved pass.** Approval creates the pending pass; the
employee pays for *their own* pending pass from their account. Reuses the existing Stripe
machinery in `SeasonPass/Buy` without exposing the product publicly, because the pass row
already exists and belongs to them — the endpoint validates "this pending employee pass is
yours" rather than "this product is purchasable". Moderate work, no new money surface, no
counter dependency. This is the recommendation **if card payment is required**.

**(d) Build season-pass sales into the counter.** The largest, but the only one that also
solves a gap customers hit: a walk-up who wants to buy a season pass at the window cannot today.
If counter pass sales are wanted for customers anyway, priced employee passes ride along for
free and this becomes the obvious choice. **Worth asking before picking (b) or (c)**, because it
changes the sequencing entirely.

### 12.2 Settled by the eligibility rule

**Contractors and coaches without accounts** get no pass: eligibility requires an active account
on the tenant. Handing passes to people who never log in is a different feature (an unattached
pass carrying a photo and an ID verification), not an employee pass.

## 13. Phasing

- **Phase 1**: `is_employee` flag with every public path excluded, `issued_by_user_id`, admin
  approval and revoke, the derived eligibility predicate at all six sites in 6.1, the admin
  page, and event-surface discounts.
- **Phase 2**: F&B employee discounts, which means wiring the concession POS to
  `season_pass_benefit` (shared with the season-pass perk unification).
- **Phase 3**: rental and retail discounts on the same wiring; Rider Report bucket.

## 14. Deferred: dependent / family passes

Not in scope (decision, 2026-07-25). Recorded only so a future revisit starts from the findings
rather than repeating the investigation:

- **The employment cascade would come free.** Section 6's predicate keys on
  `sp.purchaser_user_id`. With the employee as purchaser and the family member as holder, a
  dependent's pass is already tied to the employee's account status: deactivate the employee and
  the whole family goes invalid in the same instant, through the same predicate, no new
  mechanism.
- **A dependent would need no account.** `season_pass_purchase` already carries
  `holder_first_name`, `holder_last_name`, and `holder_birthdate` separately from the purchaser,
  and Script0238 states outright that the admitted person "frequently has no users row at all".
  Photo, waiver (guardian-signed for a minor), and ID verification all attach to the credential
  rather than a login.
- **What would still need building**: a separate dependent product with its own price, and a
  `max_dependents` cap, since "family" is otherwise unbounded.
- **What could never be built**: relationship verification. Nothing distinguishes a spouse from
  a housemate. The honest control is the cap plus the employee's attestation, and tenants should
  be told that rather than sold a check that does not exist.
- **Do not model it as buddy passes.** A buddy pass is occasional, countable, per-visit, and
  needs the holder present at a counter each time. A spouse riding sixty days a season is not an
  occasional guest.
