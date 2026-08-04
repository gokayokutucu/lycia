# Lycia.Extensions.OpenTelemetry

This package connects Lycia saga activities to OpenTelemetry tracing. Register it after Lycia and add
the `Lycia` activity source to the application's OpenTelemetry configuration.

Message, request, correlation, causation, parent, saga, and canonical application identities flow through
the transport headers and appear on producer and consumer spans. Tracing does not change delivery
semantics: broker transports remain at least once, so handlers must be idempotent.

Use `Context.Respond(request, response, cancellationToken)` for targeted responses. Responses cannot be
broadcast with `Publish`; `ResponseEndpoint` is canonical and `ReplyTo` remains an obsolete compatibility
alias.
