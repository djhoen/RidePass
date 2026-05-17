---
name: ridepass-migration
description: Scaffold a new RidePass numbered migration script under RidePass.Migrator/Scripts/. Picks the next four-digit number by globbing existing scripts, drops a templated .sql file with the standard header comment style and pattern boilerplate (ALTER, CREATE TABLE, trigger seeding, backfill), and reminds about backfilling existing tenants where relevant.
---

# RidePass Migration Scaffold

Create a new numbered migration script in `RidePass.Migrator/Scripts/` that follows the established conventions.

## Steps

### 1. Pick the next number

Glob `C:\Users\djhoe\source\repos\RidePass\RidePass.Migrator\Scripts\Script*.sql`. Find the highest existing 4-digit number and add 1. Always 4 digits, zero-padded (`Script0080`, never `Script80`).

### 2. Confirm the description with the user

If they didn't provide one in their prompt, ask. Use PascalCase, no spaces (e.g. `EventTicketRaceNumber`). Final filename: `Script####_<Description>.sql`.

### 3. Write the file

Drop the new file with a prose-heavy header comment that explains **why** the migration exists, not just what it does. Match the house style — see Script0074 (RedemptionAudit), Script0078 (TenantType), Script0079 (EventTicketRaceNumber) for tone.

Skeleton:

```sql
-- One-paragraph explanation of WHY this migration exists. What problem does
-- it solve, what design decisions are encoded, and any non-obvious tradeoff
-- a future reader would benefit from knowing.

-- <body — pick the right pattern below>
```

### 4. Pick the right body pattern

#### Add a column to an existing per-tenant table
```sql
ALTER TABLE <table>
    ADD COLUMN <name> <type> NULL;
-- If the new column has business meaning (not just NULL), consider whether
-- existing tenants need a backfill UPDATE. Add it here if so.
```

#### Create a new per-tenant table
```sql
CREATE TABLE <name> (
    id           uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id    uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    -- ... other columns ...
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX idx_<name>_tenant ON <name> (tenant_id);
CREATE TRIGGER trg_<name>_updated_at
    BEFORE UPDATE ON <name>
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
```

#### Seed-on-tenant-insert trigger (or modify one)
The canonical reference is `Script0078_TenantType.sql` (`seed_default_event_types`, `seed_initial_waiver`, `seed_default_pass_products`). When branching on `tenant_type`, use `IF/ELSE` and end every INSERT with `ON CONFLICT (tenant_id, code) DO NOTHING` so the trigger is idempotent.

#### Backfill for existing rows
Additive seeds: `INSERT ... ON CONFLICT DO NOTHING`.
Column fills: `UPDATE ... WHERE <field> IS NULL`.
Always confirm the backfill is safe against any tenant currently in production.

### 5. Report what you wrote

Brief: file path + one-line summary of what the migration does. If a backfill is required and you didn't include it, call that out explicitly so the user can either add it or confirm it's not needed.

## Conventions to match

- 4-digit script numbers, padded with zeros.
- Comments in prose, not bullet lists. Explain WHY, not WHAT.
- `ON CONFLICT` on every seeding INSERT so the script can be re-run.
- Triggers go at the end of the script, after the table they reference.
- `tenant_id uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE` is the standard tenant FK shape.
- Migrations are run in order; never depend on a script that hasn't been applied yet.

## Don't

- Don't run the migrator. The user runs it manually.
- Don't update C# entity / repository / DI / DTO yet — that's a separate decision after the SQL is reviewed.
- Don't number out of sequence even if there's a gap — always pick the next consecutive number.
