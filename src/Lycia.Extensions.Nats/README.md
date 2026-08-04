# Lycia.Extensions.Nats

This transport uses JetStream by default. Commands and responses use durable logical consumers;
events use one durable consumer per handler/application subscription. Replicas share the same durable
consumer. Set `UseJetStream = false` only for explicitly ephemeral Core NATS workloads; Core NATS
cannot provide durable saga delivery when subscribers are absent.

Subjects:

- Command: `command.{Owner}.{MessageType}`
- Event: `event.{MessageType}`
- Response: `response.{RequesterApplicationId}.{MessageType}`

JetStream consumers use explicit acknowledgements, bounded redelivery, and stable names derived from
Lycia's logical queue identity. Handlers must remain idempotent.
