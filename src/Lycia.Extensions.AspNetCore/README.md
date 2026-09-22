# Lycia.Extensions.AspNetCore

An opt-in ASP.NET Core Minimal API endpoint that reports Lycia's resolved reliability/persistence
topology as JSON.

```csharp
app.MapLyciaDiagnostics();
```

Default route: `GET /diagnostics/lycia`. Custom route:

```csharp
app.MapLyciaDiagnostics("/internal/lycia");
```

The returned builder composes with standard ASP.NET Core endpoint conventions:

```csharp
app.MapLyciaDiagnostics()
    .RequireAuthorization("Operations");
```

## What it is

A thin, secret-free projection of the existing `ILyciaReliabilityDiagnostics` snapshot: delivery
guarantee, persistence mode and provider, Split Store canonical/operational stores, and
Inbox/Outbox/journal/reconciliation capability flags. It never infers topology itself.

## What it is not

Not a health check. It answers "how is Lycia configured?", not "is Redis/RabbitMQ/PostgreSQL/SQL
Server/Kafka/NATS reachable right now?". It performs no network calls and probes no configured
infrastructure; it normally returns `200 OK`.

## Opt-in

Calling `AddLycia(...)` never maps this route. An application that does not call
`MapLyciaDiagnostics()` exposes no Lycia diagnostics endpoint. Authorization is standard ASP.NET
Core - Lycia implements no authentication or authorization of its own.

See the root [README.md](https://github.com/gokayokutucu/lycia#diagnostics) for the full response shape.
