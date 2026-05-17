-- Make gift cards opt-in: tenants must enable them in Settings → Gift Cards before
-- riders see the Gift Cards link or the gift card field on checkout. New tenants
-- get the column default of false; existing tenants whose admins haven't yet
-- visited the new settings page also get flipped off so the feature doesn't
-- silently turn on for them.

ALTER TABLE tenant ALTER COLUMN gift_cards_enabled SET DEFAULT false;
UPDATE tenant SET gift_cards_enabled = false;
