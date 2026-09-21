# Lycia.Extensions.RabbitMq

The RabbitMQ transport for the Lycia Saga framework: `RabbitMqEventBus`, the background listener,
queue/exchange/binding topology, DLQ behavior, and the native TTL + DLX scheduling strategy.

## Registration

```csharp
services.AddLycia(configuration, lycia =>
{
    lycia.AddSagas().FromCurrentAssembly();
    lycia.UseTransport().RabbitMq(); // or .RabbitMq(options => { ... }); defaults bind from Lycia:EventBus
});
```

The older `services.AddLyciaRabbitMq()` call still compiles as an `[Obsolete]` wrapper.

Durable transport-independent scheduling (dispatch worker, Redis store, vacuum) lives in
`Lycia.Extensions.Scheduling`; this package only contributes RabbitMQ's native delay strategy.
Namespaces (`Lycia.Extensions.Eventing`, `Lycia.Extensions.Listener`, `Lycia.Extensions.Helpers`) are
unchanged from the earlier combined `Lycia.Extensions` package.

## RabbitMQ topology

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

Scheduling (`AddScheduling().WithRedisStore()`) stores scheduling intent in Redis and hosts `SchedulerWorker`, manifest heartbeat, health checks,
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

## Publisher confirms

Publishing uses RabbitMQ **publisher confirms** by default (`EventBusOptions.PublisherConfirms`). Every
`Send`, `Publish` and `Respond` runs on a dedicated publish channel created with confirms enabled and waits
for the broker's confirmation, so `RabbitMqEventBus` implements `IConfirmedEventBus` and an Outbox message
the broker confirmed is recorded as `Published` instead of `ConfirmationUnknown`.

- A `basic.nack` throws `RabbitMqPublishNackedException`; an unroutable message (`basic.return`) throws
  `RabbitMqUnroutableMessageException`. Commands and responses are published as mandatory because each has
  one owner queue; events are only mandatory when `RequireRoutableEvents` is set, since an event may have no
  subscriber.
- If no confirm arrives within `PublisherConfirmTimeout` (default 30 s), or the connection is lost while
  waiting, `RabbitMqPublishOutcomeUnknownException` is thrown: the broker may already hold the message, so
  it is not reported as failed. The Outbox republishes it with the same `MessageId` (at-least-once).
- A confirm means RabbitMQ accepted responsibility for the message. It does not mean a consumer received or
  processed it. For persistent messages on durable queues (what Lycia publishes) the broker confirms after
  persisting; quorum queues confirm after a quorum of replicas accepted.
- Set `PublisherConfirms = false` for the previous fire-and-forget behavior, under which Outbox messages
  settle as `ConfirmationUnknown`.

The netstandard2.0 build uses RabbitMQ.Client 6.8.1 and resolves confirmations from the broker's ack/nack
frames rather than `WaitForConfirms`, which can misreport a nack as an ack under rapid publishing. The
net8.0+ builds use RabbitMQ.Client 7.1.2's tracked publisher confirmations.
