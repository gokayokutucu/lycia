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

Application/response endpoint portions use invariant lowercase and ignore dash, underscore, dot, and
whitespace, so equivalent replicas share one durable and queue group. To migrate, stop old consumers,
verify retained stream data, activate the canonical durable/group, then remove the old consumer after
draining. Lycia does not dual-bind or delete resources.

Responses use `Context.Respond`, never event publish. Another independently configured durable or queue
group can still consume the subject; delivery is at least once and ownership is not global exclusivity.
