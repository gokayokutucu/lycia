# Lycia

[![NuGet](https://img.shields.io/nuget/v/Lycia.svg)](https://www.nuget.org/packages/Lycia)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Lycia.svg)](https://www.nuget.org/packages/Lycia)
![Target Framework](https://img.shields.io/badge/.NET-netstandard2.0%20%7C%20net8.0%20%7C%20net9.0-blue)
[![Build](https://github.com/gokayokutucu/lycia/actions/workflows/dotnet.yml/badge.svg)](https://github.com/gokayokutucu/lycia/actions/workflows/dotnet.yml)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![GitHub release](https://img.shields.io/github/v/release/gokayokutucu/lycia)](https://github.com/gokayokutucu/lycia/releases)

**Lycia** is the **main package** of the Lycia framework.  
It provides the saga infrastructure, orchestration, and choreography support.  
Extensions are published separately under `Lycia.Extensions.*`:

| Package | Contents |
| --- | --- |
| `Lycia.Extensions` | Transport-independent registration, middleware, logging, retry, Redis saga store |
| `Lycia.Extensions.RabbitMq` | RabbitMQ transport, topology, DLQ, native TTL+DLX scheduling strategy |
| `Lycia.Extensions.Scheduling` | Durable transport-independent scheduling (SchedulerWorker, Redis store, vacuum) |
| `Lycia.Extensions.Nats` | NATS JetStream / Core NATS transport |
| `Lycia.Extensions.Kafka` | Kafka transport |
| `Lycia.Extensions.OpenTelemetry` | Tracing integration |

**Lycia** began with a vision on *May 28, 2023*.  
Our motto: *“Turning difficult paths into joyful simplicity.”* Inspired by the ancient *Lycian Way*, we set out to build a framework that makes complex saga workflows easy to manage — supported by strong documentation and aligned with modern software practices.

Lycia is a messaging framework (Message-oriented Middleware, MoM) built for .NET applications, supporting .NET Standard 2.0 and higher. It provides a robust foundation for distributed systems where reliable message flow and state coordination are essential.

For architectural deep-dive, compensation coordination, and integration test strategies, see [DEVELOPERS.md](DEVELOPERS.md).
 
---

## Getting Started / Samples

Explore the [samples/](samples) folder for real-world usage:  
- **Sample.Order.Api** – API entrypoint  
- **Sample.Order.Orchestration.Consumer** – Coordinated Responsive Saga (asynchronous request–response orchestration using `CoordinatedResponsiveSagaHandler`)  
- **Sample.Order.Choreography.Consumer** – Reactive Saga (stateless event-driven choreography using `ReactiveSagaHandler`)  
- **Sample.Order.Orchestration.Seq.Consumer** – Coordinated Saga (stateful sequential orchestration using `CoordinatedSagaHandler`, with compensation flows)  

---

## Our Mission

- **Simplicity**: Define complex orchestration flows with ease.  
- **Flexibility**: Support both orchestration (which we call *Coordinated Saga*) and choreography (our term: *Reactive Saga*) patterns.  
- **Portability**: Work out of the box with popular infrastructures like RabbitMQ and Redis.  
- **Robust Documentation**: Step-by-step guides, code samples, and best practices to lead the way.

---

## What Makes Lycia Different

Unlike other frameworks, Lycia offers:

- **Minimal Setup** – Start with a single line:

```csharp
services.AddLycia(Configuration)
        .AddSagasFromCurrentAssembly()
        .Build();

services.AddLyciaRabbitMq(); // transport package, e.g. Lycia.Extensions.RabbitMq
```

- **Clear Naming and Semantics**:  
  - *Coordinated Saga* → central orchestrator-based saga management  
  - *Reactive Saga* → event-driven choreography approach  

- Built-in support for **idempotency**, **timeouts**, and **in-process retries with Polly, Ack/Nack + DLQ support on RabbitMQ**  
- **Default Middleware Pipeline (Logging + Tracing + Retry, replaceable via UseSagaMiddleware)**  
- **Extensibility**: Easily plug in custom implementations of `IMessageSerializer`, `IEventBus`, or `ISagaStore`.

## Strongly Typed Command Ownership

Commands declare their one logical owner in the contract. No destination string or handler name is
passed to `Send`:

```csharp
public interface IStockServiceCommand : ICommandEndpoint { }

public sealed class ReserveStockCommand : CommandBase, IStockServiceCommand
{
    public Guid OrderId { get; init; }
}

public sealed class ReserveStockHandler
    : CoordinatedSagaHandler<ReserveStockCommand, StockSagaData>
{
    public override Task HandleAsync(
        ReserveStockCommand command,
        CancellationToken cancellationToken = default)
    {
        // The handler contains business logic only; transport routing stays unchanged.
        return Context.MarkAsComplete<ReserveStockCommand>(cancellationToken);
    }
}

await sagaContext.Send(new ReserveStockCommand { OrderId = orderId }, cancellationToken);
```

`IStockServiceCommand` deterministically resolves to the owner `StockService`. The owning host must use
`ApplicationId = StockService` (comparison is ordinal and case-insensitive). Startup fails when a command
has no owner marker, more than one owner marker, a handler in the wrong application, or more than one
handler type in that application.

The generated topology is transport-specific but semantically equivalent:

| Kind | RabbitMQ | NATS | Kafka |
|---|---|---|---|
| Command | direct `command.ReserveStockCommand`, queue `command.ReserveStockCommand.StockService`, key `StockService` | subject `command.StockService.ReserveStockCommand`, one durable consumer | topic `lycia.command.StockService.ReserveStockCommand`, one owner consumer group |
| Event | fanout `event.StockReservedEvent`, one queue per handler/application | subject `event.StockReservedEvent`, one durable consumer per subscription | topic `lycia.event.StockReservedEvent`, one group per subscription |
| Response | direct requester key and shared requester queue | `response.{Requester}.{Type}` | `lycia.response.{Requester}.{Type}` |

Responses carry `RequestId` and canonical `ResponseEndpoint` (`ReplyTo` remains an obsolete alias);
`CorrelationId` and `SagaId` correlate workflow state inside
the requester’s shared response queue. Lycia never creates a queue or topic per saga instance.

### Replicas are competing consumers

A replica is another running copy of the same logical application: another Kubernetes pod, Docker
container, process, or host. Every replica of a service must use the same `ApplicationId`:

```text
Correct:
StockService replica 1 -> ApplicationId = StockService
StockService replica 2 -> ApplicationId = StockService
StockService replica 3 -> ApplicationId = StockService

Incorrect:
StockService replica 1 -> ApplicationId = StockService-1
StockService replica 2 -> ApplicationId = StockService-2
StockService replica 3 -> ApplicationId = StockService-3
```

Correctly configured replicas share one queue, durable consumer, or consumer group, and compete for
work. Under normal broker operation one delivery is handled by one replica. The incorrect form creates
independent logical consumers and can create extra queues or duplicate event processing.

The invariant is **one handler type, many runtime handler instances**. One command owner and one command
handler keep bounded-context ownership clear, prevent accidental command broadcasts, and keep command
addresses stable when implementation classes are renamed. Commands are intentions; publish an event when
multiple independent components must react to a fact.

RabbitMQ, JetStream, and Kafka use at-least-once delivery patterns in failure scenarios. Lycia does not
claim exactly-once application processing; handlers must remain idempotent. Kafka ordering is scoped to a
partition, and replicas beyond the partition count cannot consume concurrently.

Transport packages are `Lycia.Extensions.RabbitMq`, `Lycia.Extensions.Nats`, and
`Lycia.Extensions.Kafka`; each registers itself after `AddLycia(...)` (`AddLyciaRabbitMq()`,
`AddLyciaNats(...)`, `AddLyciaKafka(...)`). JetStream is the durable NATS default; Core NATS is an
explicit ephemeral mode.

---

## Durable message scheduling

Reference `Lycia.Extensions.Scheduling`, register Redis-backed scheduling once, and schedule commands,
events, or targeted responses from any saga context:

```csharp
services.AddLyciaScheduling(options =>
{
    options.AllowDynamicDelays = false;
    options.Worker.LeaseDuration = TimeSpan.FromSeconds(30);
    options.Worker.LeaseRenewInterval = TimeSpan.FromSeconds(10);
    options.Vacuum.ApplicationTopology.Mode = VacuumMode.ReportOnly;
});

var scheduleId = await Context.Schedule(
    new CancelOrderCommand { OrderId = orderId },
    ScheduleDelay.ThirtySeconds,
    cancellationToken);
```

`ScheduleId` identifies the scheduling operation and is deliberately different from `MessageId`. Pass a stable
`ScheduleId` when retrying schedule creation. `ScheduleAt` accepts an absolute UTC instant; enum months are fixed
30-day durations and `OneYear` is 365 days, so calendar-aware rules should calculate an instant and use `ScheduleAt`.
Pending schedules can be cancelled idempotently or rescheduled before dispatch.

RabbitMQ predefined buckets use one fixed-TTL queue per destination and bucket, then dead-letter to the final
exchange without a plugin. Arbitrary RabbitMQ buckets are opt-in because they create dynamic queues. Kafka always
uses the durable `SchedulerWorker`; Kafka retention is not delayed delivery. The validated NATS 2.11 baseline also
falls back to `SchedulerWorker`, and `NativeOnly` fails at startup. Dispatch is at least once around crash windows,
so handlers must be idempotent.

Dynamic resource cleanup requires exact registry provenance, retention, no active manifest or schedule, an empty and
unused broker resource, a fenced lease, and conditional deletion. Predefined buckets are retained. Ordinary topology
defaults to `ReportOnly`, requires quarantine, and needs a second destructive opt-in. Scheduling exposes the
`Lycia.Scheduling` activity source and meter plus the `LyciaScheduling` health check.

---

## Quick Start

**Coordinated Saga**: Uses a central orchestrator to manage the full lifecycle of a saga. This handler starts a saga, executes the business logic step-by-step, and coordinates the flow by publishing commands/events. Ideal when you need deterministic, centralized control.

**Stateful Model**: Coordinated sagas always use `TSagaData` to maintain saga state across all steps.

**Coordinated Saga (Orchestration)**

```csharp
public class CreateInvoiceSagaHandler :
    StartCoordinatedSagaHandler<CreateInvoiceCommand, CreateInvoiceSagaData>
{
    public override async Task HandleAsync(CreateInvoiceCommand cmd, CancellationToken ct = default)
    {
        // business logic
        await Context.Publish(new InvoiceStartedEvent { InvoiceId = cmd.InvoiceId }, ct);
        await Context.MarkAsComplete<CreateInvoiceCommand>(ct);
    }
}
```

**Reactive Saga**: Implements event-driven choreography. Each handler reacts only to the event it subscribes to. There is no central orchestrator; instead, services collaborate by emitting events. Ideal for loosely coupled systems and autonomous microservices.

**Stateless Model**: Reactive sagas do not use saga state (`TSagaData`); each event is handled independently without maintaining cross-step state.

**Reactive Saga (Choreography)**

```csharp
public class InventorySagaHandler :
    ReactiveSagaHandler<OrderCreatedEvent>,
    ISagaCompensationHandler<PaymentFailedEvent>
{
    public override async Task HandleAsync(OrderCreatedEvent evt, CancellationToken ct = default)
    {
        // Reserve inventory
        await Context.Publish(new InventoryReservedEvent { OrderId = evt.OrderId }, ct);
        await Context.MarkAsComplete<OrderCreatedEvent>(ct);
    }

    public Task CompensateAsync(PaymentFailedEvent failed, CancellationToken ct = default)
    {
        // Release reserved stock
        InventoryService.ReleaseStock(failed.OrderId);
        return Task.CompletedTask;
    }
}
```

### Additional Saga Handler Examples

**Coordinated Responsive Saga**: Similar to a coordinated saga, but also handles direct responses (e.g., request/response patterns). Useful when the saga step must wait for a specific success or failure message before moving forward.

Like all coordinated sagas, this pattern is **stateful** and requires a `TSagaData` object to track progress across asynchronous request–response steps.

**CoordinatedResponsiveSagaHandler**

```csharp
public class CreateOrderSagaHandler :
    StartCoordinatedResponsiveSagaHandler<CreateOrderCommand, OrderCreatedResponse, CreateOrderSagaData>
{
    public override async Task HandleAsync(CreateOrderCommand cmd, CancellationToken ct = default)
    {
        // Business logic
        await Context.Respond(cmd, new OrderCreatedResponse { OrderId = cmd.OrderId }, ct);
        await Context.MarkAsComplete<CreateOrderCommand>(ct);
    }
    
    public override async Task HandleSuccessResponseAsync(OrderCreatedResponse response, CancellationToken cancellationToken = default)
    {
        // Order created, reserve inventory
        await Context.Send(new ReserveInventoryCommand
        {
            OrderId = response.OrderId
        }, cancellationToken);
        await Context.MarkAsComplete<OrderCreatedResponse>();
    }
}
```

---

### What Lycia Emits

- A span per saga step (`Saga.<HandlerName>`)
- Attributes:
  - `lycia.saga.id`
  - `lycia.message.id`
  - `lycia.correlation.id`
  - `lycia.application.id`
  - `lycia.saga.step.status`
- Automatic W3C trace propagation through messages (RabbitMQ / EventBus)

### How It Works

Tracing is added without requiring any saga code changes:
- The middleware creates spans around each handler invocation.
- `LyciaTracePropagation` injects `traceparent`/`tracestate` into message headers.
- The listener extracts headers and restores parent-child relationships.

This produces a full cross-service trace chain in Grafana Tempo or Jaeger.

---

## Timeline

- **May 28, 2023** – The idea was born.  
- **Initial Goal** – To provide a saga framework that avoids complexity and is easy to use by anyone.  
- **Today** – Development accelerated by "vibe-coding"; includes tests, integrations, and real-world usage scenarios.

---

## What's Next

- **Native Inbox / Outbox Guarantees**
  - State-consistency
  - Cross-service delivery reliability
  - Message replay safety
  
- **Distributed Delayed Message Scheduling**
  - Compensation timers
  - Cron-like orchestration intervals
  - Durable timing guarantees

- **Schema Intelligence**
  - Avro/Protobuf registry integration (including the built‑in `AvroSchemaConverter`)
  - Backward/forward compatibility detection
  - Contract-driven saga evolution

## Durable request-response identity

Responses are targeted saga continuations, never broadcast events. Use
`Context.Respond(request, response, cancellationToken)` to send through the broker to the canonical
`ResponseEndpoint` owned by the waiting saga application. `ReplyTo` is an obsolete alias for the same
value. `Context.Publish(response)` fails explicitly.

| Field | Meaning |
| --- | --- |
| `MessageId` | Identity and idempotency key of this concrete message |
| `RequestId` | Request answered by a response; a new request uses its own `MessageId` |
| `CorrelationId` | Complete business workflow |
| `CausationId` | Direct causing message |
| `ParentMessageId` | Saga-step and compensation parent |
| `SagaId` | Durable saga instance |
| `ResponseEndpoint` | Logical application waiting for the response |

In the M1–M5 flow, request M1 has `RequestId=M1`; response M2 has a distinct identity and
`RequestId=CausationId=ParentMessageId=M1`; child request M3 has `RequestId=M3` and is caused by M2;
response M4 answers M3; request M5 is caused by M4. Correlation and saga IDs remain stable.
Compensation walks only `ParentMessageId`.

`OrderCreatedResponse` intentionally crosses the broker even when order creation is local. The durable,
observable transition lets another replica reload Redis state and continue, tolerates failure between
steps, and preserves idempotent redelivery.

### Canonical application identities and migration

Topology keys use invariant lowercase and ignore `-`, `_`, `.`, and whitespace. `StockService`,
`stock-service`, `stock_service`, and `STOCK.SERVICE` all become `stockservice`; other characters and
values without an alphanumeric character are rejected. Equivalent replicas share one queue/group.

Canonicalization can rename broker resources. Drain old RabbitMQ queues and stop old consumers; validate
NATS stream retention before replacing old durables/groups; choose Kafka starting offsets deliberately
for the new canonical group. Remove old resources only after validation. Lycia never deletes production
resources or dual-binds old and new names automatically.

Typed endpoints, discovery, startup validation, and canonical matching enforce ownership inside Lycia,
not globally at the broker. Another RabbitMQ queue, NATS group/durable, or Kafka group can receive the
same logical message. Delivery follows at-least-once patterns, not exactly once; handlers must be
idempotent.

## License

This project is licensed under the [Apache 2.0 License](LICENSE).
---

## Why Lycia? (Deep Dive Highlights)

In addition to minimal setup and clear semantics, Lycia offers:

- **SagaDispatcher** and **CompensationCoordinator** core components  
- **Built-in Idempotency** and cancellation flow (`MarkAsCancelled<T>()`)  
- **Custom Retry Hooks** finalized via `IRetryPolicy` (with Polly-based default implementation, configurable via `ConfigureRetry`), supporting exponential backoff, jitter, and per-exception retry strategies  
- **Choreography & Orchestration** support via `ReactiveSagaHandler<T>` and `CoordinatedSagaHandler<T>`  
- **RedisSagaStore** built-in extension support with TTL, CAS, parent-child message tracing  
- **RabbitMQ EventBus** built-in extension support with Dead Letter Queue (DLQ) and header normalization  
- **ISagaContextAccessor** for contextual saga state access  
- **Fluent Middleware Pipeline**: Default logging, retry, and tracing middleware (via `ActivityTracingMiddleware`), all replaceable via middleware slots (`ILoggingSagaMiddleware`, `IRetrySagaMiddleware`, `ITracingSagaMiddleware`)  
- **Fluent Configuration API**: Easily plug your custom serializers, stores and buses  
- **Detailed Integration Tests** for Redis, RabbitMQ (including Ack/Nack/DLQ behavior), Compensation logic  
- **Appsettings.json Support**: Environment-based saga configuration
