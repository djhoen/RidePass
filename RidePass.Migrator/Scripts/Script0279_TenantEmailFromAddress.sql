-- Per-tenant sending address for rider-facing email (the From line). NULL = the platform
-- default (Email__FromAddress, noreply@ridepass.io) with the track's name as the display name.
-- Validated by the API to "<local>@<tenant subdomain>.<sending domain>" (e.g.
-- noreply@highland.ridepass.io) so it stays under the domain SendGrid DKIM-signs for; see
-- Services.Email.EmailSendingPolicy. Additive and rerunnable.
ALTER TABLE tenant ADD COLUMN IF NOT EXISTS email_from_address text NULL;
