---
name: ridepass-migration
description: Author or edit a RidePass numbered migration under RidePass.Migrator/Scripts/. Picks the next four-digit number by globbing existing scripts, writes a templated .sql with the house header-comment style and the right body pattern (ALTER, CREATE TABLE, trigger seeding, backfill), and enforces the two hard rules every script must meet — it must be RERUNNABLE (idempotent) and BACKWARDS-COMPATIBLE (expand-then-contract) — plus backfilling existing tenants where relevant.
---

# RidePass Migration Scaffold

Create or edit a numbered migration script in `RidePass.Migrator/Scripts/` that follows the established conventions.

## Two hard rules (every migration must meet both)

Apply these whenever you write or edit any `.sql` under `RidePass.Migrator/Scripts/`, even a one-line ALTER.

### 1. Rerunnable (idempotent)

A script must be safe to execute more than once and produce the same end state without erroring. DbUp journals each script so it normally runs once, but idempotency matters anyway: dev/test DBs get reset and replayed, a partially-failed migration gets re-run, and scripts get renamed/re-journaled (a real source of pain here). Use the guarded forms:

- **Columns:** `ALTER TABLE t ADD COLUMN IF NOT EXISTS c ...`; `ALTER TABLE t DROP COLUMN IF EXISTS c`.
- **Tables / indexes:** `CREATE TABLE IF NOT EXISTS ...`; `CREATE INDEX IF NOT EXISTS ...`; `DROP TABLE IF EXISTS ...`.
- **Functions / views:** `CREATE OR REPLACE FUNCTION` / `CREATE OR REPLACE VIEW`.
- **Triggers:** `DROP TRIGGER IF EXISTS trg ON t;` immediately before `CREATE TRIGGER trg ...` (Postgres has no `CREATE OR REPLACE TRIGGER` before PG14).
- **Enum values:** `ALTER TYPE e ADD VALUE IF NOT EXISTS 'x'`.
- **Constraints** (no `IF NOT EXISTS` on `ADD CONSTRAINT`): guard with a `DO` block —
  ```sql
  DO $$ BEGIN
      IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'my_constraint') THEN
          ALTER TABLE t ADD CONSTRAINT my_constraint CHECK (...);
      END IF;
  END $$;
  ```
- **Seeding inserts:** `INSERT ... ON CONFLICT (...) DO NOTHING` (or `WHERE NOT EXISTS`).
- **Backfills:** `UPDATE ... WHERE col IS NULL` (only touches unfilled rows, so re-running is a no-op).

### 2. Backwards-compatible where possible (expand-then-contract)

The currently-deployed app keeps running against the new schema during/after a deploy, so a migration must not break the old code that's still serving traffic. Prefer additive change; split breaking change across releases.

- **Default to additive:** new nullable columns and new tables are always safe.
- **New `NOT NULL` column:** add it `WITH a DEFAULT` (so existing rows and old inserts that omit it still work), or do it in two steps — add nullable + backfill now, set `NOT NULL` in a *later* migration once code populates it.
- **Renames / drops / type narrowing are breaking.** Do not rename or drop a column/table the deployed app still reads, and don't narrow a type. Use expand-then-contract: (1) add the new shape + backfill + have code dual-write/read; (2) ship the code that stops using the old shape; (3) a *later* migration drops the old column/table.
- **Widening** (e.g. `int` -> `bigint`, longer `varchar`) is safe; **narrowing** is not.
- **Adding a constraint** existing data could violate: add `NOT VALID` then `VALIDATE CONSTRAINT` in a follow-up, or fix the data first.
- If a truly breaking change is unavoidable in one step, say so explicitly in your end-of-turn summary and explain why expand-then-contract wasn't feasible.

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
    ADD COLUMN IF NOT EXISTS <name> <type> NULL;
-- If the new column has business meaning (not just NULL), consider whether
-- existing tenants need a backfill UPDATE (UPDATE ... WHERE <name> IS NULL).
-- A NOT NULL column must carry a DEFAULT (or be backfilled then set NOT NULL
-- in a later migration) so old code that inserts without it keeps working.
```

#### Create a new per-tenant table
```sql
CREATE TABLE IF NOT EXISTS <name> (
    id           uuid        PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id    uuid        NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    -- ... other columns ...
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_<name>_tenant ON <name> (tenant_id);
DROP TRIGGER IF EXISTS trg_<name>_updated_at ON <name>;
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

- 4-digit script numbers, padded with zeros, **unique** (never reuse a number — two scripts sharing `Script0154_` is a real bug we hit; if you find a collision, renumber the newer one to the next free number).
- Comments in prose, not bullet lists. Explain WHY, not WHAT.
- Every statement uses its guarded/idempotent form (see "Two hard rules") so the whole script is rerunnable.
- `ON CONFLICT` on every seeding INSERT; `WHERE <col> IS NULL` on every backfill.
- Triggers go at the end of the script, after the table they reference, preceded by `DROP TRIGGER IF EXISTS`.
- `tenant_id uuid NOT NULL REFERENCES tenant(id) ON DELETE CASCADE` is the standard tenant FK shape.
- Migrations are run in order; never depend on a script that hasn't been applied yet.
- Prefer additive/backwards-compatible change; split a breaking change across releases (expand-then-contract).

## Don't

- Don't run the migrator. The user runs it manually.
- Don't update C# entity / repository / DI / DTO yet — that's a separate decision after the SQL is reviewed.
- Don't number out of sequence even if there's a gap — always pick the next consecutive number.
