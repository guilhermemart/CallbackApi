#!/usr/bin/env bash
set -euo pipefail

exec > >(tee -a /var/log/callback-api-startup.log | logger -t callback-api-startup -s 2>/dev/console) 2>&1

apt-get update
apt-get install -y ca-certificates curl git openssl

install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc
. /etc/os-release
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian ${VERSION_CODENAME} stable" \
  > /etc/apt/sources.list.d/docker.list
apt-get update
apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
systemctl enable --now docker

install -d -m 0755 /opt/callbackapi /opt/CallbackFrontEnd
git clone --depth 1 --branch main https://github.com/guilhermemart/CallbackApi.git /opt/callbackapi
git clone --depth 1 --branch main https://github.com/guilhermemart/CallbackFrontEnd.git /opt/CallbackFrontEnd

public_ip="$(curl -fsS -H 'Metadata-Flavor: Google' \
  http://metadata.google.internal/computeMetadata/v1/instance/network-interfaces/0/access-configs/0/external-ip)"
postgres_password="$(openssl rand -hex 32)"
umask 077
cat > /opt/callbackapi/.env <<EOF
POSTGRES_PASSWORD=${postgres_password}
COOKIE_SECURE=true
FRONTEND_PORT=127.0.0.1:8080
PUBLIC_IP=${public_ip}
EOF

cd /opt/callbackapi
docker compose --env-file .env -f compose.yaml -f compose.production.yaml up --build -d
