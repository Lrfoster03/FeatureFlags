#!/usr/bin/env bash

set -euo pipefail

start_port="${1:-8080}"
end_port="${2:-8090}"

find_open_port() {
  local port

  for ((port = start_port; port <= end_port; port++)); do
    if ! lsof -nP -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1; then
      echo "$port"
      return 0
    fi
  done

  return 1
}

port="$(find_open_port)" || {
  echo "No available port found in range ${start_port}-${end_port}." >&2
  exit 1
}

echo "Using host port ${port} for container port 8080."
HOST_PORT="$port" docker compose up --build
