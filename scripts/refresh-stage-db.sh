#!/usr/bin/env bash
# Refresh the STAGING database from a production dump, then sanitize it.
#
#   PROD_DB_URL=postgresql://readonly_user:...@prod-host:25060/ridepass?sslmode=require \
#   STAGE_DB_URL=postgresql://ridepass_stage_app:...@host:25060/ridepass_stage?sslmode=require \
#   bash scripts/refresh-stage-db.sh
#
# Use a READ-ONLY prod user for PROD_DB_URL. Requires pg_dump / pg_restore / psql
# (matching the cluster's major version) on the machine you run this from.
set -euo pipefail

: "${PROD_DB_URL:?set PROD_DB_URL (read-only production connection string)}"
: "${STAGE_DB_URL:?set STAGE_DB_URL (staging connection string)}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Safety: refuse unless the TARGET clearly looks like staging, so this can never be
# pointed at prod by mistake. (The stage DB is named ridepass_stage.)
case "$STAGE_DB_URL" in
  *stage*) ;;
  *) echo "REFUSING: STAGE_DB_URL does not contain 'stage'. Aborting to protect prod." >&2; exit 1 ;;
esac
if [ "$PROD_DB_URL" = "$STAGE_DB_URL" ]; then
  echo "REFUSING: PROD_DB_URL and STAGE_DB_URL are identical." >&2; exit 1
fi

DUMP="$(mktemp)"   # pg_dump custom format; extension doesn't matter
trap 'rm -f "$DUMP"' EXIT

echo "==> [1/4] Dumping production (custom format)..."
pg_dump "$PROD_DB_URL" --no-owner --no-privileges --format=custom --file "$DUMP"

echo "==> [2/4] Resetting staging schema..."
psql "$STAGE_DB_URL" -v ON_ERROR_STOP=1 -c "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;"

echo "==> [3/4] Restoring into staging..."
# Benign warnings (extensions, missing roles) can make pg_restore exit non-zero;
# the sanitize step below (ON_ERROR_STOP) is the real correctness gate.
pg_restore --no-owner --no-privileges --dbname "$STAGE_DB_URL" "$DUMP" \
  || echo "    (pg_restore reported warnings; continuing to sanitize)"

echo "==> [4/4] Sanitizing staging (scrub PII, null cloned credentials)..."
psql "$STAGE_DB_URL" -v ON_ERROR_STOP=1 -f "$SCRIPT_DIR/sanitize-stage.sql"

echo "==> Done. Staging refreshed from prod and sanitized."
echo "    Reminder: staging must use Stripe TEST keys and have Twilio/email disabled."
