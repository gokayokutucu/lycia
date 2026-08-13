#!/usr/bin/env bash
# Copyright 2023 Lycia Contributors
# Licensed under the Apache License, Version 2.0
#
# Local-development reset tool for the Lycia Microservices sample. Resets service-local
# PostgreSQL and Redis state for one or more application services, and optionally resets the
# shared RabbitMQ broker for this sample's Compose project. Intended for manual architecture
# review, not CI. Always scoped to samples/Microservices/docker-compose.yml.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

readonly VALID_SERVICES="checkout order inventory payment shipping"

usage() {
    cat <<'EOF'
reset-state.sh - Reset local Lycia Microservices sample state (PostgreSQL + Redis + RabbitMQ)

USAGE:
  ./reset-state.sh <target> [<target> ...]
  ./reset-state.sh --help

VALID TARGETS:
  checkout | order | inventory | payment | shipping   Reset that service's PostgreSQL database
                                                        and dedicated Redis instance.
  all                                                  Reset PostgreSQL + Redis for all five
                                                        application services. Does NOT include
                                                        RabbitMQ unless "rabbitmq" is also given.
  rabbitmq                                             Explicitly reset the shared RabbitMQ
                                                        broker for THIS sample's Compose project.
                                                        RabbitMQ is shared by every service and is
                                                        never reset implicitly.

EXAMPLES:
  ./reset-state.sh checkout                Reset Checkout's PostgreSQL DB + Redis only.
  ./reset-state.sh checkout payment        Reset Checkout and Payment only.
  ./reset-state.sh all                     Reset PostgreSQL + Redis for all five services.
  ./reset-state.sh rabbitmq                Reset only the shared RabbitMQ broker.
  ./reset-state.sh checkout rabbitmq       Reset Checkout's state AND the shared broker.
  ./reset-state.sh all rabbitmq            Reset every service's state AND the shared broker.

WHAT GETS RESET PER SERVICE:
  - PostgreSQL: the service's dedicated database (<service>_db) is dropped and recreated with
    the same owner. The service's container is restarted so Lycia's schema migrations recreate
    the schema from scratch. No other service's database is touched.
  - Redis: the service's dedicated Redis instance (<service>-redis) is flushed. No other
    service's Redis instance is touched.

RABBITMQ IS SHARED AND GLOBAL:
  RabbitMQ is one broker shared by all five services in this Compose project. A RabbitMQ reset
  cannot be scoped to a single logical service because exchanges/queues can be shared. Requesting
  "rabbitmq" resets the ENTIRE broker (all queues, exchanges, bindings, and in-flight messages for
  this sample) and restarts every application service so consumers redeclare topology
  deterministically. This never affects RabbitMQ containers belonging to other Compose projects.

SAFETY:
  This script never runs `docker system prune`, `docker volume prune`, or
  `docker compose down -v`. It never touches Docker resources outside this sample's Compose
  project (samples/Microservices/docker-compose.yml). No Docker volumes are removed.
EOF
}

log() { printf '%s\n' "$*" >&2; }
fail() { log "error: $*"; exit 1; }

is_valid_service() {
    local candidate="$1" svc
    for svc in $VALID_SERVICES; do
        [ "$svc" = "$candidate" ] && return 0
    done
    return 1
}

db_name_for() {
    case "$1" in
        checkout) echo "checkout_db" ;;
        order) echo "order_db" ;;
        inventory) echo "inventory_db" ;;
        payment) echo "payment_db" ;;
        shipping) echo "shipping_db" ;;
        *) fail "no database mapping for service '$1'" ;;
    esac
}

redis_service_for() {
    case "$1" in
        checkout) echo "checkout-redis" ;;
        order) echo "order-redis" ;;
        inventory) echo "inventory-redis" ;;
        payment) echo "payment-redis" ;;
        shipping) echo "shipping-redis" ;;
        *) fail "no Redis mapping for service '$1'" ;;
    esac
}

host_port_for() {
    case "$1" in
        checkout) echo "8080" ;;
        order) echo "8081" ;;
        inventory) echo "8082" ;;
        payment) echo "8083" ;;
        shipping) echo "8084" ;;
        *) fail "no host port mapping for service '$1'" ;;
    esac
}

# --- argument parsing -------------------------------------------------------

if [ "$#" -eq 0 ]; then
    usage
    exit 1
fi

if [ "$1" = "--help" ] || [ "$1" = "-h" ]; then
    usage
    exit 0
fi

selected_services=""
reset_rabbitmq=0

for arg in "$@"; do
    case "$arg" in
        all)
            selected_services="checkout order inventory payment shipping"
            ;;
        rabbitmq)
            reset_rabbitmq=1
            ;;
        checkout|order|inventory|payment|shipping)
            case " $selected_services " in
                *" $arg "*) : ;; # already selected, ignore duplicate
                *) selected_services="$selected_services $arg" ;;
            esac
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            fail "unknown target '$arg'. Valid targets: $VALID_SERVICES all rabbitmq. Run --help for usage."
            ;;
    esac
done

selected_services="$(echo "$selected_services" | xargs -n1 | sort -u | xargs)"

if [ -z "$selected_services" ] && [ "$reset_rabbitmq" -eq 0 ]; then
    fail "no reset target selected. Run --help for usage."
fi

# --- print plan --------------------------------------------------------------

plan_databases=""
plan_redis=""
for svc in $selected_services; do
    plan_databases="$plan_databases $(db_name_for "$svc")"
    plan_redis="$plan_redis $(redis_service_for "$svc")"
done

log "Reset targets:"
if [ -n "$selected_services" ]; then
    log "  PostgreSQL:$plan_databases"
    log "  Redis:     $plan_redis"
else
    log "  PostgreSQL: none"
    log "  Redis:      none"
fi
if [ "$reset_rabbitmq" -eq 1 ]; then
    log "  RabbitMQ:  yes (shared broker for this sample - global reset)"
else
    log "  RabbitMQ:  no"
fi
log ""

# --- implementation ----------------------------------------------------------

wait_for_health() {
    local service="$1" port="$2" attempt=0 max_attempts=40
    log "Waiting for $service to become healthy (http://localhost:$port/health)..."
    while [ "$attempt" -lt "$max_attempts" ]; do
        if curl -fsS "http://localhost:$port/health" >/dev/null 2>&1; then
            log "$service is healthy."
            return 0
        fi
        attempt=$((attempt + 1))
        sleep 1
    done
    log "warning: $service did not report healthy within ${max_attempts}s; continue manually inspecting it."
}

reset_postgres_for_service() {
    local service="$1" db
    db="$(db_name_for "$service")"

    log "[$service] Stopping application container..."
    docker compose stop "$service" >/dev/null

    log "[$service] Terminating active connections to $db..."
    docker compose exec -T postgres psql -U lycia -d postgres -v ON_ERROR_STOP=1 -c \
        "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$db' AND pid <> pg_backend_pid();" \
        >/dev/null

    log "[$service] Dropping database $db..."
    docker compose exec -T postgres psql -U lycia -d postgres -v ON_ERROR_STOP=1 -c \
        "DROP DATABASE IF EXISTS $db;" >/dev/null

    log "[$service] Recreating database $db (owner: lycia)..."
    docker compose exec -T postgres psql -U lycia -d postgres -v ON_ERROR_STOP=1 -c \
        "CREATE DATABASE $db OWNER lycia;" >/dev/null

    log "[$service] Starting application container (schema migrations recreate the schema)..."
    docker compose up -d --no-deps "$service" >/dev/null

    wait_for_health "$service" "$(host_port_for "$service")"
}

reset_redis_for_service() {
    local service="$1" redis_service
    redis_service="$(redis_service_for "$service")"
    log "[$service] Flushing Redis instance $redis_service..."
    docker compose exec -T "$redis_service" redis-cli FLUSHALL >/dev/null
}

reset_rabbitmq_broker() {
    log "[rabbitmq] Recreating the RabbitMQ container for this sample (no persistent volume is used, so"
    log "[rabbitmq] this deterministically clears all queues, exchanges, bindings, and messages)..."
    docker compose up -d --force-recreate --no-deps rabbitmq >/dev/null

    log "[rabbitmq] Waiting for RabbitMQ to become healthy..."
    local attempt=0 max_attempts=40
    while [ "$attempt" -lt "$max_attempts" ]; do
        if docker compose exec -T rabbitmq rabbitmq-diagnostics -q ping >/dev/null 2>&1; then
            log "RabbitMQ is healthy."
            break
        fi
        attempt=$((attempt + 1))
        sleep 1
    done

    log "[rabbitmq] Restarting all five application services so consumers redeclare topology"
    log "[rabbitmq] deterministically (RabbitMQ.Client automatic recovery would eventually reconnect on"
    log "[rabbitmq] its own, but an explicit restart gives a predictable state for manual testing)..."
    docker compose restart checkout order inventory payment shipping >/dev/null

    for svc in $VALID_SERVICES; do
        wait_for_health "$svc" "$(host_port_for "$svc")"
    done
}

for svc in $selected_services; do
    reset_postgres_for_service "$svc"
    reset_redis_for_service "$svc"
done

if [ "$reset_rabbitmq" -eq 1 ]; then
    reset_rabbitmq_broker
fi

log ""
log "Reset complete."
