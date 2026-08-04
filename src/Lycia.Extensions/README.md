# Lycia.Extensions RabbitMQ topology

RabbitMQ uses one durable exchange per message type.

- Commands use a `direct` exchange, marker-derived owner routing key, and
  `command.{MessageType}.{ApplicationId}` queue. A command queue never contains the handler class.
- Events use a `fanout` exchange and one
  `event.{MessageType}.{HandlerType}.{ApplicationId}` queue per logical subscription.
- Responses use a `direct` exchange and target `ReplyTo`; requesters consume
  `response.{MessageType}.{ApplicationId}`.

Queues are durable, non-exclusive, and non-auto-delete. Replicas use the same `ApplicationId` and consume
the same queue competitively. Dead lettering, TTL, explicit ack/nack, serializer headers, and tracing
metadata remain enabled. Deliveries can be repeated around failures, so handlers must be idempotent.
