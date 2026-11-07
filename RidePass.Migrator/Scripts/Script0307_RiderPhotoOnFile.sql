-- Gate check-in: require a rider photo on file.
--
-- 1. Tenant policy. rider_photo_mode:
--      'off'      nothing changes (default, so every existing tenant keeps today's behaviour);
--      'prompt'   the gate shows "No photo on file" with a Take photo button, but admits;
--      'required' the gate refuses a rider-audience ticket or pass with no photo on file.
--
-- 2. rider_photo: a tenant's photo of a RIDER, taken at the gate, keyed like rider_id_verification
--    (Script0305): the buying account plus the normalized rider / holder name (rider_key), so a
--    parent's account holds one photo per child and the child's later tickets and passes find it.
--    A credential bought without an account is keyed by its buyer email instead (owner_key
--    'email:<lowercased email>'); owner_key is 'user:<account id>' otherwise. A credential that names no
--    rider and has no owner to key by is filed under the credential itself ('ticket:<id>' / 'pass:<id>',
--    rider_key 'credential'), so a photo taken for it still satisfies the gate for that credential.
--    The image itself is NOT in the database: storage_key is an opaque key into the image store's
--    PRIVATE area (never a public URL); the API serves it to authorized staff only.
--
-- Additive: the new column carries a default, the new table is unused by older code.
-- Rerunnable: ADD COLUMN IF NOT EXISTS, guarded / re-created constraints, CREATE TABLE / INDEX IF NOT
-- EXISTS, CREATE OR REPLACE FUNCTION, DROP / CREATE TRIGGER.

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS rider_photo_mode text NOT NULL DEFAULT 'off';

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tenant_rider_photo_mode') THEN
        ALTER TABLE tenant ADD CONSTRAINT chk_tenant_rider_photo_mode
            CHECK (rider_photo_mode IN ('off', 'prompt', 'required'));
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS rider_photo (
    id                uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id         uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    owner_key         text NOT NULL,
    user_id           uuid NULL REFERENCES users(id) ON DELETE CASCADE,
    owner_email       text NULL,
    rider_key         text NOT NULL,
    storage_key       text NOT NULL,
    taken_at          timestamptz NOT NULL DEFAULT now(),
    taken_by_user_id  uuid NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_rider_photo_owner CHECK (user_id IS NOT NULL OR owner_email IS NOT NULL
        OR owner_key LIKE 'ticket:%' OR owner_key LIKE 'pass:%')
);

CREATE UNIQUE INDEX IF NOT EXISTS uk_rider_photo_tenant_owner_rider
    ON rider_photo (tenant_id, owner_key, rider_key);

-- A database that ran an earlier draft of this script (unpushed) has the narrower owner check.
ALTER TABLE rider_photo DROP CONSTRAINT IF EXISTS chk_rider_photo_owner;
ALTER TABLE rider_photo ADD CONSTRAINT chk_rider_photo_owner CHECK (user_id IS NOT NULL OR owner_email IS NOT NULL
    OR owner_key LIKE 'ticket:%' OR owner_key LIKE 'pass:%');

-- 3. private_image_trash: images whose rider_photo row went away (deleted directly, or by the cascade
--    when an account or tenant is deleted) or was pointed at a new image. A trigger queues the old
--    storage key; the API's PrivateImageTrashWorker deletes the image and the queue row. So no
--    deletion path, current or future, can leave a rider's photo behind in storage.
CREATE TABLE IF NOT EXISTS private_image_trash (
    id           bigserial PRIMARY KEY,
    tenant_id    uuid NOT NULL,          -- no FK: the tenant may be the thing being deleted
    storage_key  text NOT NULL,
    queued_at    timestamptz NOT NULL DEFAULT now()
);

CREATE OR REPLACE FUNCTION rider_photo_trash_image() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        INSERT INTO private_image_trash (tenant_id, storage_key) VALUES (OLD.tenant_id, OLD.storage_key);
        RETURN OLD;
    END IF;
    IF NEW.storage_key IS DISTINCT FROM OLD.storage_key THEN
        INSERT INTO private_image_trash (tenant_id, storage_key) VALUES (OLD.tenant_id, OLD.storage_key);
    END IF;
    RETURN NEW;
END $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_rider_photo_trash_image ON rider_photo;
CREATE TRIGGER trg_rider_photo_trash_image
    AFTER DELETE OR UPDATE OF storage_key ON rider_photo
    FOR EACH ROW EXECUTE FUNCTION rider_photo_trash_image();
