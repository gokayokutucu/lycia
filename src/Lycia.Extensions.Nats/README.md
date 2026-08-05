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

## Scheduling

The validated NATS 2.11/JetStream baseline does not expose a Lycia-validated native delayed-delivery primitive.
`NatsSchedulingMode.FallbackToWorker` therefore uses the Redis-backed `SchedulerWorker`; `Disabled` explicitly uses
the same worker path without capability probing, while `NativeOnly` fails during transport construction. Lycia does
not emulate delay with stream retention. The worker publishes to the normal final subject after the due time and
preserves command, event, or targeted-response semantics. Delivery is at least once, and handlers must be idempotent.
