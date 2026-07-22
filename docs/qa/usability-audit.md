# RidePass usability audit (2026-07-22)

A usability / intuitiveness review, distinct from the functional test suite and from the earlier
flow-logic audit (`ux-flow-audit.md`). Every finding is grounded in an actual rendered screenshot of
the live app, not code.

**Method.** 25 full-page screenshots were captured on `motoland.stage.ridepass.io` at desktop
(1280) and mobile (390) widths across four areas (customer purchase/checkout, discovery + food +
membership, auth + account, admin/operator). Four independent reviewers evaluated them against
usability heuristics (clarity of the primary action, information scent, visual hierarchy, trust,
feedback/system status, error prevention, empty states, mobile layout). Screenshots live in
`vueapp/e2e/usability/shots/`; re-run with `npx playwright test --project=usability`.

**Scope caveat.** This is one seeded stage tenant. A few findings reflect that tenant's data/config
(a $0 membership price, a thin "food" menu that only has merch, a `QA Te...` test event on the
calendar) rather than pure design defects. Those are tagged **[config/data]** so they can be
separated from design work. Several items are likely **real bugs**, tagged **[bug]**, and warrant a
fix, not just design discussion.

---

## Priority summary (act on these first)

**Critical**
1. **`/User/Upcoming` was fully broken (HTTP 500) and the UI masked it as "nothing scheduled".**
   [bug] [FIXED 2026-07-22] The page rendered "Failed to load upcoming items" AND "Nothing on your
   schedule yet — FIND A TRACK" while the rider held real passes. Root cause: `GET /Me/Upcoming`
   returned 500 for every user — `UpcomingPurchaseRepository`'s 3-branch `UNION ALL` was misaligned
   after a `WaiverRequired` boolean column was added to the event-ticket branch but not to the
   season-pass or membership branches (16 columns vs 17). So the rider's cross-tenant "what's coming
   up" feed was down in production for everyone. Fixed the query (added the aligned `WaiverRequired`
   column to both branches, verified the corrected UNION runs). Also fixed the frontend
   anti-pattern: the empty state is now gated on `!loadError` so a failed load never renders as
   "nothing scheduled".

**High**
2. **My Passes: "SHOW QR" and "CANCEL" are identical-weight links.** The gate-check action and a
   destructive cancel look the same and sit side by side; a mis-tap in the gate line cancels a paid
   admission. Make SHOW QR a primary button; demote CANCEL to a muted link or a menu.
3. **"Order Food" has no food** [config/data] — only a "Swag" category (hat, shirt). A hungry rider
   concludes the track serves no food. Seed a real food category or rename the page.
4. **Signup asks for 8 fields + 3 checkboxes, none marked optional** (login asks 2). DOB and emergency
   contact are waiver/track-day data, not account data; collecting them before any purchase risks
   abandonment. Defer them to first purchase (the profile page already captures them) or mark optional.
5. **Homepage "Next Up" cards give a CTA only to races.** Practice / Lesson / Open Ride cards show
   date + price but no button, so they look unbookable. Give every card the same action slot.
6. **Membership page can't sell itself** — "$0.00", no benefits list, no explanation of how it differs
   from a Season Pass, and no nav entry to reach it. Add benefits + a real/explicit price, and put it
   in the nav (or alongside Season Passes).
7. **Mobile: the buy control is below the fold** on event detail and season pass — riders scroll past
   a duplicated static pricing recap to reach the only tappable purchase card. Move the card up or add
   a sticky "Buy" bar.
8. **Gift card has no total/fee disclosure before payment** — event and season-pass flows show
   Subtotal / 3% service fee / Total, gift card just shows a bare amount then jumps to pay. Add the
   same order summary so the pay-screen total isn't a surprise.
9. **Admin dashboard buries "Needs Attention".** The disputes/refunds tile looks like a neutral stat
   (no color, no link) even when a dispute is pending against a chargeback deadline. Color it when
   count > 0 and link it to the queue.
10. **Work Orders and Reports aren't in the nav** — reachable only via dashboard Quick Actions, so an
    operator mid-task must go Home first. Promote both to the persistent nav.

---

## Customer purchase & checkout

**Works well:** full price transparency before pay (Subtotal / service fee / Total + "Final total is
shown before you pay"); returning-customer prefill with a "Not you? Log out" escape hatch; helpful
"Add an option above to get started" empty states.

- **[High] mobile-event-detail / mobile-season-passes** — the interactive purchase card sits below a
  full static Event Details + Pricing recap, pushing the only tappable buy control well below the
  mobile fold. Fix: reorder so the purchase card is directly under the hero, or add a sticky "Buy
  Tickets" bar.
- **[High] desktop/mobile-gift-card** — no order total or fee disclosure before "Continue to Payment",
  unlike the event/season-pass flows. If a fee applies, the buyer meets an unexplained higher total on
  the next screen. Fix: add the same fee-inclusive summary.
- **[Medium] gift-card** — "CONTINUE TO PAYMENT" uses a dark-navy fill while every other primary CTA
  ("CONTINUE", "PAY NOW", "SUBSCRIBE") is bright blue; the only button on the page reads as
  secondary/disabled. Fix: use the primary-blue token.
- **[Medium] event-step2-details** — no trust cue (lock / "Secured by Stripe" / card marks) on the
  step right before payment, on an unfamiliar subdomain. Fix: add a "Secured by Stripe" badge near Pay
  Now.
- **[Medium] event-step2-details** — the required-waiver notice sits *below* Pay Now, so a scanning eye
  hits Pay Now first and meets the signature step as a surprise. Fix: move it above Pay Now, or badge
  the button ("Pay Now — waiver next").
- **[Low] mobile-event-detail** — quantity +/- steppers look under the ~44px mobile tap target next to
  tight price text; one-handed mis-taps at the track. Fix: enlarge steppers on mobile.
- **[Low] event-detail / season-passes** — pricing is printed twice (static recap + inside the
  purchase card); redundant, and on mobile the duplicate is what pushes the buy card off-screen.

## Discovery, food & membership

**Works well:** the homepage hero is unambiguous (track name, photo, two high-contrast CTAs in the
fold); the "Next Up" strip surfaces real events with date/type/price without a click; Order Food shows
order-status badges and a clear empty-cart prompt.

- **[High] order-food** [config/data] — "Order Food" lists only a "Swag" category (hat, shirt), no
  food. Fix: seed a real food category, or rename to reflect merch + food, or split them.
- **[High] home** — only the Race card in "Next Up" has a CTA; Practice/Lesson/Open Ride have none and
  look unbookable. Fix: consistent action slot on every card ("Register" / "Reserve" / "Sold Out").
- **[High] membership** [partly config/data] — "$0.00" reads as broken, no benefits, no differentiation
  from Season Pass. Fix: benefits list + real/explicit price + clarify vs Season Passes.
- **[Medium] nav (all)** — no "Membership" entry anywhere, so the membership page is a dead-end with no
  discoverable entry point. Fix: add it to the nav or fold it in with Season Passes.
- **[Medium] mobile-events** [partly config/data] — calendar day cells truncate titles ("QA Te...",
  "Open ...") and a `QA Te[st Event]` shows on the rider-facing calendar. Fix: default mobile to the
  existing List view, and exclude test/non-public events from customer views (not just by domain).
- **[Medium] order-food** — "Your orders" entries are identical ("Order #2 · $20.00 · Preparing") with
  no items or timestamp; a customer with two orders can't tell them apart. Fix: show items or a
  placed-at time.
- **[Low] header (all)** — no cart icon/badge; a rider who adds an item then browses away loses the
  reminder. Fix: add a cart icon with an item count.
- **[Low] order-food** — merch cards use a fork-and-knife placeholder glyph, which implies food on a
  shirt/hat. Fix: require a product photo or use a category-appropriate placeholder.

## Auth & account

**Works well:** login is genuinely low-friction (2 fields, password toggle, visible "Forgot
password?" and "Create one"); signup leads with a value-prop checklist; My Passes cards are scannable
and consistent.

- **[Critical] user-upcoming** [bug] — error banner + empty state shown simultaneously while the rider
  holds 5 passes (see Priority #1).
- **[High] user-mypasses** — SHOW QR (gate check-in) and CANCEL (destructive) are identical-weight
  links (see Priority #2).
- **[High] signup** — 8 fields + 3 checkboxes, none optional, vs login's 2 (see Priority #4).
- **[High] user-mypasses** — all 5 pass cards are identical with no holder name, so a parent holding
  several admissions can't tell which QR is whose. Fix: print the holder name/label on each card.
- **[Medium] user-mypasses** [bug?] — event time shows "2026-07-24 04:00" on every card; 4:00 AM looks
  like an unconverted UTC timestamp. Fix: confirm times render in the track's local timezone.
- **[Medium] user-profile** — no change-password / security section; the only path to change a password
  is "Forgot password" on the logged-out screen. Fix: add "Change password" to the profile.
- **[Low] login vs signup** — the top-nav LOGIN element is a filled pill on the login page but plain
  text on signup. Fix: consistent styling.

## Admin / operator

**Works well:** dashboard Quick Actions put high-frequency tasks one click away; type badges are
color-coded consistently across calendar and list; the Reports sidebar's icon + one-line description
per report is the best information-scent pattern in the app.

- **[High] admin-dashboard** — "Needs Attention" (disputes/refunds) is styled like a neutral stat with
  no color or link (see Priority #9).
- **[High] admin-events / workorders / reports** — no nav entry for Work Orders or Reports (see
  Priority #10).
- **[Medium] admin-workorders** — status pills (Intake/Estimate/In progress) are all near-identical
  pale gray-blue despite an "OVERDUE" filter existing; staff can't spot an overdue job at a glance.
  Fix: color-code pills (red overdue, orange in-progress, gray intake).
- **[Medium] admin-dashboard** — "Top Riders" reads "No paid activity..." right next to "Recent
  Purchases" showing paid transactions, so the dashboard looks out of sync. Fix: clarify the
  waiver-signed-rider scoping copy.
- **[Medium] admin-events** — the "Status" column shows "scheduled" for every row, conveying nothing.
  Fix: replace with a capacity-fill indicator, or fold status into the badge when it varies.
- **[Low] admin-events** — rows offer only Share/Edit, but the dashboard's Upcoming widget has a
  one-click "view riders". Fix: add the roster shortcut to the Events rows for consistency.
- **[Low] admin-reports** — the Daily Revenue chart uses a dual y-axis (dollars + ticket count) on one
  line chart, a known misreading trap. Fix: two stacked single-axis charts or axis-colored labels.

---

## Cross-cutting themes
- **Consistency of the primary action.** The same "primary button = bright blue" and "cards have a CTA
  slot" patterns are applied unevenly (gift-card CTA color, Next-Up cards, SHOW QR vs CANCEL). Making
  the primary action visually consistent everywhere is the single highest-leverage change.
- **Mobile purchase path.** The buy control being below a duplicated pricing recap recurs on event and
  season-pass pages; a sticky buy bar would fix the class.
- **Empty vs error states.** The `/User/Upcoming` bug is the severe case, but "helpful empty state"
  discipline is otherwise good; the rule to enforce is never show empty + error together.
- **Test/seed data leaking to customers.** The `QA Te...` event and the food-less "Order Food" are
  stage-seed issues, but the calendar not filtering non-public events is a real product concern.
