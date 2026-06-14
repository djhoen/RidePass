-- Platform-level (apex domain) landing page content. Mirrors the per-tenant
-- branding pattern but at the platform level: super admins edit this, the
-- apex Home reads from it, and tenants are completely uninvolved. Two tables:
--
--   platform_branding   one singleton row (id = 1, enforced by CHECK). Holds
--                       the hero, stats, section titles, two HTML content
--                       blocks (how-it-works and benefits), CTA banner, and
--                       a curated list of featured track ids. HTML blocks
--                       use the same RichTextEditor the tenant uses for
--                       about_html / refund_policy_html.
--
--   platform_testimonial   structured list. Each row is a rider testimonial
--                          rendered as a card on the landing page. Sort_order
--                          drives display order.
--
-- Neither table carries tenant_id BY DESIGN. These are platform-level
-- entities owned by super admins; the tenant-isolation rule does not apply
-- because there is no tenant to scope them to. The /tenant-audit skill will
-- correctly flag this as an exception; the design is intentional.
--
-- A seed row at the end populates the singleton with the RidePass mockup's
-- copy so the apex page renders something reasonable on first deploy. Image
-- urls stay null until super admin uploads through the new admin UI.

CREATE TABLE platform_branding (
    id int PRIMARY KEY DEFAULT 1 CHECK (id = 1),

    -- Hero
    hero_image_url             text NULL,
    hero_headline              text NULL,
    hero_subhead               text NULL,
    hero_cta_primary_label     text NULL,
    hero_cta_primary_url       text NULL,
    hero_cta_secondary_label   text NULL,
    hero_cta_secondary_url     text NULL,

    -- Stats badge (auto-counts + optional price). Booleans gate visibility;
    -- the count values themselves come from /Discover at request time.
    stats_show_tracks      boolean NOT NULL DEFAULT true,
    stats_show_event_days  boolean NOT NULL DEFAULT true,
    stats_price_label      text NULL,     -- e.g. "$99 / YEAR". Hidden when null.

    -- Section headings (each defaults reasonably; admin can override).
    -- "How it works" is intentionally absent: that section is hard-coded
    -- on the apex Home (heading + body), since the copy is short and
    -- changes are rare. Add a column and admin field if that ever flips.
    section_tracks_title           text NULL,
    section_events_title           text NULL,
    section_benefits_title         text NULL,
    section_testimonials_title     text NULL,
    section_tracks_near_you_title  text NULL,

    -- Free-form HTML block edited via RichTextEditor.
    benefits_html      text NULL,
    benefits_image_url text NULL,

    -- Bottom CTA banner
    cta_banner_headline    text NULL,
    cta_banner_subhead     text NULL,
    cta_banner_price_label text NULL,
    cta_banner_cta_label   text NULL,
    cta_banner_cta_url     text NULL,

    -- Curated picks for the "Ride the Best Tracks" section. Null or empty
    -- falls back to auto-pick by upcoming event count (handled in code).
    featured_track_ids uuid[] NULL,

    updated_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE platform_testimonial (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    sort_order      int         NOT NULL DEFAULT 100,
    rider_name      text        NOT NULL,
    rider_photo_url text        NULL,
    quote           text        NOT NULL,
    rating          int         NOT NULL DEFAULT 5 CHECK (rating BETWEEN 1 AND 5),
    is_active       boolean     NOT NULL DEFAULT true,
    created_at_utc  timestamptz NOT NULL DEFAULT now(),
    updated_at_utc  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_platform_testimonial_sort
    ON platform_testimonial(sort_order)
    WHERE is_active = true;

-- ── Seed: initial content matching the RidePass mockup ────────────────────────
-- Image url columns stay null so the page renders without broken-image icons
-- until super admin uploads real assets through the new admin UI. Headlines
-- and copy match the mockup so the page is not blank on first deploy.

INSERT INTO platform_branding (
    id,
    hero_headline, hero_subhead,
    hero_cta_primary_label, hero_cta_primary_url,
    hero_cta_secondary_label, hero_cta_secondary_url,
    stats_price_label,
    section_tracks_title, section_events_title,
    section_benefits_title, section_testimonials_title, section_tracks_near_you_title,
    benefits_html,
    cta_banner_headline, cta_banner_subhead, cta_banner_price_label,
    cta_banner_cta_label, cta_banner_cta_url
) VALUES (
    1,
    'Ride more. Pay less.',
    'Access premier motocross tracks across the country with a single membership.',
    'Buy Pass Now', '/Membership',
    'View Events', '/Discover',
    '$99 / YEAR',
    'Ride the best tracks', 'Upcoming events',
    'More than just track access', 'What riders are saying', 'Tracks near you',
    '<ul><li>Discounted ride days</li>'
        || '<li>Exclusive events</li>'
        || '<li>Industry partner discounts</li>'
        || '<li>Digital membership card</li>'
        || '<li>Member-only giveaways</li>'
        || '<li>Growing track network</li></ul>',
    'Annual membership',
    'One pass. Hundreds of riding days.',
    '$99 / Year',
    'Get your pass', '/Membership'
);

INSERT INTO platform_testimonial (sort_order, rider_name, quote, rating) VALUES
    (10, 'Amateur Racer',
        'The pass paid for itself after two weekends.', 5),
    (20, 'Family Membership',
        'We have discovered tracks we would never have ridden otherwise.', 5);
