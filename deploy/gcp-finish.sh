#!/usr/bin/env bash
set -euo pipefail

archive=/tmp/callback-api-gcp-deploy.tar.gz
for attempt in $(seq 1 180); do
  if docker info >/dev/null 2>&1; then
    break
  fi
  sleep 5
done
docker info >/dev/null

tar -xzf "$archive" -C /opt
rm -f "$archive"

if [ ! -f /opt/callbackapi/.env ]; then
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
fi

cd /opt/callbackapi
docker compose --env-file .env -f compose.yaml -f compose.production.yaml up --build -d
