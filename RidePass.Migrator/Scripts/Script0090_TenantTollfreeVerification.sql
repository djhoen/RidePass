-- Toll-free SMS Verification submission state per tenant.
--
-- Why: Twilio's US/Canada toll-free numbers ship with a ~10-message/day
-- carrier cap until the number is "verified" via Twilio's Tollfree
-- Verification (TFV) API. Without verification, the moment a tenant tries
-- to do any real volume (event-day rider blasts, waitlist promotions) they
-- hit the wall and messages silently drop or queue. Verification submits
-- the tenant's business info + opt-in flow to Twilio, which forwards it to
-- the major US carriers for review (5–30 days typical), and once approved
-- the cap goes away.
--
-- One row per tenant. The row exists from the moment the admin opens the
-- verification form — drafts (status IS NULL) are unsubmitted, anything
-- with a twilio_verification_sid has been submitted at least once. A
-- rejected verification can be edited and resubmitted; status flips back
-- through the lifecycle.
--
-- Per-tenant table: tenant_id is the primary key (one row per tenant), so
-- the standard `tenant_id` predicate is automatically enforced by the PK
-- lookup. ON DELETE CASCADE so a tenant going away takes the verification
-- record with it.
--
-- Array columns:
--   • use_case_categories — Twilio expects an array of one or more
--     category strings ("Account Notification", "Marketing", "2FA", ...).
--   • production_message_samples — 1–5 example messages.
--   • opt_in_image_urls — Twilio wants screenshots of the consent flow
--     (signup page with the disclosure language). We store URLs only;
--     hosting the images is out of scope for v1 (admin uploads to their
--     own site / Imgur / wherever).

CREATE TABLE tenant_tollfree_verification (
    tenant_id uuid PRIMARY KEY REFERENCES tenant(id) ON DELETE CASCADE,

    -- Business identity
    business_name                  text NULL,
    business_website               text NULL,
    business_street_address        text NULL,
    business_city                  text NULL,
    business_state_province_region text NULL,
    business_postal_code           text NULL,
    business_country               text NULL,

    -- Business contact (the person Twilio + carriers reach if they have
    -- questions about the submission). Often the same as the admin who
    -- filled out the form, but kept distinct so it can be a different
    -- compliance/legal contact.
    business_contact_first_name text NULL,
    business_contact_last_name  text NULL,
    business_contact_email      text NULL,
    business_contact_phone      text NULL,

    -- Where Twilio sends status-change emails (approved / rejected).
    notification_email text NULL,

    -- Use case
    use_case_categories       text[] NULL,
    use_case_summary          text   NULL,
    production_message_samples text[] NULL,

    -- Opt-in flow. opt_in_type is one of Twilio's enums:
    --   VERBAL | WEB_FORM | PAPER_FORM | VIA_TEXT | MOBILE_QR_CODE
    opt_in_type        text   NULL,
    opt_in_image_urls  text[] NULL,

    -- Volume tier. Twilio's enum: "10" / "100" / "1,000" / "10,000" / etc.
    -- Stored as text because it's a categorical bucket, not a number we
    -- ever do arithmetic on.
    message_volume text NULL,

    additional_information text NULL,

    -- Twilio Verification SID (HH...) once submitted. NULL on a draft that
    -- the admin is still filling out.
    twilio_verification_sid text NULL,

    -- Twilio lifecycle status. NULL = unsubmitted draft. Other values map
    -- 1:1 to Twilio's: PENDING_REVIEW, IN_REVIEW, TWILIO_APPROVED,
    -- TWILIO_REJECTED, CARRIER_APPROVED, CARRIER_REJECTED.
    status                  text NULL,
    rejection_reason        text NULL,
    last_submitted_at_utc   timestamptz NULL,
    last_status_checked_at_utc timestamptz NULL,

    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now()
);

-- Reverse lookup for the eventual status webhook (when Twilio POSTs a
-- status change keyed by verification SID). Partial — only submitted rows
-- have a SID.
CREATE INDEX ix_tenant_tollfree_verification_sid
    ON tenant_tollfree_verification(twilio_verification_sid)
    WHERE twilio_verification_sid IS NOT NULL;
