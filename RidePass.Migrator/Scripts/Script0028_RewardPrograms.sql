-- Tenant-defined loyalty programs. A program describes a rule like "buy 5 day passes,
-- get 100% off your next one." Riders are enrolled (auto or opt-in) and accumulate
-- progress; once they reach the requirement_count, a redemption row is minted.
-- Vouchers are visible to the rider and honored by tenant staff at checkout.

CREATE TABLE reward_program (
    id                          uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id                   uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    name                        text        NOT NULL,
    description                 text        NULL,
    enrollment_mode             text        NOT NULL CHECK (enrollment_mode IN ('auto','opt_in')),
    requirement_kind            text        NOT NULL CHECK (requirement_kind IN ('day_pass','event_ticket','any')),
    requirement_count           int         NOT NULL CHECK (requirement_count > 0),
    reward_percent_off          int         NOT NULL CHECK (reward_percent_off > 0 AND reward_percent_off <= 100),
    proximity_email_threshold   int         NULL CHECK (proximity_email_threshold IS NULL OR proximity_email_threshold > 0),
    is_active                   boolean     NOT NULL DEFAULT true,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    updated_at                  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_reward_program_tenant ON reward_program (tenant_id) WHERE is_active = true;

CREATE TABLE reward_enrollment (
    id                              uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    program_id                      uuid        NOT NULL REFERENCES reward_program(id) ON DELETE CASCADE,
    user_id                         uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    enrolled_at                     timestamptz NOT NULL DEFAULT now(),
    last_proximity_emailed_at_count int         NULL,                                    -- so we email once per threshold-cross
    UNIQUE (program_id, user_id)
);

CREATE INDEX idx_reward_enrollment_user ON reward_enrollment (user_id);

CREATE TABLE reward_redemption (
    id                  uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    program_id          uuid        NOT NULL REFERENCES reward_program(id) ON DELETE CASCADE,
    user_id             uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    earned_at           timestamptz NOT NULL DEFAULT now(),
    redeemed_at         timestamptz NULL,
    redeemed_on_kind    text        NULL,
    redeemed_on_id      uuid        NULL
);

CREATE INDEX idx_reward_redemption_user_unredeemed ON reward_redemption (user_id) WHERE redeemed_at IS NULL;
CREATE INDEX idx_reward_redemption_program ON reward_redemption (program_id);
