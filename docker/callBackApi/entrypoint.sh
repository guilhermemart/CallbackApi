#!/usr/bin/env bash
set -euo pipefail

postgres_connection_string="${ConnectionStrings__Postgres:-}"
postgres_host="${POSTGRES_HOST:-}"
postgres_port="${POSTGRES_PORT:-5432}"
wait_timeout_seconds="${POSTGRES_WAIT_TIMEOUT_SECONDS:-60}"

# O Kubernetes injeta POSTGRES_PORT=tcp://<ip>:5432 quando existe um Service
# chamado postgres. O entrypoint aceita apenas uma porta numÃ©rica.
if [[ ! "$postgres_port" =~ ^[0-9]+$ ]]; then
  postgres_port=5432
fi

if [[ -z "$postgres_host" && -n "$postgres_connection_string" ]]; then
  postgres_host="$(printf '%s' "$postgres_connection_string" | tr ';' '\n' | sed -n 's/^[Hh]ost=//p' | head -n 1)"
fi

if [[ -n "$postgres_connection_string" && -z "${POSTGRES_PORT:-}" ]]; then
  configured_port="$(printf '%s' "$postgres_connection_string" | tr ';' '\n' | sed -n 's/^[Pp]ort=//p' | head -n 1)"
  postgres_port="${configured_port:-$postgres_port}"
fi

if [[ -z "$postgres_host" ]]; then
  echo "PostgreSQL host is required. Set POSTGRES_HOST or ConnectionStrings__Postgres." >&2
  exit 1
fi

deadline=$((SECONDS + wait_timeout_seconds))
until (echo > "/dev/tcp/${postgres_host}/${postgres_port}") 2>/dev/null; do
  if (( SECONDS >= deadline )); then
    echo "PostgreSQL did not accept connections at ${postgres_host}:${postgres_port} within ${wait_timeout_seconds}s." >&2
    exit 1
  fi

  echo "Waiting for PostgreSQL at ${postgres_host}:${postgres_port}..."
  sleep 2
done

echo "PostgreSQL is accepting connections at ${postgres_host}:${postgres_port}."
exec "$@"
