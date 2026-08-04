# Lycia.Extensions.Kafka

Commands use one consumer group per logical owner/command queue. Events use one group per
message/handler/application subscription, so separate subscribers each receive the stream while replicas
inside a subscription compete. Responses use requester-specific logical topics, never a topic per saga.

Ordering is partition-scoped. The default partition key is `CorrelationId` (then `SagaId`, then
`MessageId`). A group can actively process at most one record per assigned partition, so replicas above
the partition count remain idle. Lycia commits offsets after handler acknowledgement; failures may cause
redelivery, and handlers must be idempotent. Kafka transaction features do not provide application-level
exactly-once side effects.
