# Lycia.Extensions.Kafka

Commands use one consumer group per logical owner/command queue. Events use one group per
message/handler/application subscription, so separate subscribers each receive the stream while replicas
inside a subscription compete. Responses use requester-specific logical topics, never a topic per saga.

Ordering is partition-scoped. The default partition key is `CorrelationId` (then `SagaId`, then
`MessageId`). A group can actively process at most one record per assigned partition, so replicas above
the partition count remain idle. Lycia commits offsets after handler acknowledgement; failures may cause
redelivery, and handlers must be idempotent. Kafka transaction features do not provide application-level
exactly-once side effects.

Application/response endpoint portions use invariant lowercase and ignore dash, underscore, dot, and
whitespace, so equivalent replicas share one group. Canonical migration creates a new group identity:
choose starting offsets explicitly, stop the old group, validate the canonical group, then retire the
old one. Lycia does not delete or run both automatically.

Responses use `Context.Respond` and a canonical endpoint topic/group, never event broadcast. A separate
Kafka group can still consume the topic; ownership is not global exclusivity or exactly-once processing.

## Scheduling

Kafka scheduling deliberately uses the shared durable `SchedulerWorker` and final normal Kafka topic. Topic TTL or
retention does not hold and later release a message, so Lycia creates neither predefined delay topics nor arbitrary
per-delay topics. Redis stores the due record, replicas claim it with a lease and fencing token, and the worker marks
it complete only after the idempotent producer accepts the final publish. Crash windows can repeat delivery; consumers
must remain idempotent. Kafka business-topic cleanup is `ReportOnly` by default, and scheduling does not require
destructive admin rights.
