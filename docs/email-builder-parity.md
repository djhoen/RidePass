# Email builder: where we stand against Mailchimp

Status: gap analysis 2026-09-10; **Phases A and B built on stage the same day** (buttons, branded header/footer, preview text, phone/desktop preview, opens and clicks, saved templates), plus a tenant-written marketing email footer (Settings, General) and one Email page with Send once / Send automatically tabs. The question was "can they insert images, and does our
functionality match Mailchimp". Images: yes as of today (see §2). Full parity: no, and most of
the gap is deliberate. This document says what we have, what Mailchimp has that a track would
notice, and which of those are worth building.

## 1. What a track can do today

| Capability | RidePass | Notes |
|---|---|---|
| Rich text: headings, bold/italic/underline/strike, lists, quotes, links, rules | Yes | Same editor as blog and pages |
| Inline images (upload, insert) | **Yes (added today)** | PNG, JPEG, WebP, GIF, 5 MB; stored per tenant; made absolute and width-capped at send time |
| Email-safe rendering | Yes (added today) | 600px centered column, system font, images never wider than the column |
| Merge fields / personalization | Yes | `{{first_name}}`, pass and event tokens; unknown tokens render empty |
| Audiences | Yes | Newsletter list, purchasers of an event / event type, pass holders; on-mountain planned |
| Scheduling a send | Yes | Date and time in the track's timezone |
| Automations (drips, journeys) | Yes | Purchase, event, pass-expiry, fixed-date timing; skips late buyers |
| Unsubscribe, suppression, bounces, spam reports | Yes | One-click header, footer link, SendGrid event webhook |
| Cost preview before sending | Yes | Per email, tiered; automations show backlog and monthly rate |
| Test send to yourself | Yes | Campaigns render the real body; automations fill real merge values and show the send date |
| Reporting | Partial | Sent, skipped, failed with reasons. **No opens or clicks.** |

## 2. What Mailchimp has that a track would notice

Ranked by how often a track would hit the gap, not by how hard it is.

| Gap | What it is | Worth it? | Effort |
|---|---|---|---|
| **Buttons** | A styled call-to-action ("Buy tickets") instead of a text link | Yes, first | half a day: an editor block that emits a bulletproof table-based button |
| **Header with logo and footer with address** | Every email opens with the track's logo and closes with its name, address, and socials | Yes | 1 day: wrap sends with tenant branding (logo, colors, contact, socials already on the tenant) |
| **Preview text** | The snippet inboxes show after the subject | Yes | half a day: a field per campaign/step, emitted as a hidden preheader |
| **Opens and clicks** | Open rate, click rate, who clicked what | Yes, but honestly | 1.5 days: SendGrid open/click events already arrive at our webhook and are ignored; store per send and roll up. Opens are unreliable since Apple Mail Privacy Protection; say so in the UI |
| **Templates / saved designs** | Start from a saved layout instead of a blank body | Yes, later | 1 day: save a body as a named template per tenant; "start from" in the composer |
| **Columns and image-plus-text blocks** | Two-column layouts, image left with text right | Sometimes | 1 to 2 days: a small block palette (image+text, two columns) emitting table layouts |
| **Mobile preview** | See the email at phone width before sending | Nice | half a day: render the prepared HTML in a 375px iframe in the compose dialog |
| **Drag-and-drop block canvas** | Mailchimp's whole editor model | No | Weeks. The rich text editor plus a handful of blocks covers a bike park's newsletter; a canvas is the thing tracks would spend an afternoon fighting |
| **A/B subject tests, send-time optimization, predictive segments** | Marketing-team features | No | No audience here; a track sends to a few thousand people a few times a season |
| **Signup forms and landing pages** | Hosted forms that feed the list | Already covered differently | The newsletter signup lives on the track's own RidePass site |
| **Tags and segments on the list** | Slice subscribers by behavior | Partly covered | Purchase-based audiences do the useful part; free-form tags are a later add if asked |
| **Double opt-in** | Confirmation email before joining the list | Maybe | half a day, only if a track's counsel asks for it |

## 3. Proposed scope: "good enough that nobody asks for Mailchimp"

Phase A (about 3 days): buttons, branded header and footer, preview text, mobile preview.
Phase B (about 2.5 days): opens and clicks from the SendGrid webhook with a per-campaign report
and a per-automation-step report, plus saved templates.
Phase C (if tracks ask): image-plus-text and two-column blocks, double opt-in, list tags.

Not planned: the drag-and-drop canvas, A/B testing, send-time optimization, predictive
segments.

## 4. Decisions needed

1. Phase A first, or opens and clicks first? Opens and clicks answer "did anyone read it",
   which tracks ask about the day after the first send.
2. Branded header: take the logo and colors from the tenant's branding automatically (no new
   settings), or give marketing a separate email header image?
3. Whether to show open rates at all given Apple Mail inflates them, or click rate only.
