-- Per-line approve/decline on work orders (estimates especially): the customer okays or refuses each
-- line, with who recorded it and when. A declined line is never consumed from stock and never billed;
-- approved and still-pending lines proceed as before. Additive, rerunnable, backwards-compatible
-- (every existing line defaults to 'pending', i.e. no decision recorded, which behaves as today).

ALTER TABLE shop_work_order_line
    ADD COLUMN IF NOT EXISTS approval_status text NOT NULL DEFAULT 'pending';

ALTER TABLE shop_work_order_line
    ADD COLUMN IF NOT EXISTS approval_at timestamptz;

ALTER TABLE shop_work_order_line
    ADD COLUMN IF NOT EXISTS approval_by_user_id uuid REFERENCES users(id);

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'shop_wol_approval_check') THEN
        ALTER TABLE shop_work_order_line
            ADD CONSTRAINT shop_wol_approval_check
            CHECK (approval_status IN ('pending', 'approved', 'declined'));
    END IF;
END $$;
