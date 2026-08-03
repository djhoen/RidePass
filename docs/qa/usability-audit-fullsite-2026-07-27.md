# RidePass whole-site usability sweep (2026-07-27)

Follows `usability-audit.md` (2026-07-22), which reviewed 25 screenshots across four areas. This one
widens coverage to **every static route in the router** and adds an automated signal layer, because
the most damaging usability defect found last time was invisible in a screenshot.

## Method

`vueapp/e2e/usability/fullsite.spec.ts`, run against `motoland.stage.ridepass.io` as `qa.admin`.

**133 page loads**: all 115 reachable static routes at desktop (1440), plus the 18 customer/gate
routes a rider or operator would open on a phone at 390. Read-only: navigation and observation only,
nothing clicks a control that writes.

Each load records a signal record (`fullsite-report.json`, appended live to
`fullsite-signals.jsonl`): console errors, uncaught exceptions, any `/api/` response ≥400, redirect
target, rendered text length, and whether the page shows error copy, empty-state copy, or **both at
once**. Screenshots land in `e2e/usability/shots/fullsite/`.

That last signal is the point of the exercise. The 2026-07-22 audit's critical finding was
`/User/Upcoming` returning HTTP 500 for every user while the UI rendered "Nothing on your schedule
yet" — a silent data-loss bug that looks completely healthy in a screenshot. Empty-over-failed is now
detected rather than hoped for.

Re-run with:
```
STAGE_BASE_URL=https://motoland.stage.ridepass.io \
STAGE_ADMIN_EMAIL=... STAGE_ADMIN_PASSWORD=... \
npx playwright test --project=usability --grep "full site sweep"
```

## Result: the functional layer is clean

Across all 133 loads:

| signal | count |
|---|---|
| uncaught JS exceptions | **0** |
| console errors | **0** |
| `/api/` responses ≥400 | **0** |
| blank / near-empty pages | **0** (excluding embeds, below) |
| error copy shown together with empty copy | **0** |

No route repeated the `/User/Upcoming` failure pattern. Every page that had data rendered it, and
every page that had none said so without also reporting a failure.

**Access control held.** 13 of 14 `/SuperAdmin/*` routes correctly bounced the tenant-admin session
to `/`. See finding 2 for the fourteenth.

**The 11 `/embed/*` routes are correct, not broken.** They render "Embedding is not enabled for this
track" (50 chars), which is why they tripped the thin-page threshold. Accurate and honest copy;
recorded here only so the next run doesn't re-investigate. [config/data]

---

## Status

All six findings below are **fixed** (2026-07-27), verified by build + type-check. Not yet deployed:
the stage push is paused until the check-in add-ons land, so none of this has been exercised on stage.

## Findings

### 1. The gate counter can only find a customer by email, while the scanner next to it searches by name. [High]
**FIXED.** New `POST /Counter/Riders/Search` backed by `IUserRepository.SearchForCounter`, matching
email, first name, last name, full name, and digit-normalised phone. Scoped to global riders plus
this tenant's users, deliberately NOT the existing platform-wide `SearchAll`, which would have leaked
other tracks' customers into a gate operator's search results. One match resolves straight through;
several present a pick list showing email and phone to tell same-name riders apart; none offers to
create. A failed search now reports the failure instead of rendering as "no customer", which was the
path that produced duplicate accounts.


`/Admin/Counter` step 1 offers a single **Email** field and a FIND button. `/Admin/RedeemTickets`,
used at the same gate for the same queue, offers *"Rider or buyer name, or email"*.

This matters because of where the Counter is used: a walk-up line, on a tablet, with people waiting.
Full email addresses are slow to type, easy to mistype, and frequently not what the customer
volunteers ("I'm Dan Tester" / a phone number). The operator's fallback is to create a duplicate
customer, which quietly corrupts the customer list and the spend totals that read from it.

The capability already exists in the product one screen over, so this is an inconsistency rather
than a feature gap. Widening Counter lookup to name/phone/email would close it.

### 2. `/SuperAdmin/Bootstrap` has no router guard, unlike every other SuperAdmin route. [Low, not a vulnerability]
**FIXED, but not the way the finding implied.** A static `requiresRoles: ['super_admin']` guard would
be wrong: during a genuine first run there is nobody to authenticate as, so the page must stay
reachable. Instead the page now asks (`GET /SuperAdmin/Bootstrap/Needed`, anonymous for the same
reason) and renders "RidePass is already set up" with a sign-in link once a super admin exists. If
the check itself fails it shows the form rather than a wrong "already set up" - the server refuses a
second bootstrap regardless, so the worst case is an honest error on submit.


Every other `/SuperAdmin/*` route carries `meta: { requiresAuth: true, requiresRoles: ['super_admin'] }`.
`Bootstrap` (router.ts:97-100) carries no `meta` at all, so any signed-in user — or an anonymous
visitor — can load a page headed "Bootstrap RidePass".

**This is not exploitable, and I verified that rather than assuming it.** The endpoint is
`[AllowAnonymous]` by deliberate design (the platform must be initialisable before anyone can
authenticate), but it opens with `if (await _users.AnySuperAdminExists()) return BadRequest(...)`.
Stage has 4 super admins, so the call always refuses.

The defect is a dead-end page: a tenant admin who wanders onto it gets a form that can only ever
fail. Adding the same `meta` guard once bootstrap has run would make the routing honest. Worth doing
because the current state invites someone to "fix" the server guard instead.

### 3. A B2B sales banner runs inside logged-in customers' account pages. [Medium]
**FIXED.** The tenant-side operator CTA is suppressed under `/User/*`.


`/User/MyOrders` at mobile width ends with a full-width blue bar: *"Run a track? See how RidePass can
power yours. SEE MORE"*.

This is a rider who has signed in to look at their own orders. Pitching them on running their own
track is off-key in an account area, and on mobile it is one of the largest elements on the page.
Reasonable on marketing surfaces (`/`, `/ForTracks`); questionable behind a customer login.

### 4. On mobile, an empty account page is mostly chrome. [Medium]
**FIXED.** Same suppression as 3, plus the newsletter signup, which was asking a signed-in customer
to hand over an email the track already has.


Same screen: the actual content is one small card ("No shop orders yet" + BROWSE THE SHOP), followed
by address, social links, contact email, a newsletter signup, a copyright line, and the banner from
finding 3. Content is roughly a fifth of the viewport; footer and marketing fill the rest.

The empty state itself is good — it explains the page and offers the next action. It is simply
buried. Consider suppressing the marketing footer on `/User/*` routes at mobile width.

### 5. The empty-state CTA is styled as a secondary action. [Low, reinforces a known theme]
**FIXED.** `My Orders` and `Upcoming` empty-state CTAs are now filled primary buttons.


"BROWSE THE SHOP" renders as a pale tonal button. It is the only action on the page and should read
as primary. This is another instance of the cross-cutting *"consistency of the primary action"* theme
already recorded in `usability-audit.md`, not a new class of problem.

### 6. The Counter wastes most of its screen and hides "new customer". [Low]
**PARTLY FIXED.** "New customer" now sits beside Find, always visible, and prefills from whatever was
typed (an email into the email box, anything else split into first/last name). The wasted vertical
space is untouched: that is a layout redesign, not a defect fix.


At 1440 the Counter uses roughly the top third; the remainder is empty. The stepper implies four more
steps that cannot be previewed. And walk-ins are the screen's stated purpose ("For walk-ins without a
device"), yet the create-customer affordance only appears *after* a lookup fails. Surfacing it
alongside FIND would save a step on the most common path.

---

## Coverage and caveats

- **Functional sweep: complete.** All 115 reachable static routes, both widths where relevant.
- **Visual review: a prioritised sample**, concentrated on operator screens the 2026-07-22 audit did
  not reach (Counter, gate scan, account pages). The remaining screenshots are captured and on disk;
  nobody has looked at all 133 by eye and this document does not claim otherwise.
- **Dynamic routes are not covered** (`/Event/:id`, `/Admin/Customers/:userId`, the token-bearing
  links like `/SignWaiver/:token`, and the 25 others). They need fixture ids per run.
- **One seeded tenant.** Findings tagged [config/data] reflect stage seed data, not design.
- Stage's footer contact address renders as `redacted-...@stage.invalid`, which is seed scrubbing
  working as intended.
