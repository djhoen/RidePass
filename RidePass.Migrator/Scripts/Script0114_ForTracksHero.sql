-- For Tracks (operator-acquisition) page content, super-admin editable.
-- Hero copy lives in dedicated columns; the "Why Tracks love RidePass" benefits
-- block reuses the existing platform benefits_html / benefits_image_url /
-- section_benefits_title columns (the band moved off the apex home onto For Tracks),
-- so no data copy is needed.
ALTER TABLE platform_branding ADD COLUMN IF NOT EXISTS for_tracks_hero_eyebrow  text NULL;
ALTER TABLE platform_branding ADD COLUMN IF NOT EXISTS for_tracks_hero_headline text NULL;
ALTER TABLE platform_branding ADD COLUMN IF NOT EXISTS for_tracks_hero_subhead  text NULL;
