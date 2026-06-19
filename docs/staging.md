# RidePass staging environment

Staging mirrors production cheaply (~$6/mo) so features can be tested on real-shaped
data before they hit prod. It lives at `stage.ridepass.io` (+ `*.stage.ridepass.io`)
and deploys on push to the `stage` branch.

## How it works

- **No app code changes.** Tenant resolution is root-domain-configurable on both sides
  (`Tenant:RootDomain` server-side in `TenantResolutionMiddleware`, `VITE_ROOT_DOMAIN`
  client-side in `TenantHelper`). Staging just sets those to `stage.ridepass.io`, so
  `demo.stage.ridepass.io` resolves to tenant `demo`.
- **Database:** a separate `ridepass_stage` database on the SAME managed cluster as prod
  (`ridepass-db`). An extra database on an existing cluster is free; it shares the
  1 vCPU / 1 GB with prod, which is fine for low-traffic staging.
- **Compute:** a dedicated $6 droplet running its own nginx + pm2, ports mirroring prod
  (Vue `8080`, Kestrel `7293`).
- **Deploy:** push to `stage` triggers `.github/workflows/deploy-stage-action.yml`, which
  mirrors the prod deploy but targets `/var/www/staging`, sources
  `/etc/ridepass/staging.env`, builds the SPA with `--mode staging`, runs migrations, and
  restarts `ecosystem.stage.config.js`.

## Repo pieces (already in place)

| File | Purpose |
|---|---|
| `vueapp/.env.staging` | `VITE_ROOT_DOMAIN=stage.ridepass.io`, `VITE_API_ENDPOINT=/api` (loaded by `--mode staging`) |
| `ecosystem.stage.config.js` | pm2 apps `stage-vueapp` / `stage-taskrunner` / `stage-webapi` (secrets come from `staging.env`) |
| `.github/workflows/deploy-stage-action.yml` | deploy on push to `stage` |
| `staging.env.example` | secret-free checklist for `/etc/ridepass/staging.env` |
| `scripts/sanitize-stage.sql` | scrub PII + null cloned Stripe/Twilio creds (keeps super-admin logins) |
| `scripts/refresh-stage-db.sh` | dump prod -> restore stage -> sanitize |

## One-time setup (account-level, not in the repo)

1. **Droplet.** Create a $6 droplet (or colocate on prod). Install nginx, pm2, the .NET
   SDK, and Node. Create `/var/www/staging` owned by the `deploy` user.
2. **Database.** In the DO console, on the existing `ridepass-db` cluster, create database
   `ridepass_stage` and user `ridepass_stage_app`. (Free; do not create a new cluster.)
3. **DNS.** Add A records for `stage.ridepass.io` and `*.stage.ridepass.io` pointing at the
   stage droplet IP.
4. **TLS.** Issue a Let's Encrypt **wildcard cert for `*.stage.ridepass.io`** via the DNS-01
   challenge. The production `*.ridepass.io` wildcard does NOT cover the nested label.
   ```bash
   sudo certbot certonly --manual --preferred-challenges dns \
     -d 'stage.ridepass.io' -d '*.stage.ridepass.io'
   ```
5. **nginx.** Add a server block with `server_name stage.ridepass.io *.stage.ridepass.io;`
   using the stage cert. Copy the structure from `scripts/nginx-ridepass.conf.template`,
   including the `/embed/` + `/__embed_csp` CSP blocks. Then `sudo nginx -t && sudo systemctl reload nginx`.
6. **Env file.** Create `/etc/ridepass/staging.env` (root-owned, `chmod 600`) from
   `staging.env.example`. Use the `ridepass_stage` connection string, `Tenant__RootDomain=stage.ridepass.io`,
   **Stripe test keys**, fresh stage `Jwt`/`Encryption` secrets, and leave Twilio + email off.
7. **GitHub secrets.** Add `STAGE_DEPLOY_HOST` (stage droplet IP) and `STAGE_DEPLOY_SSH_KEY`
   (a private key authorized for `deploy@<stage droplet>`). Keep them separate from the prod
   `DEPLOY_*` secrets.
8. **First data load.** Run the refresh (see below) to populate and sanitize the stage DB.

## Refreshing stage data from prod

Run whenever you want stage to mirror current prod. Use a read-only prod user.

```bash
PROD_DB_URL='postgresql://readonly_user:...@ridepass-db-...:25060/ridepass?sslmode=require' \
STAGE_DB_URL='postgresql://ridepass_stage_app:...@ridepass-db-...:25060/ridepass_stage?sslmode=require' \
bash scripts/refresh-stage-db.sh
```

What it does: `pg_dump` prod -> drop/recreate the stage `public` schema -> `pg_restore` ->
run `sanitize-stage.sql`. The sanitize step scrubs PII (emails redacted, phones/contacts
nulled), nulls cloned Stripe Connect/Terminal and Twilio identifiers, and preserves
super-admin rows so you can still log in. It refuses to run unless the target DB name
contains `stage`.

After a refresh, stage tenants have no Stripe Connect account (nulled), so re-onboard a
**test-mode** Connect account in stage if you need to exercise checkout end to end.

### Refreshing from the super-admin UI

There's also an in-app button: super-admin → **Misc settings** → **Staging data** →
"Refresh staging from production". It runs the same `refresh-stage-db.sh` server-side as a
background job and shows live progress. It is rendered/usable ONLY when the server is the
staging environment (`ASPNETCORE_ENVIRONMENT=Staging`) with `StageMirror:Enabled=true`; on
production the endpoint 403s and the control is hidden.

Setup for the in-app utility (one-time):

1. Add `StageMirror__*` to `/etc/ridepass/staging.env` (see `staging.env.example`): `Enabled=true`,
   `SourceUrl` (read-only prod libpq URI), `TargetUrl` (the `ridepass_stage` libpq URI; must
   contain "stage").
2. The droplet needs the **PostgreSQL 17** client (`pg_dump` must match the PG17 cluster).
   `provision-stage.sh` installs `postgresql-client-17` from PGDG; on an already-provisioned
   box run that block manually.
3. Grant the stage app user what the refresh needs, as `doadmin`, once (the refresh drops and
   recreates the `public` schema and the `uuid-ossp` extension):
   ```sql
   GRANT CREATE ON DATABASE ridepass_stage TO ridepass_stage_app;
   ALTER SCHEMA public OWNER TO ridepass_stage_app;
   ```
   If `ALTER SCHEMA ... OWNER` errors with "must be member of role" (DO's doadmin isn't a full
   superuser), the app user can't drop the schema; switch `refresh-stage-db.sh` to a
   `pg_restore --clean --if-exists` reset instead of `DROP SCHEMA`.

## Day-to-day flow

```
feature branch  ->  merge to `stage`  ->  auto-deploy to *.stage.ridepass.io  ->  verify
                ->  merge to `master` ->  auto-deploy to prod
```

## Guardrails

- Staging must use **Stripe test keys** and have **Twilio/email disabled** so it can never
  charge a card, text a rider, or email a real customer, even with cloned prod data.
- `refresh-stage-db.sh` and `sanitize-stage.sql` both refuse to touch a database whose name
  doesn't look like staging.
- Staging shares the prod DB cluster's CPU/RAM. Avoid load tests against stage that could
  pressure prod; if it becomes an issue, bump the cluster tier or split stage onto its own.
