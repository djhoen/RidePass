-- Repoint the apex (platform) landing page away from the cancelled multi-track
-- membership pitch toward its new purpose: a rider discovery hub (tracks +
-- events) with an operator-acquisition CTA pointing at /ForTracks.
--
-- Updates only the singleton platform_branding row (id = 1), and ONLY where
-- each field still holds its original Script0091 seed value. The guard means
-- any copy a super admin has already customized through the Home Page editor
-- is left untouched — we only rewrite the stale defaults.
--
-- platform_branding is platform-level and carries no tenant_id (see Script0091);
-- the /tenant-audit skill flags that as the intended exception.

-- ── Hero ────────────────────────────────────────────────────────────────────
UPDATE platform_branding SET hero_headline = 'Find your track. Ride this weekend.'
    WHERE id = 1 AND hero_headline = 'Ride more. Pay less.';

UPDATE platform_branding SET hero_subhead =
        'Discover motocross tracks near you, see what is on the schedule, and grab your gate pass before you load the van.'
    WHERE id = 1 AND hero_subhead =
        'Access premier motocross tracks across the country with a single membership.';

UPDATE platform_branding SET hero_cta_primary_label = 'Browse tracks'
    WHERE id = 1 AND hero_cta_primary_label = 'Buy Pass Now';
UPDATE platform_branding SET hero_cta_primary_url = '/Discover'
    WHERE id = 1 AND hero_cta_primary_url = '/Membership';

UPDATE platform_branding SET hero_cta_secondary_label = 'Upcoming events'
    WHERE id = 1 AND hero_cta_secondary_label = 'View Events';
UPDATE platform_branding SET hero_cta_secondary_url = '/Events'
    WHERE id = 1 AND hero_cta_secondary_url = '/Discover';

-- The price stat was the membership price; the hero now shows only tracks +
-- event-day counts, so clear it.
UPDATE platform_branding SET stats_price_label = NULL
    WHERE id = 1 AND stats_price_label = '$99 / YEAR';

-- ── Benefits band: repurposed from membership perks to rider-facing reasons
--    to ride RidePass tracks. ───────────────────────────────────────────────
UPDATE platform_branding SET section_benefits_title = 'Why ride with RidePass'
    WHERE id = 1 AND section_benefits_title = 'More than just track access';

UPDATE platform_branding SET benefits_html =
        '<ul><li>Book and pay online before you arrive</li>'
        || '<li>Skip the gate line with a digital pass</li>'
        || '<li>Sign your waiver once, ride all season</li>'
        || '<li>Find events and open-ride days near you</li>'
        || '<li>Earn rewards at participating tracks</li></ul>'
    WHERE id = 1 AND benefits_html =
        '<ul><li>Discounted ride days</li>'
        || '<li>Exclusive events</li>'
        || '<li>Industry partner discounts</li>'
        || '<li>Digital membership card</li>'
        || '<li>Member-only giveaways</li>'
        || '<li>Growing track network</li></ul>';

-- ── Bottom CTA banner: repurposed from "buy a membership" to operator
--    acquisition, pointing at the new /ForTracks page. Price label cleared
--    (irrelevant for a B2B call to action). ───────────────────────────────
UPDATE platform_branding SET cta_banner_headline = 'Run a track?'
    WHERE id = 1 AND cta_banner_headline = 'Annual membership';

UPDATE platform_branding SET cta_banner_price_label = NULL
    WHERE id = 1 AND cta_banner_price_label = '$99 / Year';

UPDATE platform_branding SET cta_banner_subhead =
        'Sell passes and tickets online, check riders in at the gate, and run your events. All in one place.'
    WHERE id = 1 AND cta_banner_subhead = 'One pass. Hundreds of riding days.';

UPDATE platform_branding SET cta_banner_cta_label = 'See RidePass for tracks'
    WHERE id = 1 AND cta_banner_cta_label = 'Get your pass';
UPDATE platform_branding SET cta_banner_cta_url = '/ForTracks'
    WHERE id = 1 AND cta_banner_cta_url = '/Membership';
