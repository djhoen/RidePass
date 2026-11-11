-- Rider height and weight, so the bike shop can size a rental bike before the rider turns up.
-- An adult's numbers sit on their platform-wide account (users), like their name and birthdate, so
-- every venue they ride at sees and updates the same values; a minor's sit on the venue's rider_dependent row.
--
-- Highland's go-live list: "ask height and weight when a bike is rented with a lesson" and "ask it at
-- the lessons kiosk". The numbers belong to the RIDER, not to an order, so they live on the rider's
-- profile: the users row for an adult rider account, the rider_dependent row (Script0309) for a minor
-- under a guardian. measured_at is when they were last entered, so staff can tell a stale number from a
-- fresh one (kids grow). Whole inches and whole pounds: that is how riders know them and how a size chart
-- reads; the CHECK ranges (24-96 in, 20-450 lb) only reject typos (a height typed in cm, a weight in kg
-- is still in range, which is why the UI asks in ft/in + lb).
--
-- A rental also keeps a SNAPSHOT (shop_rental.rider_height_in / rider_weight_lb): the size the shop set the
-- bike up for on that day, which must not change when the rider later edits their profile. Lesson bikes are
-- shop_rental rows (Script0186); the old rental_purchase table was retired in Script0200, so the snapshot
-- goes on shop_rental.
--
-- kiosk_signup.height_in / weight_lb hold what a walk-up typed at the kiosk while the sign-up waits for an
-- ID check (it matched an existing account); they are copied to the account when staff confirm it.
--
-- tenant.kiosk_height_weight_mode: whether the kiosk sign-up asks for them. off | optional | required,
-- default off so no venue's kiosk changes until it opts in.
--
-- Additive and nullable (the mode carries a DEFAULT): the deployed app keeps working.
-- Rerunnable: ADD COLUMN IF NOT EXISTS, DO-block guarded constraints.

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS height_in   int         NULL,
    ADD COLUMN IF NOT EXISTS weight_lb   int         NULL,
    ADD COLUMN IF NOT EXISTS measured_at timestamptz NULL;

ALTER TABLE rider_dependent
    ADD COLUMN IF NOT EXISTS height_in   int         NULL,
    ADD COLUMN IF NOT EXISTS weight_lb   int         NULL,
    ADD COLUMN IF NOT EXISTS measured_at timestamptz NULL;

ALTER TABLE shop_rental
    ADD COLUMN IF NOT EXISTS rider_height_in int NULL,
    ADD COLUMN IF NOT EXISTS rider_weight_lb int NULL;

ALTER TABLE kiosk_signup
    ADD COLUMN IF NOT EXISTS height_in int NULL,
    ADD COLUMN IF NOT EXISTS weight_lb int NULL;

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS kiosk_height_weight_mode text NOT NULL DEFAULT 'off';

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_users_height_in') THEN
        ALTER TABLE users ADD CONSTRAINT chk_users_height_in CHECK (height_in IS NULL OR height_in BETWEEN 24 AND 96);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_users_weight_lb') THEN
        ALTER TABLE users ADD CONSTRAINT chk_users_weight_lb CHECK (weight_lb IS NULL OR weight_lb BETWEEN 20 AND 450);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_rider_dependent_height_in') THEN
        ALTER TABLE rider_dependent ADD CONSTRAINT chk_rider_dependent_height_in CHECK (height_in IS NULL OR height_in BETWEEN 24 AND 96);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_rider_dependent_weight_lb') THEN
        ALTER TABLE rider_dependent ADD CONSTRAINT chk_rider_dependent_weight_lb CHECK (weight_lb IS NULL OR weight_lb BETWEEN 20 AND 450);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_shop_rental_rider_height_in') THEN
        ALTER TABLE shop_rental ADD CONSTRAINT chk_shop_rental_rider_height_in CHECK (rider_height_in IS NULL OR rider_height_in BETWEEN 24 AND 96);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_shop_rental_rider_weight_lb') THEN
        ALTER TABLE shop_rental ADD CONSTRAINT chk_shop_rental_rider_weight_lb CHECK (rider_weight_lb IS NULL OR rider_weight_lb BETWEEN 20 AND 450);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_kiosk_signup_height_in') THEN
        ALTER TABLE kiosk_signup ADD CONSTRAINT chk_kiosk_signup_height_in CHECK (height_in IS NULL OR height_in BETWEEN 24 AND 96);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_kiosk_signup_weight_lb') THEN
        ALTER TABLE kiosk_signup ADD CONSTRAINT chk_kiosk_signup_weight_lb CHECK (weight_lb IS NULL OR weight_lb BETWEEN 20 AND 450);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tenant_kiosk_height_weight_mode') THEN
        ALTER TABLE tenant ADD CONSTRAINT chk_tenant_kiosk_height_weight_mode
            CHECK (kiosk_height_weight_mode IN ('off', 'optional', 'required'));
    END IF;
END $$;
