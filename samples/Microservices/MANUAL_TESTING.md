# Manual Architecture Review Guide

This guide walks a human reviewer through the Lycia Microservices sample end to end: starting the
stack, exercising the saga workflow, and inspecting canonical PostgreSQL state, the Redis
operational projection, RabbitMQ topology, and distributed traces in Jaeger. It is written for
manual architecture review, not CI. Commands assume macOS/Linux with Docker Desktop and `curl`.

All commands below are run from `samples/Microservices` unless stated otherwise.

```bash
cd samples/Microservices
```

---

## 1. Environment

### 1.1 Start the stack

```bash
docker compose up --build -d
docker compose ps
```

Wait for every service to report healthy/running (Postgres, RabbitMQ, and Jaeger have Docker
health checks; the five app services do not, so poll their `/health` endpoint):

```bash
for port in 8080 8081 8082 8083 8084; do
  until curl -fsS "http://localhost:$port/health" >/dev/null 2>&1; do sleep 1; done
  echo "port $port ready"
done
```

### 1.2 Host URLs

| Purpose | URL |
| --- | --- |
| Checkout HTTP API | http://localhost:8080 |
| Order HTTP API | http://localhost:8081 |
| Inventory HTTP API | http://localhost:8082 |
| Payment HTTP API | http://localhost:8083 |
| Shipping HTTP API | http://localhost:8084 |
| RabbitMQ management UI | http://localhost:15672 (guest / guest) |
| Jaeger UI | http://localhost:16686 |

PostgreSQL and each Redis instance are **not** published to the host — the compose network is
sufficient, and every inspection command below uses `docker compose exec` instead of a host port.

### 1.3 A note on timing

A full five-hop happy-path checkout normally completes within a few seconds. Poll for the state you
want instead of relying on a fixed `sleep`, because timing depends on the host.

This sample's RabbitMQ publisher uses publisher confirms (see root `README.md`, "Publisher confirms"): the
Outbox worker marks a message `Published` only once the broker has confirmed it, so each Outbox row is
published once (`retry_count = 1`) and settles as `Published` within moments. A message is redispatched only
when its outcome was genuinely uncertain (for example the broker connection dropped before the confirm
arrived, §15), and the receiving Inbox absorbs any resulting duplicate (see §11).

```bash
# wait_for_saga_version <orderId> <minVersion> [timeoutSeconds]
wait_for_saga_version() {
  local order_id="$1" min_version="$2" timeout="${3:-240}" waited=0
  while [ "$waited" -lt "$timeout" ]; do
    local version
    version=$(curl -s "http://localhost:8080/checkouts/$order_id" | python3 -c \
      'import json,sys; print(json.load(sys.stdin).get("sagaVersion",0))' 2>/dev/null || echo 0)
    if [ "$version" -ge "$min_version" ] 2>/dev/null; then echo "reached version $version"; return 0; fi
    sleep 3; waited=$((waited + 3))
  done
  echo "timed out waiting for version >= $min_version (last seen: ${version:-unknown})"
  return 1
}
```

The tests below use `wait_for_saga_version` — define it once in your shell session before running
them.

---

## 2. Create a checkout

`POST /checkout` accepts `orderId` (optional — a new GUID is generated if omitted), an optional
`messageId`, and an optional `failAt` (`"inventory"` or `"payment"`) used by the failure-path
tests below. This is the actual current contract (`samples/Microservices/ServiceBootstrap.cs`).

```bash
ORDER_ID=$(uuidgen | tr '[:upper:]' '[:lower:]')
echo "ORDER_ID=$ORDER_ID"

curl -s -X POST http://localhost:8080/checkout \
  -H 'content-type: application/json' \
  -d "{\"orderId\":\"$ORDER_ID\"}"
echo
```

Poll for completion (see §1.3 for `wait_for_saga_version` and why this can take a couple of
minutes in this demo topology):

```bash
wait_for_saga_version "$ORDER_ID" 5
curl -s "http://localhost:8080/checkouts/$ORDER_ID"
echo
```

Expected final response once the workflow completes:

```json
{"orderId":"...","sagaId":"...","sagaVersion":5,"canonicalState":"{...\"Status\": \"Completed\"...\"IsCompleted\": true...}","operationalProjectionVersion":5}
```

`sagaVersion` reaches `5` on a full happy path: `1` create, `2` after OrderCreatedResponse, `3`
after InventoryReservedResponse, `4` after PaymentSucceededResponse, `5` after OrderShippedResponse
(terminal, `IsCompleted: true`).

---

## 3. PostgreSQL inspection

Canonical state lives in Checkout's own database (`checkout_db`); every other service has its own
database with the same schema. Table/column names below come directly from
`src/Lycia.Persistence.PostgreSql/Schema/*.sql` — nothing here is invented.

Open a psql shell against Checkout's database:

```bash
docker compose exec postgres psql -U lycia -d checkout_db
```

Or run one-off queries directly:

```bash
docker compose exec -T postgres psql -U lycia -d checkout_db -c "<query>"
```

### 3.1 Saga state (`lycia_saga_data`)

```sql
SELECT saga_id, version, is_completed, completed_at_utc, failed_at_utc, data_json
FROM lycia_saga_data
ORDER BY updated_at_utc DESC
LIMIT 5;
```

### 3.2 Saga step log (`lycia_saga_steps`)

```sql
SELECT step_type, handler_type, message_id, status, recorded_at_utc
FROM lycia_saga_steps
WHERE saga_id = '<saga-id>'
ORDER BY recorded_at_utc;
```

### 3.3 Inbox (`lycia_inbox`)

```sql
SELECT message_id, handler_type, status, created_at_utc, updated_at_utc
FROM lycia_inbox
ORDER BY created_at_utc DESC
LIMIT 20;
```

### 3.4 Outbox (`lycia_outbox`)

```sql
SELECT message_id, message_type_name, status, retry_count, saga_id, created_at_utc
FROM lycia_outbox
ORDER BY created_at_utc DESC
LIMIT 20;
```

### 3.5 Reconciliation intents (`lycia_saga_reconciliation`)

```sql
SELECT transition_id, saga_id, target_version, status, attempt_count, created_at_utc
FROM lycia_saga_reconciliation
WHERE saga_id = '<saga-id>'
ORDER BY target_version;
```

### 3.6 Canonical journal (`lycia_saga_journal`)

```sql
SELECT sequence_number, previous_version, target_version, transition_type, message_id, handler_type
FROM lycia_saga_journal
WHERE saga_id = '<saga-id>'
ORDER BY sequence_number;
```

### 3.7 Other services' databases

Same queries work against `order_db`, `inventory_db`, `payment_db`, `shipping_db` — each row in
those services will have its own `saga_id` (their own `ServiceSagaData` instance), distinct from
Checkout's orchestrating saga:

```bash
docker compose exec postgres psql -U lycia -d inventory_db -c "SELECT saga_id, version, data_json FROM lycia_saga_data ORDER BY updated_at_utc DESC LIMIT 5;"
```

---

## 4. Redis inspection

Each service owns a dedicated Redis instance holding only its own Split Store operational
projection. The key format is `saga:data:<sagaId>` (`src/Lycia.Persistence.Redis/RedisOperationalSagaProjectionStore.cs`).

List keys (a tiny dev instance, but `SCAN` is used instead of `KEYS *` as good practice):

```bash
docker compose exec checkout-redis redis-cli --scan --pattern 'saga:data:*'
```

Inspect a specific projection (JSON payload includes the `Version` field used for reconciliation):

```bash
docker compose exec checkout-redis redis-cli GET "saga:data:<saga-id>"
```

Verify a projection disappears after deletion (used by the rebuild test in §7):

```bash
docker compose exec checkout-redis redis-cli EXISTS "saga:data:<saga-id>"
```

---

## 5. RabbitMQ inspection

Management UI: **http://localhost:15672** (guest / guest).

Actual queue naming convention for this sample (`command.{Type}.{Owner}`, `response.{Type}.{Requester}`,
plus a `.dlq` dead-letter queue per queue):

```
command.StartCheckoutCommand.checkoutservice(.dlq)
command.CreateOrderCommand.orderservice(.dlq)
command.ReserveInventoryCommand.inventoryservice(.dlq)
command.ProcessPaymentCommand.paymentservice(.dlq)
command.ShipOrderCommand.shippingservice(.dlq)
response.OrderCreatedResponse.checkoutservice(.dlq)
response.InventoryReservedResponse.checkoutservice(.dlq)
response.PaymentSucceededResponse.checkoutservice(.dlq)
response.OrderShippedResponse.checkoutservice(.dlq)
message.<SagaData>.<Handler>.<owner>(.dlq)   # internal saga-continuation queues per handler
```

Each command/response queue normally shows **1 consumer** (the owning service's single replica in
this sample) and **0 messages** at rest — a nonzero message count means a consumer is stopped or
falling behind, which is exactly what the process-restart test in §10 demonstrates.

List queues and consumer/message counts from the CLI:

```bash
curl -s -u guest:guest http://localhost:15672/api/queues | \
  python3 -c "import json,sys; [print(q['name'],'| consumers:',q.get('consumers'),'| messages:',q.get('messages')) for q in json.load(sys.stdin)]"
```

Broker-level diagnostics via `docker compose exec`:

```bash
docker compose exec rabbitmq rabbitmq-diagnostics -q ping
docker compose exec rabbitmq rabbitmqctl list_queues name messages consumers
```

---

## 6. Jaeger trace inspection

Jaeger UI: **http://localhost:16686**.

1. Open the UI, select service **`CheckoutService`** in the Service dropdown.
2. Select operation **`POST /checkout`** (or leave "all").
3. Click **Find Traces** and open the most recent trace for the `orderId` you just created.
4. You should see one continuous trace containing spans from `CheckoutService`, `OrderService`,
   `InventoryService`, `PaymentService`, and `ShippingService`, each a child of the message that
   caused it — not five disconnected single-span traces. If a workflow that visibly completed shows
   as fragmented, unrelated traces instead, that is a tracing defect, not expected behavior.
5. Expand a handler span (e.g. `Lycia.Samples.Microservices.Inventory.InventoryHandler`) and inspect
   its tags. Attributes actually emitted by `ActivityTracingMiddleware`
   (`src/Lycia/Middleware/ActivityTracingMiddleware.cs`) include:
   - `lycia.saga.id` / `lycia.saga_id`
   - `lycia.message.id` / `lycia.message_id`
   - `lycia.correlation.id` / `lycia.correlation_id`
   - `lycia.parent_message_id`, `lycia.causation_id`
   - `lycia.handler`, `lycia.application.id` / `lycia.application_id`
   - `lycia.saga.step.status` (`Completed`, or `Failed` with an error span status and `exception.*`
    tags)
   RabbitMQ consumer spans additionally carry `messaging.system=rabbitmq`, `messaging.destination`,
   `messaging.operation=process` (set in `RabbitMqListener`).
6. Outbox-mediated hops (every hop in this sample, since Outbox is enabled) show an additional
   `Outbox.Send` / `Outbox.Publish` / `Outbox.Respond` span between the originating handler and the
   next consumer — this is the durable Outbox dispatch worker publishing the captured message, not
   a synchronous transport call.

Query the same data from the CLI (useful for scripting or when the UI is inconvenient):

```bash
curl -s "http://localhost:16686/api/traces?service=CheckoutService&limit=5" | python3 -m json.tool
```

If tracing export is misconfigured or Jaeger is down, checkouts still complete normally — the OTLP
exporter drops spans in the background rather than failing the business request
(`OTEL_EXPORTER_OTLP_ENDPOINT` in `docker-compose.yml`).

---

## 7. Reset cheat sheet

```bash
./reset-state.sh checkout                # Checkout PostgreSQL + Redis only
./reset-state.sh checkout payment        # Checkout and Payment only
./reset-state.sh all                     # every service's PostgreSQL + Redis (RabbitMQ untouched)
./reset-state.sh rabbitmq                # only the shared RabbitMQ broker
./reset-state.sh all rabbitmq            # everything, including the shared broker
./reset-state.sh --help                  # full usage
```

See `reset-state.sh --help` for exact semantics — in particular, RabbitMQ is **never** reset
implicitly; it requires the explicit `rabbitmq` target because it is shared by all five services.

---

## 8. Test 1 — Happy path

```bash
./reset-state.sh all
docker compose ps   # confirm everything is healthy before starting

ORDER_ID=$(uuidgen | tr '[:upper:]' '[:lower:]')
curl -s -X POST http://localhost:8080/checkout -H 'content-type: application/json' -d "{\"orderId\":\"$ORDER_ID\"}"
echo
wait_for_saga_version "$ORDER_ID" 5
curl -s "http://localhost:8080/checkouts/$ORDER_ID"; echo
```

Verify the architecture invariants:

- Final response: `sagaVersion: 5`, `"Status": "Completed"`, `"IsCompleted": true`.
- PostgreSQL: `lycia_saga_data` for the saga shows `version = 5`, `is_completed = true`.
- Journal: `lycia_saga_journal` has exactly 5 contiguous entries (`sequence_number` 1..5,
  `previous_version`/`target_version` chained with no gaps).
- Redis: `saga:data:<saga-id>` JSON `"Version": 5`, matching canonical.
- Inbox: one row per `(message_id, handler_type)` pair actually processed — no duplicates.
- Outbox: one row per outgoing message. Once the workflow completes every row is `Published`
  (`status = 3`) with `retry_count = 1`: RabbitMQ confirmed each publish. A row that is still
  `Pending`, `Claimed`, `Publishing` or `ConfirmationUnknown` a minute after the checkout finished
  indicates a problem (`ConfirmationUnknown` is expected only briefly after a broker outage).
- Jaeger: one connected trace spanning all five services (§6).

---

## 9. Test 2 — Redis outage and recovery

```bash
ORDER_ID=$(uuidgen | tr '[:upper:]' '[:lower:]')

docker compose stop checkout-redis

curl -s -X POST http://localhost:8080/checkout -H 'content-type: application/json' -d "{\"orderId\":\"$ORDER_ID\"}"
echo
wait_for_saga_version "$ORDER_ID" 5

# Canonical PostgreSQL state, independent of Redis:
curl -s "http://localhost:8080/state/$ORDER_ID"; echo
docker compose exec postgres psql -U lycia -d checkout_db -c \
  "SELECT saga_id, version, is_completed FROM lycia_saga_data WHERE data_json->>'OrderId' = '$ORDER_ID';"

docker compose start checkout-redis
sleep 20   # allow the reconciliation worker to catch up (its own poll interval, separate from Outbox)

docker compose exec checkout-redis redis-cli --scan --pattern 'saga:data:*'
```

Expected: the checkout completes to `version 5` in PostgreSQL while `checkout-redis` is stopped
(`/state/{orderId}` reads canonical state directly, independent of Redis — Split Store design).
After `checkout-redis` restarts, its projection is repopulated by ordinary reconciliation, not a
manual rebuild call. The journal stays contiguous throughout; no handler is re-invoked merely
because Redis was unavailable.

---

## 10. Test 3 — Redis projection delete + journal-based rebuild

This test specifically uses the **journal-based rebuild** endpoint
(`POST /debug/sagas/{sagaId}/rebuild-from-journal`), not the latest-state-copy endpoint
(`POST /debug/projections/{sagaId}/restore`). The two are intentionally different mechanisms — see
root `DEVELOPERS.md`.

```bash
ORDER_ID=$(uuidgen | tr '[:upper:]' '[:lower:]')
curl -s -X POST http://localhost:8080/checkout -H 'content-type: application/json' -d "{\"orderId\":\"$ORDER_ID\"}"
echo
wait_for_saga_version "$ORDER_ID" 5
curl -s "http://localhost:8080/checkouts/$ORDER_ID"; echo   # note sagaId and sagaVersion
SAGA_ID=<paste-sagaId-here>

# Baseline
docker compose exec postgres psql -U lycia -d checkout_db -c \
  "SELECT version FROM lycia_saga_data WHERE saga_id = '$SAGA_ID';"
docker compose exec checkout-redis redis-cli GET "saga:data:$SAGA_ID"

# Baseline Outbox row count (to prove rebuild creates no new business messages)
docker compose exec postgres psql -U lycia -d checkout_db -c \
  "SELECT count(*) FROM lycia_outbox;"

# Delete the Redis projection
curl -s -X DELETE "http://localhost:8080/debug/projections/$SAGA_ID" -w '\n%{http_code}\n'

# Verify: MissingProjection
curl -s "http://localhost:8080/debug/sagas/$SAGA_ID/verify"; echo

# Journal-based rebuild
curl -s -X POST "http://localhost:8080/debug/sagas/$SAGA_ID/rebuild-from-journal"; echo

# Verify: Healthy
curl -s "http://localhost:8080/debug/sagas/$SAGA_ID/verify"; echo

# Confirm Redis restored and Outbox count unchanged
docker compose exec checkout-redis redis-cli GET "saga:data:$SAGA_ID"
docker compose exec postgres psql -U lycia -d checkout_db -c \
  "SELECT count(*) FROM lycia_outbox;"
```

Expected: `/verify` reports `MissingProjection` after the delete (`operationalProjectionVersion: 0`),
then `Healthy` after rebuild, with `journalVersion` and `operationalProjectionVersion` equal (5 on a
completed happy-path saga). `canonicalVersion` is a best-effort field resolved via reflection against
the stored SagaData type name and is commonly `null` in this sample — that does not indicate a
problem; `VerifySagaAsync` never fails verification over it (see `DEVELOPERS.md`). The Redis payload
after rebuild matches the pre-delete payload. The Outbox row count is identical before and after —
rebuild never invokes handlers or creates new outgoing messages (`SagaRebuildService` has no
dependency capable of doing either; see `DEVELOPERS.md`).

---

## 11. Test 4 — Duplicate delivery

With publisher confirms a healthy run produces no redispatch, so the Inbox duplicate-suppression path is
not exercised by an ordinary checkout; it appears after a broker outage or a crash (a message whose confirm
was lost is published again with the same `MessageId`), and you can trigger it deterministically by putting
a published Outbox row back to `Pending`:

```bash
MSG=$(docker compose exec -T postgres psql -U lycia -d inventory_db -At -c \
  "SELECT message_id FROM lycia_outbox ORDER BY created_at_utc DESC LIMIT 1;")
docker compose exec -T postgres psql -U lycia -d inventory_db -c \
  "UPDATE lycia_outbox SET status = 0, retry_count = 0, updated_at_utc = now() WHERE message_id = '$MSG';"
sleep 12
docker compose logs --since 30s | grep "already"
```

The row is republished with the same `MessageId` (its status returns to `Published`), and the receiving
service logs:

```
Inbox: message <id> for handler CheckoutSagaHandler is already AlreadyCompleted; skipping duplicate execution.
```

Re-posting a checkout with the same `messageId` is absorbed even earlier: Outbox capture is idempotent on
`MessageId`, so no second record is created.

To verify the effect deterministically:

1. Run a happy-path checkout and note its `sagaId` and final `sagaVersion` (§8).
2. Re-run the same `GET /checkouts/{orderId}` a few times — `sagaVersion` does not change.
3. Confirm the journal is still exactly the same length:
   ```bash
   docker compose exec postgres psql -U lycia -d checkout_db -c \
     "SELECT count(*) FROM lycia_saga_journal WHERE saga_id = '<saga-id>';"
   ```
4. Confirm the Redis projection version is unchanged:
   ```bash
   docker compose exec checkout-redis redis-cli GET "saga:data:<saga-id>"
   ```

Expected: `sagaVersion`, journal row count, and the Redis projection version are stable across
redeliveries — Inbox absorbs the duplicate before the handler body (and therefore any further
canonical write) runs again.

---

## 12. Test 5 — Inventory failure

```bash
ORDER_ID=$(uuidgen | tr '[:upper:]' '[:lower:]')
curl -s -X POST http://localhost:8080/checkout -H 'content-type: application/json' \
  -d "{\"orderId\":\"$ORDER_ID\",\"failAt\":\"inventory\"}"
echo
wait_for_saga_version "$ORDER_ID" 2
curl -s "http://localhost:8080/checkouts/$ORDER_ID"; echo
```

Expected (from `CheckoutSagaHandler`/`InventoryHandler` in this sample — no compensation is
implemented here, only a deterministic stuck boundary): the Checkout saga remains at
`"Status": "ReservingInventory"`, `sagaVersion: 2`, `"IsCompleted": false`. The Inventory service
throws `InvalidOperationException("Injected inventory failure.")` and never responds, so Checkout
never advances past this step. Do not expect a `PaymentFailedEvent`/compensation event — this
sample does not implement compensation for this path; it demonstrates the failure boundary staying
observable rather than silently advancing or fabricating a response.

Inspect:

```bash
docker compose exec postgres psql -U lycia -d checkout_db -c \
  "SELECT version, data_json->>'Status' AS status FROM lycia_saga_data WHERE data_json->>'OrderId' = '$ORDER_ID';"
docker compose logs inventory --since 2m | grep -i "injected inventory failure"
```

In Jaeger, the trace for this `orderId` ends at the `InventoryHandler` span, which carries an error
status, `lycia.saga.step.status=Failed` and `exception.type`/`exception.message` tags — there is no
further continuation into Payment or Shipping. The Inventory log shows a matching warning from
`SagaCompensationCoordinator` naming the saga, message and exception. The handler base class records the
exception as a failed step instead of rethrowing it, so the message is acknowledged rather than
redelivered.

---

## 13. Test 6 — Payment failure

Identical shape, using `"failAt":"payment"`:

```bash
ORDER_ID=$(uuidgen | tr '[:upper:]' '[:lower:]')
curl -s -X POST http://localhost:8080/checkout -H 'content-type: application/json' \
  -d "{\"orderId\":\"$ORDER_ID\",\"failAt\":\"payment\"}"
echo
wait_for_saga_version "$ORDER_ID" 3
curl -s "http://localhost:8080/checkouts/$ORDER_ID"; echo
```

Expected: the saga remains at `"Status": "ProcessingPayment"`, `sagaVersion: 3`,
`"IsCompleted": false`. `PaymentHandler` throws `InvalidOperationException("Injected payment
failure.")`. Same inspection pattern as §12, against `payment_db` and the `payment` container logs.

---

## 14. Test 7 — Process restart

```bash
ORDER_ID=$(uuidgen | tr '[:upper:]' '[:lower:]')
curl -s -X POST http://localhost:8080/checkout -H 'content-type: application/json' -d "{\"orderId\":\"$ORDER_ID\"}"
echo

# Restart Inventory mid-flight
docker compose restart inventory

wait_for_saga_version "$ORDER_ID" 5
curl -s "http://localhost:8080/checkouts/$ORDER_ID"; echo
```

Expected:

- The checkout still reaches `sagaVersion: 5`, `"Status": "Completed"` — durable Outbox/Inbox state
  survives the restart, and RabbitMQ redelivers any in-flight `ReserveInventoryCommand` once
  Inventory's consumer reconnects.
- Check the RabbitMQ queue backlog during the restart window and confirm it drains afterward:
  ```bash
  docker compose exec rabbitmq rabbitmqctl list_queues name messages consumers
  ```
- Check Inventory's logs for any "already AlreadyCompleted; skipping duplicate execution" lines if a
  redelivered message arrived after the handler had already committed — this is the same Inbox
  dedup behavior as §11, triggered by the restart instead of an ordinary broker redelivery.
- In Jaeger, the trace for this `orderId` still shows a connected chain through Inventory — Outbox
  dispatch is what re-establishes the Inventory hop after the restart, not a single unbroken span
  across the crash boundary; a restart is a real discontinuity in process lifetime and the trace
  reflects that honestly rather than pretending otherwise.

---

## 15. Test 8 — RabbitMQ reset and topology recovery

Recreate the broker, which wipes every queue, exchange and binding, without restarting the services:

```bash
docker compose up -d --force-recreate --no-deps rabbitmq
until docker compose exec -T rabbitmq rabbitmq-diagnostics -q ping >/dev/null 2>&1; do sleep 1; done
sleep 15   # allow RabbitMQ.Client automatic recovery to reconnect and redeclare topology
docker compose exec rabbitmq rabbitmqctl list_queues name consumers

ORDER_ID=$(uuidgen | tr '[:upper:]' '[:lower:]')
curl -s -X POST http://localhost:8080/checkout -H 'content-type: application/json' -d "{\"orderId\":\"$ORDER_ID\"}"
echo
wait_for_saga_version "$ORDER_ID" 5
```

Expected: every non-DLQ queue is redeclared with one consumer, and the new checkout completes to
`sagaVersion: 5`. `./reset-state.sh rabbitmq` is the scripted variant; it also restarts all five services
for a deterministic starting state.
