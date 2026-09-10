-- Super-admin kill switch + allowlist for ALL outbound email / SMS.
-- Read by Services.Delivery.OutboundDeliveryGate at the last hop (SmtpEmailer /
-- TwilioSmsSender) in both the web API and the TaskRunner. Defaults keep today's
-- behaviour (everything on, everyone allowed); staging flips them in the super-admin
-- Misc settings page so demo sends only ever reach inboxes we own.
-- Rerunnable: seeds are ON CONFLICT DO NOTHING so an edited value is never reset.
INSERT INTO platform_setting (key, value) VALUES
    ('outbound_email_enabled',   'true'),
    ('outbound_email_allowlist', ''),
    ('outbound_sms_enabled',     'true'),
    ('outbound_sms_allowlist',   '')
ON CONFLICT (key) DO NOTHING;
