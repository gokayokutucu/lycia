# Lycia.Extensions RabbitMQ topology

RabbitMQ uses one durable exchange per message type.

- Commands use a `direct` exchange, marker-derived owner routing key, and
  `command.{MessageType}.{ApplicationId}` queue. A command queue never contains the handler class.
- Events use a `fanout` exchange and one
  `event.{MessageType}.{HandlerType}.{ApplicationId}` queue per logical subscription.
- Responses use a `direct` exchange and target canonical `ResponseEndpoint`; requesters consume
  `response.{MessageType}.{ApplicationId}`.

Queues are durable, non-exclusive, and non-auto-delete. Replicas use the same `ApplicationId` and consume
the same queue competitively. Dead lettering, TTL, explicit ack/nack, serializer headers, and tracing
metadata remain enabled. Deliveries can be repeated around failures, so handlers must be idempotent.

Responses are sent with `Context.Respond` and cannot be broadcast with `Publish`. Headers preserve
message, request, correlation, causation, parent, saga, and endpoint identity through redelivery and DLQ.
Application keys use invariant lowercase and ignore dash, underscore, dot, and whitespace.

## RabbitMQ scheduling and cleanup

`AddLyciaScheduling` stores scheduling intent in Redis and hosts `SchedulerWorker`, manifest heartbeat, health checks,
and `VacuumWorker`. Predefined `ScheduleDelay` values lazily declare durable queues with one fixed
`x-message-ttl`, `x-dead-letter-exchange`, and `x-dead-letter-routing-key` per destination and bucket. Lycia never
mixes per-message expirations in a shared queue, and an incompatible pre-existing queue fails redeclaration clearly.
Buckets beyond RabbitMQ's unsigned 32-bit millisecond TTL limit automatically use `SchedulerWorker` instead of
overflowing or silently shortening the requested delay.

`AllowDynamicDelays=true` permits deterministic `...{milliseconds}ms` queues and adds `x-expires` as an extra safety
net. These queues are more expensive and should be exceptional. Vacuum ownership comes from the durable registry,
never a name prefix. Deletion additionally requires age, idle retention, no manifest or pending schedule, zero
messages, zero consumers, a current fenced lease, and RabbitMQ `if-empty` plus `if-unused`. Predefined queues are
protected. Ordinary queues remain `ReportOnly` by default and require quarantine plus
`AllowDestructiveApplicationTopologyCleanup` for automatic deletion. Runtime scheduling needs Redis access and final
publish rights; only automatic vacuum needs broker delete rights. Delivery remains at least once.

Canonicalization can rename queues and routing keys. Drain and stop old consumers, deploy and validate
the canonical topology, then remove obsolete resources; Lycia never deletes or dual-binds them. Another
independently bound RabbitMQ queue can still receive the same key, so ownership is a Lycia invariant,
not broker-global exclusivity. Delivery is at least once.
