-- Saved, rule-based audiences for marketing email. An audience is a definition (a base list plus
-- filters: events or passes bought, address, abandoned checkouts) that is evaluated live, so a
-- rider who buys a season pass tomorrow is in "Season pass holders" tomorrow. Campaigns send to
-- an audience; automations can start when someone JOINS one, which is what audience_member
-- tracks (first time seen matching = joined_at). Additive and rerunnable.

CREATE TABLE IF NOT EXISTS audience (
    id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id          uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name               text        NOT NULL,
    description        text        NULL,
    -- { base, match, rules[] }; see Services.Repositories.Data.NewsletterData.AudienceDefinition.
    definition         jsonb       NOT NULL DEFAULT '{}'::jsonb,
    is_sample          boolean     NOT NULL DEFAULT false,
    created_by_user_id uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS uk_audience_tenant_name ON audience (tenant_id, lower(name));

CREATE TABLE IF NOT EXISTS audience_member (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    audience_id uuid        NOT NULL REFERENCES audience(id) ON DELETE CASCADE,
    email       text        NOT NULL,   -- lower-cased
    name        text        NULL,
    user_id     uuid        NULL,
    joined_at   timestamptz NOT NULL DEFAULT now(),
    left_at     timestamptz NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS uk_audience_member ON audience_member (audience_id, email);
CREATE INDEX IF NOT EXISTS idx_audience_member_active ON audience_member (tenant_id, audience_id, joined_at) WHERE left_at IS NULL;
