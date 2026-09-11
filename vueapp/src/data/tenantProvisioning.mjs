// Single source of truth for "what it takes to stand up a tenant", including every step that
// is still MANUAL. Rendered by the super-admin page /SuperAdmin/TenantProvisioning and exported
// to docs/tenant-provisioning.md by `npm run provisioning:doc` (run it after editing this file so
// the repo doc never drifts from the page).
//
// Keep entries honest about automation: a step marked "automatic" happens without anyone doing
// anything beyond the trigger named in `where`; "assisted" has a UI but a person still drives it;
// "manual" is done by hand in an outside system (DNS, SendGrid, Twilio, an env file).
//
// Plain ESM (not TS) so the doc exporter can import it under Node without a build step.

/** @typedef {'RidePass' | 'Track' | 'Both'} Owner */
/** @typedef {'automatic' | 'assisted' | 'manual'} Automation */
/** @typedef {{ label: string, to?: string, href?: string }} Where */
/**
 * @typedef {object} ProvisioningStep
 * @property {string} id            Stable slug, used as the anchor and checklist key.
 * @property {string} title
 * @property {Owner} owner          Who does it.
 * @property {Automation} automation
 * @property {Where} where          Where it happens. `to` = in-app route (super-admin pages are
 *                                  linkable from the page; tenant admin paths are shown as text
 *                                  because they live on the tenant's subdomain), `href` = external.
 * @property {string[]} steps       What to do, in order.
 * @property {string} verify        How you know it worked.
 * @property {string} [notes]       Gotchas, costs, timing.
 * @property {string} [cost]        Money this step spends, if any.
 * @property {boolean} [stagingOnly]
 * @property {boolean} [optional]
 * @property {string} added         ISO date the step was added to this list.
 */
/** @typedef {{ id: string, title: string, intro: string, steps: ProvisioningStep[] }} ProvisioningPhase */

/** @type {ProvisioningPhase[]} */
export const provisioningPhases = [
    {
        id: 'create',
        title: '1. Create the tenant',
        intro: 'Everything here is in the super-admin Tenants page. The subdomain works the moment the tenant exists: the *.ridepass.io wildcard DNS record and certificate already cover it.',
        steps: [
            {
                id: 'create-tenant',
                title: 'Create the tenant record and its first admin',
                owner: 'RidePass',
                automation: 'assisted',
                where: { label: 'Super admin, Tenants, New tenant', to: '/SuperAdmin/Tenants' },
                steps: [
                    'Subdomain (becomes <subdomain>.ridepass.io; lowercase, no spaces; it is also the email sending subdomain later, so pick it carefully).',
                    'Display name, timezone, tenant type, venue category, client type.',
                    'First admin: first name, last name, email. A temporary password is generated and shown once; pass it to the track admin over a private channel, not email.',
                    'Feature toggles (season passes, extras, concessions, bike shop, gift cards, spectator passes). These gate the admin nav, so turn on only what the track bought.',
                ],
                verify: 'Open https://<subdomain>.ridepass.io and log in as the new admin.',
                added: '2026-09-10',
            },
            {
                id: 'service-charge',
                title: 'Set the service charge, monthly cap, and charge mode',
                owner: 'RidePass',
                automation: 'assisted',
                where: { label: 'Super admin, Tenants, edit tenant', to: '/SuperAdmin/Tenants' },
                steps: [
                    'Service charge (bps or flat per the contract) and the monthly cap if the contract has one.',
                    'Charge mode: platform (RidePass collects and pays out) or direct (charges land on the track\'s own Stripe account).',
                ],
                verify: 'A test purchase shows the expected fee split in the payout ledger.',
                notes: 'Direct-charge mode depends on the platform-level Stripe Connect webhook being registered once per environment.',
                added: '2026-09-10',
            },
            {
                id: 'publish',
                title: 'Publish when ready (not before)',
                owner: 'RidePass',
                automation: 'assisted',
                where: { label: 'Super admin, Tenants, edit tenant, Published', to: '/SuperAdmin/Tenants' },
                steps: [
                    'Leave Published off while the track sets up. Unpublished tenants are reachable only by super admins and the tenant\'s own staff.',
                    'Turn Published on at go-live so the track appears on the map, featured list, search, and public events.',
                ],
                verify: 'The track shows up on ridepass.io discovery and its subdomain loads for an anonymous visitor.',
                added: '2026-09-10',
            },
        ],
    },
    {
        id: 'presence',
        title: '2. Web presence and branding',
        intro: 'Mostly the track\'s job inside its admin. RidePass sets the website mode.',
        steps: [
            {
                id: 'website-mode',
                title: 'Website mode: RidePass site or the track\'s own domain',
                owner: 'RidePass',
                automation: 'assisted',
                where: { label: 'Super admin, Tenants, edit tenant, Client type and Custom domain', to: '/SuperAdmin/Tenants' },
                steps: [
                    'Default: the track lives at its RidePass subdomain. Nothing to do.',
                    'Track keeps its own website: set client type to custom domain, enter their domain, and tick Verified once you have confirmed it is theirs. Discovery links and the apex event click then go to their site, and you can also set the external home and events URLs.',
                ],
                verify: 'Click the track from ridepass.io discovery and land on the intended site.',
                added: '2026-09-10',
            },
            {
                id: 'general-settings',
                title: 'General settings: address, contact email, hours, policies',
                owner: 'Track',
                automation: 'assisted',
                where: { label: 'Tenant admin, Settings, General' },
                steps: [
                    'Street address and geocode (latitude/longitude drive the map pin).',
                    'Contact email: this becomes the Reply-To on every email the platform sends for the track, so it must be a monitored inbox.',
                    'Phone, hours, about text, refund policy, social links.',
                ],
                verify: 'The footer and map show the right details; reply to a test email and it reaches the contact inbox.',
                added: '2026-09-10',
            },
            {
                id: 'branding',
                title: 'Branding: logo, hero images, colors, home page sections',
                owner: 'Track',
                automation: 'assisted',
                where: { label: 'Tenant admin, Settings, Branding and Home page' },
                steps: [
                    'Upload the logo, hero, and benefits images; pick the color scheme.',
                    'Choose which home page sections show and the next-up event types.',
                ],
                verify: 'The public home page looks right on phone and desktop.',
                added: '2026-09-10',
            },
            {
                id: 'staff',
                title: 'Invite staff and set roles',
                owner: 'Track',
                automation: 'assisted',
                where: { label: 'Tenant admin, Users and Settings, Staff access' },
                steps: [
                    'Invite each staff member and assign the role (admin, counter, gate, reports).',
                    'Set staff access rules if the track restricts by location or time.',
                ],
                verify: 'Each staff member can log in and sees only the pages their role allows.',
                added: '2026-09-10',
            },
            {
                id: 'embed',
                title: 'Embedded widgets on the track\'s own site',
                owner: 'RidePass',
                automation: 'assisted',
                optional: true,
                where: { label: 'Super admin, Tenants, edit tenant, Embed', to: '/SuperAdmin/Tenants' },
                steps: [
                    'Enable embed widgets and list the origins (their website domains) allowed to frame them.',
                    'Our own first-party sites are on the global list in Misc settings and never need adding per tenant.',
                ],
                verify: 'The widget loads on their site and is refused from an unlisted origin.',
                added: '2026-09-10',
            },
        ],
    },
    {
        id: 'email',
        title: '3. Email',
        intro: 'Email works out of the box from noreply@ridepass.io with the track\'s name. A branded sending address needs one manual SendGrid and DNS step per tenant until that is automated.',
        steps: [
            {
                id: 'sending-address',
                title: 'Branded sending address (noreply@<subdomain>.ridepass.io)',
                owner: 'RidePass',
                automation: 'manual',
                where: { label: 'Tenant admin, Settings, General, then SendGrid and DigitalOcean DNS', href: 'https://app.sendgrid.com/settings/sender_auth' },
                steps: [
                    'In the tenant admin, Settings, General: set Email sending address to noreply@<subdomain>.ridepass.io (only addresses at the tenant\'s own subdomain are accepted). Blank keeps the platform address with the track\'s name.',
                    'SendGrid (the live account, the one linked to the Twilio login; Account ID 110641785): Settings, Sender Authentication, Authenticate Your Domain. DNS host: DigitalOcean. Brand links: No. Domain: <subdomain>.ridepass.io. Leave advanced settings alone.',
                    'SendGrid shows three CNAME records (em####.<subdomain>, s1._domainkey.<subdomain>, s2._domainkey.<subdomain>). If the form hangs, the entry is usually created anyway: open it from the Sender Authentication list.',
                    'DigitalOcean, Networking, Domains, ridepass.io: add the three CNAMEs. Enter the host WITHOUT the ridepass.io suffix (for example s1._domainkey.highland) and the value exactly as SendGrid shows it.',
                    'Back in SendGrid, open the entry and click Verify. DigitalOcean is authoritative, so it verifies within a minute.',
                ],
                verify: 'Request a password reset from the tenant subdomain to your own inbox. In Gmail, Show original: From is the branded address and signed-by is ridepass.io. The stage web API log must show no "Relay rejected tenant from-address" line for it.',
                notes: 'SendGrid treats every subdomain as a separate sender identity; authenticating ridepass.io does not cover its subdomains. Until a subdomain is verified, the mailer automatically resends from noreply@ridepass.io and logs the fallback, so the track is never left without email. A track\'s OWN domain (info@track.com) is a different, larger job: the track must add records to its DNS, and it is not offered yet. Planned automation: create the SendGrid entry and the DigitalOcean records through their APIs when the address is saved.',
                added: '2026-09-10',
            },
            {
                id: 'sendgrid-event-webhook',
                title: 'SendGrid event webhook: bounces, spam reports, opens, clicks (once per environment)',
                owner: 'RidePass',
                automation: 'manual',
                where: { label: 'SendGrid, Settings, Mail Settings, Event Webhook; then the server env file', href: 'https://app.sendgrid.com/settings/mail_settings' },
                steps: [
                    'In the live SendGrid account: Settings, Mail Settings, Event Webhook. Create a webhook with the HTTP POST URL https://<env host>/api/SendGridWebhook, and tick Bounced, Dropped, Spam Reports, Unsubscribes, Opened, and Clicked. Enable the Signed Event Webhook and copy the verification key.',
                    'In the env file set Email__SendGrid__WebhookEnabled=true and Email__SendGrid__WebhookVerificationKey=<key>, then restart the web API.',
                    'Also under Mail Settings, Tracking: leave Open Tracking and Click Tracking available; each marketing send switches them on for itself.',
                ],
                verify: 'SendGrid\'s "Test Your Integration" returns 2xx (403 means the key is wrong, 404 means WebhookEnabled is off). After a campaign, a click on a link shows up in the campaign list\'s Clicks column within a minute.',
                notes: 'Without this, hard bounces and spam reports never reach the suppression list, and the Opens and Clicks columns stay at zero. Opens include Apple Mail\'s automatic pre-fetch, so clicks are the honest engagement number.',
                added: '2026-09-10',
            },
            {
                id: 'subscribers',
                title: 'Newsletter list import',
                owner: 'Track',
                automation: 'assisted',
                optional: true,
                where: { label: 'Tenant admin, Subscribers, Import' },
                steps: [
                    'Import the track\'s existing list as CSV. The import requires the consent attestation and never resurrects an address that opted out.',
                ],
                verify: 'Subscriber count matches the file minus duplicates and opt-outs.',
                added: '2026-09-10',
            },
            {
                id: 'email-allowlist-staging',
                title: 'Staging only: outbound email and SMS allowlist',
                owner: 'RidePass',
                automation: 'assisted',
                stagingOnly: true,
                where: { label: 'Super admin, Misc settings, Outbound email & SMS', to: '/SuperAdmin/MiscSettings' },
                steps: [
                    'Staging holds a scrubbed copy of production with fake rider addresses that would bounce by the thousand. Keep email and SMS switched on but restricted to the allowlist.',
                    'Add every real address and phone that a demo or test will send to. Nothing else can be reached.',
                ],
                verify: 'A test to an unlisted address shows "Suppressed email ... not on the super-admin email allowlist" in the stage web API log.',
                added: '2026-09-10',
            },
        ],
    },
    {
        id: 'payments',
        title: '4. Payments',
        intro: 'The track owns its Stripe relationship. RidePass verifies the account came back enabled.',
        steps: [
            {
                id: 'stripe-connect',
                title: 'Stripe Connect onboarding',
                owner: 'Track',
                automation: 'assisted',
                where: { label: 'Tenant admin, Settings, Payments' },
                steps: [
                    'The track admin clicks Connect with Stripe and completes Stripe\'s hosted onboarding (business details, bank account, identity).',
                    'RidePass checks the tenant in the super-admin Tenants page: charges enabled and payouts enabled must both be true.',
                ],
                verify: 'Super admin, Tenants shows charges and payouts enabled for the tenant.',
                notes: 'Stripe can leave an account in "restricted" with more information requested; the track sees the request in its Stripe dashboard.',
                added: '2026-09-10',
            },
            {
                id: 'test-purchase',
                title: 'Test purchase and refund',
                owner: 'Both',
                automation: 'manual',
                where: { label: 'Tenant public site, then tenant admin, Purchases' },
                steps: [
                    'Buy the cheapest real item with a real card (or a Stripe test card on staging).',
                    'Confirm the receipt email, the QR code, and the purchase in the admin list; then refund it.',
                ],
                verify: 'Purchase, receipt, refund, and payout ledger entries all line up.',
                cost: 'One real charge plus its refund on production.',
                added: '2026-09-10',
            },
        ],
    },
    {
        id: 'sms',
        title: '5. Text messaging',
        intro: 'SMS is off until a toll-free number is provisioned. The platform Twilio credentials are a once-per-environment setup. Rider messages, text campaigns, and automation text steps all wait on this section.',
        steps: [
            {
                id: 'twilio-master',
                title: 'Platform Twilio credentials (once per environment)',
                owner: 'RidePass',
                automation: 'manual',
                where: { label: 'Server env file, then pm2 restart' },
                steps: [
                    'Set Sms__Twilio__AccountSid, Sms__Twilio__AuthToken, and Sms__Twilio__FromNumber (the master account).',
                    'Set Sms__Twilio__StatusCallbackUrl to https://<env host>/api/TwilioWebhook/StatusCallback and Sms__Twilio__InboundSmsWebhookUrl to https://<env host>/api/TwilioWebhook/IncomingSms. Wrong URLs make Twilio POST to a 404, and no delivery status or billing rows ever appear.',
                    'Restart the web API and the task runner.',
                ],
                verify: 'Tenant admin, Settings, SMS no longer shows the "not configured, contact support" warning.',
                notes: 'production.env.example still lists an old callback path; the controller route above is the real one.',
                added: '2026-09-10',
            },
            {
                id: 'provision-number',
                title: 'Provision the track\'s toll-free number',
                owner: 'Both',
                automation: 'assisted',
                where: { label: 'Tenant admin, Settings, SMS' },
                steps: [
                    'Search available toll-free numbers and Provision one. This creates a Twilio subaccount, buys the number, and creates a Messaging Service.',
                    'Turn SMS on. The per-segment price the track pays is shown on the same page.',
                ],
                verify: 'The number appears in the Twilio console under the subaccount, and a rider message from Reports arrives on a test phone.',
                cost: 'About two dollars per month per number, plus per-message charges.',
                added: '2026-09-10',
            },
            {
                id: 'tollfree-verification',
                title: 'Toll-free verification',
                owner: 'Both',
                automation: 'assisted',
                where: { label: 'Tenant admin, Settings, SMS, Toll-free verification' },
                steps: [
                    'Fill in the business details, contact, opt-in description, and sample messages, then Submit.',
                    'Twilio and the carriers review it; typically five to thirty days. Use Refresh status to check.',
                ],
                verify: 'Status shows carrier approved.',
                notes: 'Until approved, carriers cap the number at roughly ten messages per day, so do not run a bulk rider text before then.',
                added: '2026-09-10',
            },
            {
                id: 'test-sms',
                title: 'Test a text and the STOP/START keywords',
                owner: 'Both',
                automation: 'manual',
                where: { label: 'Tenant admin, Reports, Event riders' },
                steps: [
                    'Send a rider message to a phone you control.',
                    'Reply STOP, then START, and confirm the opt-out toggles in the Inbox thread.',
                ],
                verify: 'Delivery status reaches the thread and a billing ledger row appears for the message.',
                added: '2026-09-10',
            },
            {
                id: 'text-campaigns',
                title: 'Text campaigns and automation text steps',
                owner: 'Both',
                automation: 'assisted',
                where: { label: 'Tenant admin, Campaigns (Send once, Send automatically, Audiences)', to: '/Admin/Email' },
                steps: [
                    'Prerequisites: the steps above (number provisioned, SMS switched on, toll-free verification approved). Until then a text campaign refuses to send and an automation with a text step refuses to turn on, each with a message naming this page.',
                    'Texts go to the phone on the rider\'s account (entered at checkout on the Racer Info step or on the profile). The Audiences tab and the campaign composer show how many people in an audience can be texted; a track whose riders never gave a phone will see 0.',
                    'Open Send automatically, add an email with Send as: Text (or Both), write the text with merge fields, and use Send yourself a test with your phone number. The test arrives with "Reply STOP to opt out" appended, which every marketing text carries.',
                    'On a first bulk text, send to a small audience first: carriers throttle unverified toll-free numbers to roughly ten messages a day.',
                ],
                verify: 'The test text lands, a billing ledger row appears for it (Twilio\'s price via the status webhook), and a STOP reply from that phone marks it opted out in the Inbox and is then skipped by the next campaign with the reason "Recipient replied STOP".',
                notes: 'Texts are billed per message segment from Twilio\'s price, separately from the email tier. A text campaign to an audience with no phones sends nothing and says so rather than sending emails instead.',
                added: '2026-09-11',
            },
        ],
    },
    {
        id: 'integrations',
        title: '6. Integrations (optional)',
        intro: 'Only for tracks that use them.',
        steps: [
            {
                id: 'loampass',
                title: 'Loam Pass credit redemption',
                owner: 'RidePass',
                automation: 'assisted',
                optional: true,
                where: { label: 'Super admin, Tenants, edit tenant, LoamMx destination ID', to: '/SuperAdmin/Tenants' },
                steps: [
                    'Platform env must have LoamPassMx__BaseUrl and LoamPassMx__ApiKey (once per environment).',
                    'Enter the track\'s LoamMx destination ID on the tenant.',
                ],
                verify: 'A rider can link a Loam Pass and redeem credit at the gate.',
                added: '2026-09-10',
            },
            {
                id: 'quickbooks',
                title: 'QuickBooks Online sync',
                owner: 'Both',
                automation: 'assisted',
                optional: true,
                where: { label: 'Tenant admin, Settings, QuickBooks and Profit centers' },
                steps: [
                    'Platform env must have QuickBooks__ClientId, ClientSecret, RedirectUri, and Environment (once per environment).',
                    'The track connects its QuickBooks Online company, maps every required account slot and class, and sets the sync start date; then Sync now.',
                ],
                verify: 'Journal entries appear in QuickBooks for each day since the start date with zero errors.',
                notes: 'QuickBooks Desktop cannot be reached by the API; those tracks get the IIF export instead.',
                added: '2026-09-10',
            },
        ],
    },
    {
        id: 'golive',
        title: '7. Go-live and staging demos',
        intro: 'The last pass before real riders, and the extra steps a staging demo needs.',
        steps: [
            {
                id: 'golive-checks',
                title: 'Go-live checks',
                owner: 'Both',
                automation: 'manual',
                where: { label: 'Everywhere above' },
                steps: [
                    'Email: password reset and a one-recipient campaign to your own inbox; check From, Reply-To, DKIM, unsubscribe link.',
                    'SMS: one rider message and a STOP/START round trip.',
                    'Payments: one purchase and its refund.',
                    'Catalog: events, passes, prices, and blackout dates reviewed with the track.',
                    'Publish the tenant.',
                ],
                verify: 'Every line above passed on the production tenant, not just on staging.',
                added: '2026-09-10',
            },
            {
                id: 'staging-demo',
                title: 'Staging demo tenant',
                owner: 'RidePass',
                automation: 'assisted',
                stagingOnly: true,
                where: { label: 'Super admin, Tenants, Seed demo data', to: '/SuperAdmin/Tenants' },
                steps: [
                    'Staging uses Stripe test keys, so purchases use Stripe test cards.',
                    'Seed demo data on the tenant (staging and development only) for events, riders, and sales.',
                    'Put every real demo address and phone on the outbound allowlist in Misc settings.',
                    'Impersonation sessions expire after an hour; log in again before a live demo.',
                ],
                verify: 'The demo flows (buy, check in, email, text) work end to end on stage with real mail and texts reaching only allowlisted contacts.',
                added: '2026-09-10',
            },
        ],
    },
]
