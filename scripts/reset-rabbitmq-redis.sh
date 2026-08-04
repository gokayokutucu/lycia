#!/usr/bin/env bash
# Resets Lycia's local development messaging and saga-state infrastructure.
# SQL Server is sample business persistence and is intentionally not touched.

set -Eeuo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_NAME="${COMPOSE_PROJECT_NAME:-lycia}"
COMPOSE=(docker compose --project-directory "$PROJECT_ROOT" --project-name "$PROJECT_NAME")
SERVICES=(rabbitmq redis nats kafka)
VOLUME_NAMES=(rabbitmq_data redis_data nats_data kafka_data)

environment="${DOTNET_ENVIRONMENT:-${ASPNETCORE_ENVIRONMENT:-Development}}"
environment_key="$(printf '%s' "$environment" | tr '[:upper:]' '[:lower:]')"

if [[ "$environment_key" != "development" && "$environment_key" != "dev" && "$environment_key" != "local" ]]; then
  echo "Refusing to reset infrastructure outside Development (current environment: $environment)." >&2
  exit 1
fi

case "${CI:-}" in
  1|true|TRUE|yes|YES)
    echo "Refusing to run the local development reset script in CI." >&2
    exit 1
    ;;
esac

if ! docker info >/dev/null 2>&1; then
  echo "Docker daemon is unavailable. Start Docker Desktop and try again." >&2
  exit 1
fi

# Capture volumes currently mounted by the explicitly scoped Compose services
# before deleting them. This handles volumes created by a previous invocation.
volumes=()
for service in "${SERVICES[@]}"; do
  while IFS= read -r container_id; do
    [[ -n "$container_id" ]] || continue
    while IFS= read -r volume; do
      [[ -n "$volume" ]] && volumes+=("$volume")
    done < <(docker inspect --format '{{ range .Mounts }}{{ if eq .Type "volume" }}{{ .Name }}{{ "\\n" }}{{ end }}{{ end }}' "$container_id")
  done < <("${COMPOSE[@]}" ps --all --quiet "$service")
done

# Also locate the declared named volumes when the containers no longer exist.
for volume_name in "${VOLUME_NAMES[@]}"; do
  while IFS= read -r volume; do
    [[ -n "$volume" ]] && volumes+=("$volume")
  done < <(docker volume ls --quiet \
    --filter "label=com.docker.compose.project=$PROJECT_NAME" \
    --filter "label=com.docker.compose.volume=$volume_name")
done

echo "Stopping and removing Lycia development infrastructure..."
"${COMPOSE[@]}" rm --stop --force "${SERVICES[@]}" >/dev/null

if ((${#volumes[@]})); then
  echo "Removing persisted RabbitMQ, Redis, NATS, and Kafka data..."
  removed_volumes=" "
  for volume in "${volumes[@]}"; do
    [[ "$removed_volumes" == *" $volume "* ]] && continue
    if docker volume inspect "$volume" >/dev/null 2>&1; then
      docker volume rm "$volume" >/dev/null
    fi
    removed_volumes+="$volume "
  done
fi

echo "Starting clean RabbitMQ, Redis, NATS, and Kafka instances..."
"${COMPOSE[@]}" up --detach "${SERVICES[@]}"

wait_for_service() {
  local service="$1"
  local timeout_seconds="${2:-180}"
  local container_id state health
  local started_at=$SECONDS

  container_id="$("${COMPOSE[@]}" ps --quiet "$service")"
  if [[ -z "$container_id" ]]; then
    echo "No container was created for $service." >&2
    return 1
  fi

  while ((SECONDS - started_at < timeout_seconds)); do
    state="$(docker inspect --format '{{ .State.Status }}' "$container_id")"
    health="$(docker inspect --format '{{ if .State.Health }}{{ .State.Health.Status }}{{ else }}none{{ end }}' "$container_id")"

    if [[ "$state" == "running" && ("$health" == "healthy" || "$health" == "none") ]]; then
      printf '  %-8s ready\n' "$service"
      return 0
    fi

    if [[ "$state" == "exited" || "$state" == "dead" || "$health" == "unhealthy" ]]; then
      echo "$service failed readiness validation (state=$state, health=$health)." >&2
      return 1
    fi

    sleep 2
  done

  echo "$service did not become ready within ${timeout_seconds}s." >&2
  return 1
}

echo "Waiting for service readiness..."
for service in "${SERVICES[@]}"; do
  wait_for_service "$service"
done

echo
echo "Ready:"
echo "  RabbitMQ AMQP: http://localhost:5672"
echo "  RabbitMQ UI:   http://localhost:15672  (guest / guest)"
echo "  Redis:         redis://localhost:6379"
echo "  NATS:          nats://localhost:4222"
echo "  NATS monitor:  http://localhost:8222"
echo "  Kafka:         localhost:9092"
