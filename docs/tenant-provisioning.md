# Tenant provisioning checklist

GENERATED from `vueapp/src/data/tenantProvisioning.mjs` by `npm run provisioning:doc` (run in
`vueapp`). Edit the data file, not this document. The same list renders in the app at
super admin, Tenant provisioning (`/SuperAdmin/TenantProvisioning`).

Legend: **owner** is who does the step; **automation** is `automatic` (happens on its own once
triggered), `assisted` (there is a UI but a person drives it), or `manual` (done by hand in an
outside system such as DNS, SendGrid, Twilio, or an env file). Manual steps are the automation
backlog.

## Still manual today

- **Branded sending address (noreply@<subdomain>.ridepass.io)** (3. Email; RidePass)
- **SendGrid event webhook: bounces, spam reports, opens, clicks (once per environment)** (3. Email; RidePass)
- **Test purchase and refund** (4. Payments; Both)
- **Platform Twilio credentials (once per environment)** (5. Text messaging; RidePass)
- **Test a text and the STOP/START keywords** (5. Text messaging; Both)
- **Go-live checks** (7. Go-live and staging demos; Both)

## 1. Create the tenant

Everything here is in the super-admin Tenants page. The subdomain works the moment the tenant exists: the *.ridepass.io wildcard DNS record and certificate already cover it.

### Create the tenant record and its first admin

_RidePass | assisted_ (added 2026-09-10)

**Where:** Super admin, Tenants, New tenant (`/SuperAdmin/Tenants`)

1. Subdomain (becomes <subdomain>.ridepass.io; lowercase, no spaces; it is also the email sending subdomain later, so pick it carefully).
2. Display name, timezone, tenant type, venue category, client type.
3. First admin: first name, last name, email. A temporary password is generated and shown once; pass it to the track admin over a private channel, not email.
4. Feature toggles (season passes, extras, concessions, bike shop, gift cards, spectator passes). These gate the admin nav, so turn on only what the track bought.

**Verify:** Open https://<subdomain>.ridepass.io and log in as the new admin.

### Set the service charge, monthly cap, and charge mode

_RidePass | assisted_ (added 2026-09-10)

**Where:** Super admin, Tenants, edit tenant (`/SuperAdmin/Tenants`)

1. Service charge (bps or flat per the contract) and the monthly cap if the contract has one.
2. Charge mode: platform (RidePass collects and pays out) or direct (charges land on the track's own Stripe account).

**Verify:** A test purchase shows the expected fee split in the payout ledger.

**Notes:** Direct-charge mode depends on the platform-level Stripe Connect webhook being registered once per environment.

### Publish when ready (not before)

_RidePass | assisted_ (added 2026-09-10)

**Where:** Super admin, Tenants, edit tenant, Published (`/SuperAdmin/Tenants`)

1. Leave Published off while the track sets up. Unpublished tenants are reachable only by super admins and the tenant's own staff.
2. Turn Published on at go-live so the track appears on the map, featured list, search, and public events.

**Verify:** The track shows up on ridepass.io discovery and its subdomain loads for an anonymous visitor.

## 2. Web presence and branding

Mostly the track's job inside its admin. RidePass sets the website mode.

### Website mode: RidePass site or the track's own domain

_RidePass | assisted_ (added 2026-09-10)

**Where:** Super admin, Tenants, edit tenant, Client type and Custom domain (`/SuperAdmin/Tenants`)

1. Default: the track lives at its RidePass subdomain. Nothing to do.
2. Track keeps its own website: set client type to custom domain, enter their domain, and tick Verified once you have confirmed it is theirs. Discovery links and the apex event click then go to their site, and you can also set the external home and events URLs.

**Verify:** Click the track from ridepass.io discovery and land on the intended site.

### General settings: address, contact email, hours, policies

_Track | assisted_ (added 2026-09-10)

**Where:** Tenant admin, Settings, General

1. Street address and geocode (latitude/longitude drive the map pin).
2. Contact email: this becomes the Reply-To on every email the platform sends for the track, so it must be a monitored inbox.
3. Phone, hours, about text, refund policy, social links.

**Verify:** The footer and map show the right details; reply to a test email and it reaches the contact inbox.

### Branding: logo, hero images, colors, home page sections

_Track | assisted_ (added 2026-09-10)

**Where:** Tenant admin, Settings, Branding and Home page

1. Upload the logo, hero, and benefits images; pick the color scheme.
2. Choose which home page sections show and the next-up event types.

**Verify:** The public home page looks right on phone and desktop.

### Invite staff and set roles

_Track | assisted_ (added 2026-09-10)

**Where:** Tenant admin, Users and Settings, Staff access

1. Invite each staff member and assign the role (admin, counter, gate, reports).
2. Set staff access rules if the track restricts by location or time.

**Verify:** Each staff member can log in and sees only the pages their role allows.

### Embedded widgets on the track's own site

_RidePass | assisted | optional_ (added 2026-09-10)

**Where:** Super admin, Tenants, edit tenant, Embed (`/SuperAdmin/Tenants`)

1. Enable embed widgets and list the origins (their website domains) allowed to frame them.
2. Our own first-party sites are on the global list in Misc settings and never need adding per tenant.

**Verify:** The widget loads on their site and is refused from an unlisted origin.

## 3. Email

Email works out of the box from noreply@ridepass.io with the track's name. A branded sending address needs one manual SendGrid and DNS step per tenant until that is automated.

### Branded sending address (noreply@<subdomain>.ridepass.io)

_RidePass | manual_ (added 2026-09-10)

**Where:** [Tenant admin, Settings, General, then SendGrid and DigitalOcean DNS](https://app.sendgrid.com/settings/sender_auth)

1. In the tenant admin, Settings, General: set Email sending address to noreply@<subdomain>.ridepass.io (only addresses at the tenant's own subdomain are accepted). Blank keeps the platform address with the track's name.
2. SendGrid (the live account, the one linked to the Twilio login; Account ID 110641785): Settings, Sender Authentication, Authenticate Your Domain. DNS host: DigitalOcean. Brand links: No. Domain: <subdomain>.ridepass.io. Leave advanced settings alone.
3. SendGrid shows three CNAME records (em####.<subdomain>, s1._domainkey.<subdomain>, s2._domainkey.<subdomain>). If the form hangs, the entry is usually created anyway: open it from the Sender Authentication list.
4. DigitalOcean, Networking, Domains, ridepass.io: add the three CNAMEs. Enter the host WITHOUT the ridepass.io suffix (for example s1._domainkey.highland) and the value exactly as SendGrid shows it.
5. Back in SendGrid, open the entry and click Verify. DigitalOcean is authoritative, so it verifies within a minute.

**Verify:** Request a password reset from the tenant subdomain to your own inbox. In Gmail, Show original: From is the branded address and signed-by is ridepass.io. The stage web API log must show no "Relay rejected tenant from-address" line for it.

**Notes:** SendGrid treats every subdomain as a separate sender identity; authenticating ridepass.io does not cover its subdomains. Until a subdomain is verified, the mailer automatically resends from noreply@ridepass.io and logs the fallback, so the track is never left without email. A track's OWN domain (info@track.com) is a different, larger job: the track must add records to its DNS, and it is not offered yet. Planned automation: create the SendGrid entry and the DigitalOcean records through their APIs when the address is saved.

### SendGrid event webhook: bounces, spam reports, opens, clicks (once per environment)

_RidePass | manual_ (added 2026-09-10)

**Where:** [SendGrid, Settings, Mail Settings, Event Webhook; then the server env file](https://app.sendgrid.com/settings/mail_settings)

1. In the live SendGrid account: Settings, Mail Settings, Event Webhook. Create a webhook with the HTTP POST URL https://<env host>/api/SendGridWebhook, and tick Bounced, Dropped, Spam Reports, Unsubscribes, Opened, and Clicked. Enable the Signed Event Webhook and copy the verification key.
2. In the env file set Email__SendGrid__WebhookEnabled=true and Email__SendGrid__WebhookVerificationKey=<key>, then restart the web API.
3. Also under Mail Settings, Tracking: leave Open Tracking and Click Tracking available; each marketing send switches them on for itself.

**Verify:** SendGrid's "Test Your Integration" returns 2xx (403 means the key is wrong, 404 means WebhookEnabled is off). After a campaign, a click on a link shows up in the campaign list's Clicks column within a minute.

**Notes:** Without this, hard bounces and spam reports never reach the suppression list, and the Opens and Clicks columns stay at zero. Opens include Apple Mail's automatic pre-fetch, so clicks are the honest engagement number.

### Newsletter list import

_Track | assisted | optional_ (added 2026-09-10)

**Where:** Tenant admin, Subscribers, Import

1. Import the track's existing list as CSV. The import requires the consent attestation and never resurrects an address that opted out.

**Verify:** Subscriber count matches the file minus duplicates and opt-outs.

### Staging only: outbound email and SMS allowlist

_RidePass | assisted | staging only_ (added 2026-09-10)

**Where:** Super admin, Misc settings, Outbound email & SMS (`/SuperAdmin/MiscSettings`)

1. Staging holds a scrubbed copy of production with fake rider addresses that would bounce by the thousand. Keep email and SMS switched on but restricted to the allowlist.
2. Add every real address and phone that a demo or test will send to. Nothing else can be reached.

**Verify:** A test to an unlisted address shows "Suppressed email ... not on the super-admin email allowlist" in the stage web API log.

## 4. Payments

The track owns its Stripe relationship. RidePass verifies the account came back enabled.

### Stripe Connect onboarding

_Track | assisted_ (added 2026-09-10)

**Where:** Tenant admin, Settings, Payments

1. The track admin clicks Connect with Stripe and completes Stripe's hosted onboarding (business details, bank account, identity).
2. RidePass checks the tenant in the super-admin Tenants page: charges enabled and payouts enabled must both be true.

**Verify:** Super admin, Tenants shows charges and payouts enabled for the tenant.

**Notes:** Stripe can leave an account in "restricted" with more information requested; the track sees the request in its Stripe dashboard.

### Test purchase and refund

_Both | manual | cost: One real charge plus its refund on production._ (added 2026-09-10)

**Where:** Tenant public site, then tenant admin, Purchases

1. Buy the cheapest real item with a real card (or a Stripe test card on staging).
2. Confirm the receipt email, the QR code, and the purchase in the admin list; then refund it.

**Verify:** Purchase, receipt, refund, and payout ledger entries all line up.

## 5. Text messaging

SMS is off until a toll-free number is provisioned. The platform Twilio credentials are a once-per-environment setup. Rider messages, text campaigns, and automation text steps all wait on this section.

### Platform Twilio credentials (once per environment)

_RidePass | manual_ (added 2026-09-10)

**Where:** Server env file, then pm2 restart

1. Set Sms__Twilio__AccountSid, Sms__Twilio__AuthToken, and Sms__Twilio__FromNumber (the master account).
2. Set Sms__Twilio__StatusCallbackUrl to https://<env host>/api/TwilioWebhook/StatusCallback and Sms__Twilio__InboundSmsWebhookUrl to https://<env host>/api/TwilioWebhook/IncomingSms. Wrong URLs make Twilio POST to a 404, and no delivery status or billing rows ever appear.
3. Restart the web API and the task runner.

**Verify:** Tenant admin, Settings, SMS no longer shows the "not configured, contact support" warning.

**Notes:** production.env.example still lists an old callback path; the controller route above is the real one.

### Provision the track's toll-free number

_Both | assisted | cost: About two dollars per month per number, plus per-message charges._ (added 2026-09-10)

**Where:** Tenant admin, Settings, SMS

1. Search available toll-free numbers and Provision one. This creates a Twilio subaccount, buys the number, and creates a Messaging Service.
2. Turn SMS on. The per-segment price the track pays is shown on the same page.

**Verify:** The number appears in the Twilio console under the subaccount, and a rider message from Reports arrives on a test phone.

### Toll-free verification

_Both | assisted_ (added 2026-09-10)

**Where:** Tenant admin, Settings, SMS, Toll-free verification

1. Fill in the business details, contact, opt-in description, and sample messages, then Submit.
2. Twilio and the carriers review it; typically five to thirty days. Use Refresh status to check.

**Verify:** Status shows carrier approved.

**Notes:** Until approved, carriers cap the number at roughly ten messages per day, so do not run a bulk rider text before then.

### Test a text and the STOP/START keywords

_Both | manual_ (added 2026-09-10)

**Where:** Tenant admin, Reports, Event riders

1. Send a rider message to a phone you control.
2. Reply STOP, then START, and confirm the opt-out toggles in the Inbox thread.

**Verify:** Delivery status reaches the thread and a billing ledger row appears for the message.

### Text campaigns and automation text steps

_Both | assisted_ (added 2026-09-11)

**Where:** Tenant admin, Campaigns (Send once, Send automatically, Audiences) (`/Admin/Email`)

1. Prerequisites: the steps above (number provisioned, SMS switched on, toll-free verification approved). Until then a text campaign refuses to send and an automation with a text step refuses to turn on, each with a message naming this page.
2. Texts go to the phone on the rider's account (entered at checkout on the Racer Info step or on the profile). The Audiences tab and the campaign composer show how many people in an audience can be texted; a track whose riders never gave a phone will see 0.
3. Open Send automatically, add an email with Send as: Text (or Both), write the text with merge fields, and use Send yourself a test with your phone number. The test arrives with "Reply STOP to opt out" appended, which every marketing text carries.
4. On a first bulk text, send to a small audience first: carriers throttle unverified toll-free numbers to roughly ten messages a day.

**Verify:** The test text lands, a billing ledger row appears for it (Twilio's price via the status webhook), and a STOP reply from that phone marks it opted out in the Inbox and is then skipped by the next campaign with the reason "Recipient replied STOP".

**Notes:** Texts are billed per message segment from Twilio's price, separately from the email tier. A text campaign to an audience with no phones sends nothing and says so rather than sending emails instead.

## 6. Integrations (optional)

Only for tracks that use them.

### Loam Pass credit redemption

_RidePass | assisted | optional_ (added 2026-09-10)

**Where:** Super admin, Tenants, edit tenant, LoamMx destination ID (`/SuperAdmin/Tenants`)

1. Platform env must have LoamPassMx__BaseUrl and LoamPassMx__ApiKey (once per environment).
2. Enter the track's LoamMx destination ID on the tenant.

**Verify:** A rider can link a Loam Pass and redeem credit at the gate.

### QuickBooks Online sync

_Both | assisted | optional_ (added 2026-09-10)

**Where:** Tenant admin, Settings, QuickBooks and Profit centers

1. Platform env must have QuickBooks__ClientId, ClientSecret, RedirectUri, and Environment (once per environment).
2. The track connects its QuickBooks Online company, maps every required account slot and class, and sets the sync start date; then Sync now.

**Verify:** Journal entries appear in QuickBooks for each day since the start date with zero errors.

**Notes:** QuickBooks Desktop cannot be reached by the API; those tracks get the IIF export instead.

## 7. Go-live and staging demos

The last pass before real riders, and the extra steps a staging demo needs.

### Go-live checks

_Both | manual_ (added 2026-09-10)

**Where:** Everywhere above

1. Email: password reset and a one-recipient campaign to your own inbox; check From, Reply-To, DKIM, unsubscribe link.
2. SMS: one rider message and a STOP/START round trip.
3. Payments: one purchase and its refund.
4. Catalog: events, passes, prices, and blackout dates reviewed with the track.
5. Publish the tenant.

**Verify:** Every line above passed on the production tenant, not just on staging.

### Staging demo tenant

_RidePass | assisted | staging only_ (added 2026-09-10)

**Where:** Super admin, Tenants, Seed demo data (`/SuperAdmin/Tenants`)

1. Staging uses Stripe test keys, so purchases use Stripe test cards.
2. Seed demo data on the tenant (staging and development only) for events, riders, and sales.
3. Put every real demo address and phone on the outbound allowlist in Misc settings.
4. Impersonation sessions expire after an hour; log in again before a live demo.

**Verify:** The demo flows (buy, check in, email, text) work end to end on stage with real mail and texts reaching only allowlisted contacts.

