#!/usr/bin/env bash
# Deploy the working tree to the RidePass production droplet.
# Run from anywhere: bash scripts/deploy.sh
#
# Steps: transfer source -> build vueapp -> publish .NET -> migrate -> pm2 restart
# Brief downtime (~30s) during pm2 restart at the end.
set -euo pipefail

DEPLOY_HOST="146.190.48.200"
DEPLOY_USER="deploy"
SSH_KEY="$HOME/.ssh/ridepass_deploy"
DEPLOY_PATH="/var/www/production"
ENV_FILE="/etc/ridepass/production.env"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SSH_OPTS="-i $SSH_KEY -o StrictHostKeyChecking=accept-new"

cd "$REPO_ROOT"

log() { printf '\n\033[1;36m=== %s ===\033[0m\n' "$*"; }

log "1/5 Transfer source to $DEPLOY_USER@$DEPLOY_HOST:$DEPLOY_PATH"
tar -czf - \
  --exclude=bin --exclude=obj --exclude=publish \
  --exclude=node_modules --exclude=dist \
  --exclude=.git --exclude=.vs \
  --exclude='*.user' --exclude='.vite' \
  --exclude=Template.sln \
  -C "$REPO_ROOT" . | \
ssh $SSH_OPTS "$DEPLOY_USER@$DEPLOY_HOST" "
  set -e
  # Preserve tenant uploads (logos, hero images) across deploys
  if [[ -d '$DEPLOY_PATH/webapi/publish/wwwroot' ]]; then
    rm -rf /tmp/ridepass-wwwroot.bak
    mv '$DEPLOY_PATH/webapi/publish/wwwroot' /tmp/ridepass-wwwroot.bak
  fi
  find '$DEPLOY_PATH' -mindepth 1 -delete
  tar -xzf - -C '$DEPLOY_PATH'
  if [[ -d /tmp/ridepass-wwwroot.bak ]]; then
    mkdir -p '$DEPLOY_PATH/webapi/publish'
    mv /tmp/ridepass-wwwroot.bak '$DEPLOY_PATH/webapi/publish/wwwroot'
  fi
  echo \"  -> \$(du -sh '$DEPLOY_PATH' | cut -f1) on disk\"
"

log "2/5 Build vueapp (npm install + npm run build)"
ssh $SSH_OPTS "$DEPLOY_USER@$DEPLOY_HOST" "
  set -e
  cd '$DEPLOY_PATH/vueapp'
  npm install --no-fund --no-audit --silent
  npm run build 2>&1 | tail -3
"

log "3/5 Publish .NET projects (Release)"
ssh $SSH_OPTS "$DEPLOY_USER@$DEPLOY_HOST" "
  set -e
  cd '$DEPLOY_PATH'
  for proj in webapi/webapi.csproj TaskRunner/TaskRunner.csproj RidePass.Migrator/RidePass.Migrator.csproj; do
    out=\"\$(dirname \$proj)/publish\"
    dotnet publish \"\$proj\" -c Release -o \"\$out\" --nologo -v minimal | tail -2
  done
"

log "4/5 Run database migrations (idempotent)"
ssh $SSH_OPTS "$DEPLOY_USER@$DEPLOY_HOST" "
  set -e
  cd '$DEPLOY_PATH'
  set -a; source '$ENV_FILE'; set +a
  dotnet RidePass.Migrator/publish/RidePass.Migrator.dll
"

log "5/5 Restart pm2 with refreshed env"
ssh $SSH_OPTS "$DEPLOY_USER@$DEPLOY_HOST" "
  set -e
  cd '$DEPLOY_PATH'
  set -a; source '$ENV_FILE'; set +a
  pm2 startOrRestart ecosystem.config.js --update-env
  pm2 save --silent
  pm2 list
"

log "Health gate (fail the deploy if the API doesn't come up)"
# /api/health is proxied through nginx to Kestrel, so a 200 here means the API
# process actually booted (this is what would have caught the missing-env 502).
healthy=0
for i in $(seq 1 30); do
  code=$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 https://ridepass.io/api/health || echo 000)
  if [ "$code" = "200" ]; then echo "  API healthy after $i checks."; healthy=1; break; fi
  echo "  check $i: API HTTP $code, retrying in 3s..."
  sleep 3
done
if [ "$healthy" != "1" ]; then
  log "DEPLOY FAILED: API did not become healthy (~90s). Recent webapi logs:"
  ssh $SSH_OPTS "$DEPLOY_USER@$DEPLOY_HOST" "pm2 logs webapi --lines 40 --nostream || true"
  exit 1
fi

log "Smoke test (SPA shell)"
curl -sS -o /dev/null -w "  apex   https://ridepass.io/      HTTP %{http_code}\n" --max-time 15 https://ridepass.io/ || echo "  apex curl failed"
curl -sS -o /dev/null -w "  tenant https://demo.ridepass.io/ HTTP %{http_code}\n" --max-time 15 https://demo.ridepass.io/ || echo "  tenant curl failed"

log "Deploy complete"
