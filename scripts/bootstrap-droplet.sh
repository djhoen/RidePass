#!/usr/bin/env bash
set -euo pipefail

# Idempotent bootstrap for the RidePass droplet.
# Run as root on a fresh Ubuntu 24.04 droplet:
#   scp -i ~/.ssh/ridepass_deploy scripts/bootstrap-droplet.sh root@<ip>:/root/
#   ssh -i ~/.ssh/ridepass_deploy root@<ip> 'bash /root/bootstrap-droplet.sh'

log() { printf '\n=== %s ===\n' "$*"; }

if [[ $EUID -ne 0 ]]; then
  echo "Must run as root" >&2; exit 1
fi

export DEBIAN_FRONTEND=noninteractive

log "Set timezone to UTC"
timedatectl set-timezone UTC

log "Add 2GB swap (if missing)"
if [[ ! -f /swapfile ]]; then
  fallocate -l 2G /swapfile
  chmod 600 /swapfile
  mkswap /swapfile >/dev/null
  swapon /swapfile
  grep -q '^/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
  sysctl -w vm.swappiness=10 >/dev/null
  grep -q '^vm.swappiness' /etc/sysctl.conf || echo 'vm.swappiness=10' >> /etc/sysctl.conf
fi

log "apt update + base packages"
apt-get update -qq
apt-get install -yqq curl ca-certificates gnupg apt-transport-https \
  ufw fail2ban git build-essential unattended-upgrades

log "Configure unattended security upgrades"
dpkg-reconfigure -f noninteractive unattended-upgrades >/dev/null

log "UFW: allow 22, 80, 443; deny everything else"
ufw --force reset >/dev/null
ufw default deny incoming
ufw default allow outgoing
ufw allow OpenSSH >/dev/null
ufw allow 80/tcp >/dev/null
ufw allow 443/tcp >/dev/null
ufw --force enable

log "fail2ban: enable for SSH"
systemctl enable --now fail2ban

log "Install .NET 10 ASP.NET Core runtime (Microsoft repo)"
if ! command -v dotnet >/dev/null || ! dotnet --list-runtimes 2>/dev/null | grep -q 'Microsoft.AspNetCore.App 10\.'; then
  wget -q https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O /tmp/ms-prod.deb
  dpkg -i /tmp/ms-prod.deb
  rm /tmp/ms-prod.deb
  apt-get update -qq
  apt-get install -yqq aspnetcore-runtime-10.0
fi
dotnet --list-runtimes

log "Install Node.js 22 LTS (NodeSource)"
if ! command -v node >/dev/null || [[ "$(node -v)" != v22* ]]; then
  curl -fsSL https://deb.nodesource.com/setup_22.x | bash -
  apt-get install -yqq nodejs
fi
node -v && npm -v

log "Install pm2 globally"
npm install -g pm2 >/dev/null
pm2 -v

log "Install nginx"
apt-get install -yqq nginx
systemctl enable --now nginx

log "Install certbot (snap)"
if ! command -v snap >/dev/null; then
  apt-get install -yqq snapd
  systemctl enable --now snapd.socket
  ln -sf /var/lib/snapd/snap /snap
fi
snap install core >/dev/null 2>&1 || true
snap refresh core >/dev/null 2>&1 || true
snap install --classic certbot >/dev/null 2>&1 || true
ln -sf /snap/bin/certbot /usr/bin/certbot
# DNS provider plugin is intentionally not installed yet; choose one later:
#   snap set certbot trust-plugin-with-root=ok
#   snap install certbot-dns-digitalocean   # if DNS lives on DO
#   snap install certbot-dns-cloudflare     # if DNS lives on Cloudflare

log "Create deploy user"
if ! id deploy &>/dev/null; then
  useradd -m -s /bin/bash deploy
fi
usermod -aG sudo deploy
mkdir -p /home/deploy/.ssh
if [[ -f /root/.ssh/authorized_keys ]]; then
  cp /root/.ssh/authorized_keys /home/deploy/.ssh/authorized_keys
fi
chown -R deploy:deploy /home/deploy/.ssh
chmod 700 /home/deploy/.ssh
chmod 600 /home/deploy/.ssh/authorized_keys
# Limited passwordless sudo for service management only
cat > /etc/sudoers.d/deploy <<'EOF'
deploy ALL=(ALL) NOPASSWD: /usr/bin/systemctl reload nginx, /usr/bin/systemctl restart nginx, /usr/sbin/nginx -t, /usr/bin/certbot
EOF
chmod 440 /etc/sudoers.d/deploy

log "Create /var/www/production and /var/www/staging"
mkdir -p /var/www/production /var/www/staging
chown -R deploy:deploy /var/www/production /var/www/staging

log "Set up pm2 systemd service for deploy user"
env PATH="$PATH:/usr/bin" pm2 startup systemd -u deploy --hp /home/deploy >/dev/null
systemctl enable pm2-deploy >/dev/null 2>&1 || true

log "Disable nginx default site"
rm -f /etc/nginx/sites-enabled/default
nginx -t && systemctl reload nginx

log "Done. Summary:"
echo "  .NET runtimes:"
dotnet --list-runtimes | sed 's/^/    /'
echo "  Node:    $(node -v)"
echo "  pm2:     $(pm2 -v)"
echo "  nginx:   $(nginx -v 2>&1)"
echo "  certbot: $(certbot --version 2>&1)"
echo
echo "Next steps:"
echo "  1. Point ridepass.io DNS at $(curl -s ifconfig.me) (A @ + A *)"
echo "  2. Pick a DNS provider for certbot, install the matching plugin (see comments in this script)"
echo "  3. Issue wildcard cert: certbot certonly --dns-<provider> -d ridepass.io -d '*.ridepass.io'"
echo "  4. Drop nginx server blocks into /etc/nginx/sites-available/, symlink, reload"
echo "  5. Push code to /var/www/production as the deploy user, run migrator, pm2 startOrRestart ecosystem.config.js"
