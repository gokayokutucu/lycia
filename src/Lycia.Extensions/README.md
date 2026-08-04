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

Canonicalization can rename queues and routing keys. Drain and stop old consumers, deploy and validate
the canonical topology, then remove obsolete resources; Lycia never deletes or dual-binds them. Another
independently bound RabbitMQ queue can still receive the same key, so ownership is a Lycia invariant,
not broker-global exclusivity. Delivery is at least once.
