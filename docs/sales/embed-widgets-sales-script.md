# Sales Script: RidePass Embedded Widgets

*For the sales team. Covers what the embedded functionality is, how to pitch it, what we need from the track, what we give them, and how to offer done-for-you installation. Last updated 2026-07-08.*

---

## The pitch (30 seconds)

> "You keep your website. Your riders never leave it. We drop your event calendar and registration directly into the pages you already have, and everything from sign-up to waiver to payment happens right there on your site. It looks like part of your website because it is part of your website. Setup on your end is pasting two lines of code, and if you give us access to your site, we'll do even that part for you."

The key positioning: most tracks already have a website they like (or a webmaster they pay). The competition says "replace your site" or "link out to our ticketing page." We say **keep your site, we plug into it**.

---

## What the embedded functionality actually is

RidePass widgets are small pieces of the RidePass platform that render inside the track's own website:

| Widget | What the visitor sees | Best spot on their site |
|---|---|---|
| **Events list** | A carousel of upcoming events. Clicking any card opens registration and checkout right there, inline. | Home page or "Events" page |
| **Calendar + events** | A carousel of upcoming events above a full month calendar. Clicking any event opens registration and checkout inline. | "Schedule" or "Calendar" page |
| **Single event** | Registration and checkout for one specific event. | A dedicated page for a big race or promo landing page |

Points that matter in the conversation:

- **The full purchase happens on their site.** Race entry, gate fees, add-ons like camping and parking, waiver signing (including parent/guardian signing for minors), and card payment. The rider never gets bounced to another domain.
- **It auto-sizes and matches the page.** The widget grows and shrinks with its content, so it doesn't look like a bolted-on box with scrollbars.
- **They can place more than one.** Calendar on the schedule page, events carousel on the home page, a single-event widget on a race promo page. All fine, all at once.
- **It stays current automatically.** The widgets show whatever is in their RidePass admin. Add an event in RidePass, it's on their website. No webmaster involved.
- **It's secure by design.** Each track approves exactly which website addresses are allowed to display their widgets. Nobody else can embed their checkout on some other site.

---

## What we need FROM the track

Just one thing to turn it on, plus one thing if we're installing it:

1. **Their website address(es).** The exact domains their site runs on, for example `xyztrack.com` (we automatically cover the `www.` version too). If they have a separate promo or landing-page domain that should also show widgets, we need that address as well. This is the security allow-list: widgets only render on addresses the track has approved.
2. **(Only if we're installing it for them) access to edit their site.** See "The done-for-you offer" below.

That's it. No developer required on their side to *authorize* it. We flip on embedding for their account, enter their site addresses, and generate their snippets.

## What we GIVE the track

1. **A copy-paste snippet per widget.** Two lines of HTML. Example for an events carousel:

   ```html
   <div data-ridepass="events" data-tenant="yourtrack"></div>
   <script src="https://ridepass.io/embed.js" async></script>
   ```

   Options like "show at most 6 events" or "races only" are handled for them; the snippet we hand over already includes their choices. (Internally: Super Admin, Tenants, edit the tenant, Embedded Widgets tab. Pick the widget, set the options, copy the snippet, and use the live preview to show it rendering during the call.)

2. **Placement guidance.** Which widget goes on which page (table above), and the note that the script line only needs to appear once per page even with multiple widgets.

3. **A live preview during the demo.** We can show their actual events rendering in the widget from our dashboard before they've touched their site.

---

## The done-for-you offer

Say it plainly:

> "If you'd rather not touch the code at all, give us access to your website and we'll install it for you. It's usually a 15-minute job on our side."

What "access" means depends on their site, so ask **"What is your website built on?"** and map it:

| Their answer | What to ask for |
|---|---|
| WordPress | A temporary admin (or editor) login to their WordPress dashboard |
| Wix / Squarespace / Weebly / GoDaddy builder | A contributor/admin invite to the site in that platform |
| "Our web guy handles it" | An email intro to their web person; we send the snippets and placement notes and stay on the thread until it's live |
| Custom / hand-built site | Access to wherever the site is edited (hosting login or repo), or again, an intro to whoever maintains it |

Ground rules for us: ask for the least access that works, do the placement, verify the widgets load and a test checkout works, then tell the track to revoke or expire the access we were given. That last line lands well in the pitch; it signals we take their security seriously.

---

## Common questions and answers

**"Will it match our website's look?"**
The widgets carry the track's RidePass branding (their colors and logo, which we set up with them) and sit cleanly inside the page layout. They size themselves to the page automatically.

**"Do riders have to create an account on our website?"**
No changes to their website's accounts at all. Riders check out through RidePass inside the widget, and rider accounts work across every page the widgets appear on.

**"What about payments? Does money touch our website?"**
No. Payment is handled by RidePass through Stripe inside the widget. Their website never sees or stores card data, which is actually a compliance win for them versus rolling their own.

**"What if we redesign or move our website?"**
Paste the same snippets into the new site and tell us the new address so we can approve it. Everything else carries over.

**"Can we still send people to a RidePass page directly?"**
Yes. Every track also gets their hosted RidePass site (their-name.ridepass.io). The widgets and the hosted site show the same events, so links in emails or social can go either place.

**"We only want the calendar, not checkout on our site."**
Fine. Widgets are independent. Start with the calendar; clicking an event can still complete registration inline whenever they're ready for that step.

---

## Qualifying notes for the pipeline

- Best-fit tracks: already have a website they maintain and like, run recurring events, currently link out to Facebook, Google Forms, or a generic ticketing page for sign-ups.
- The embedded option is a deployment model, not a different product. Same admin, same reports, same gate check-in, same pricing conversation as a hosted track.
- If the track has **no** website (or hates theirs), don't pitch embedding. Pitch the hosted RidePass site as their website. Embedding is the answer to "we already have a site," not a requirement.

## Internal checklist (what actually happens after the track says yes)

1. Super Admin: edit the tenant, enable embedding, enter the track's approved website addresses.
2. Snippet builder: generate a snippet per widget with the agreed options; live-preview it on the call.
3. Send the snippets + placement notes, or collect site access and install them ourselves.
4. Verify on their live site: widgets render, resize, and a test registration completes end to end.
5. Have the track revoke any temporary access they gave us.
