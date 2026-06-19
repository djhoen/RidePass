#!/usr/bin/env bash
# One-time bootstrap for a FRESH Ubuntu 24.04 staging droplet. Run as root:
#   ssh root@<stage-droplet-ip> 'bash -s' < scripts/provision-stage.sh
# (or paste it into the droplet's "user data" at create time).
#
# Installs the runtime stack, creates the deploy user + /var/www/staging, and drops
# the nginx stage site (left DISABLED until the wildcard cert exists). It does NOT
# write secrets and does NOT obtain the cert; those final steps are printed at the end.
#
# Reviewed-by-you bootstrap: it makes standard choices (Node 20, .NET 10 SDK, pm2).
set -euo pipefail

DEPLOY_USER=deploy
DEPLOY_PATH=/var/www/staging
# Public half of the "djhoe-windows - RidePass Deploy Key" (DO key id 55927706).
# The matching PRIVATE key must be the STAGE_DEPLOY_SSH_KEY GitHub secret.
DEPLOY_PUBKEY='ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAILwXvjdjVr8jfFMTO2ZgCHMBPeYgUtt2TVbQWTWe38Hh djhoen@gmail.com ridepass-deploy'

echo "==> apt packages (nginx, certbot + DO DNS plugin, build tools)"
export DEBIAN_FRONTEND=noninteractive
apt-get update -y
apt-get install -y nginx certbot python3-certbot-dns-digitalocean curl git ufw

echo "==> Node 20 (NodeSource)"
if ! command -v node >/dev/null 2>&1; then
    curl -fsSL https://deb.nodesource.com/setup_20.x | bash -
    apt-get install -y nodejs
fi

echo "==> .NET 10 SDK"
if [ ! -x /usr/local/bin/dotnet ]; then
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet
    ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet
fi

echo "==> pm2 (global)"
command -v pm2 >/dev/null 2>&1 || npm install -g pm2

echo "==> deploy user + app dir"
id -u "$DEPLOY_USER" >/dev/null 2>&1 || adduser --disabled-password --gecos "" "$DEPLOY_USER"
install -d -m 700 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "/home/$DEPLOY_USER/.ssh"
touch "/home/$DEPLOY_USER/.ssh/authorized_keys"
grep -qF "$DEPLOY_PUBKEY" "/home/$DEPLOY_USER/.ssh/authorized_keys" \
    || echo "$DEPLOY_PUBKEY" >> "/home/$DEPLOY_USER/.ssh/authorized_keys"
chown -R "$DEPLOY_USER:$DEPLOY_USER" "/home/$DEPLOY_USER/.ssh"
chmod 600 "/home/$DEPLOY_USER/.ssh/authorized_keys"
install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" "$DEPLOY_PATH"
install -d /var/www/certbot

echo "==> pm2 startup on boot (as $DEPLOY_USER)"
env PATH="$PATH:/usr/bin" pm2 startup systemd -u "$DEPLOY_USER" --hp "/home/$DEPLOY_USER" >/dev/null || true

echo "==> firewall (allow SSH + HTTP/HTTPS)"
ufw allow OpenSSH >/dev/null 2>&1 || true
ufw allow 'Nginx Full' >/dev/null 2>&1 || true
yes | ufw enable >/dev/null 2>&1 || true

echo "==> nginx stage site (written but NOT enabled until the cert exists)"
cat > /etc/nginx/sites-available/ridepass-stage <<'NGINX'
server {
    listen 80;
    listen [::]:80;
    server_name stage.ridepass.io *.stage.ridepass.io;
    location /.well-known/acme-challenge/ { root /var/www/certbot; }
    location / { return 301 https://$host$request_uri; }
}
server {
    listen 443 ssl http2;
    listen [::]:443 ssl http2;
    server_name stage.ridepass.io *.stage.ridepass.io;

    ssl_certificate     /etc/letsencrypt/live/stage.ridepass.io/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/stage.ridepass.io/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers off;
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
    add_header X-Content-Type-Options nosniff always;
    add_header Referrer-Policy strict-origin-when-cross-origin always;
    client_max_body_size 25M;

    location ~ ^/(api|uploads)/ {
        proxy_pass http://127.0.0.1:7293;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Host $host;
        proxy_read_timeout 90s;
    }

    location ^~ /embed/ {
        auth_request /__embed_csp;
        auth_request_set $embed_frame_ancestors $upstream_http_x_embed_frame_ancestors;
        add_header Content-Security-Policy "frame-ancestors $embed_frame_ancestors" always;
        add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
        add_header X-Content-Type-Options nosniff always;
        add_header Referrer-Policy strict-origin-when-cross-origin always;
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Host $host;
    }
    location = /__embed_csp {
        internal;
        proxy_pass http://127.0.0.1:7293/api/Embed/FrameAncestors;
        proxy_pass_request_body off;
        proxy_set_header Content-Length "";
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Host $host;
    }
}
NGINX

cat <<DONE

============================================================
Bootstrap done. Remaining manual steps (need your secrets):

1) Wildcard cert (DNS-01 via the DigitalOcean plugin). Create a DO API token, then:
     printf 'dns_digitalocean_token = %s\n' "<DO_API_TOKEN>" > /root/.certbot-do.ini
     chmod 600 /root/.certbot-do.ini
     certbot certonly --dns-digitalocean \\
       --dns-digitalocean-credentials /root/.certbot-do.ini \\
       -d 'stage.ridepass.io' -d '*.stage.ridepass.io'

2) Enable the nginx site (only after the cert exists):
     ln -sf /etc/nginx/sites-available/ridepass-stage /etc/nginx/sites-enabled/ridepass-stage
     nginx -t && systemctl reload nginx

3) Create /etc/ridepass/staging.env from the repo's staging.env.example
   (Stripe TEST keys, the ridepass_stage connection string, fresh secrets):
     mkdir -p /etc/ridepass && chmod 600 /etc/ridepass/staging.env

4) Back on your machine, set the GitHub secrets and push to 'stage':
     gh secret set STAGE_DEPLOY_HOST   --body "<this droplet IP>"
     gh secret set STAGE_DEPLOY_SSH_KEY < ~/.ssh/ridepass_deploy   # the PRIVATE key
============================================================
DONE
