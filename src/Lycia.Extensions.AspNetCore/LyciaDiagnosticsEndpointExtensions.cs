// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Abstractions.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Lycia.Extensions.AspNetCore;

/// <summary>
/// Maps the opt-in Lycia reliability diagnostics endpoint onto an ASP.NET Core application.
/// </summary>
public static class LyciaDiagnosticsEndpointExtensions
{
    /// <summary>The default diagnostics route, used when no path is given to <see cref="MapLyciaDiagnostics"/>.</summary>
    public const string DefaultPattern = "/diagnostics/lycia";

    /// <summary>
    /// Maps <c>GET {pattern}</c> (default <c>/diagnostics/lycia</c>), which reports Lycia's resolved
    /// reliability/persistence topology as JSON: delivery guarantee, persistence mode and provider, Split
    /// Store canonical/operational stores, and Inbox/Outbox/journal/reconciliation capability flags.
    /// </summary>
    /// <remarks>
    /// This is a configuration/topology endpoint, not a health check. It answers "how is Lycia configured
    /// and what did it resolve?", not "are RabbitMQ/Redis/PostgreSQL/SQL Server/Kafka/NATS reachable right
    /// now?" - it performs no network calls and probes no configured infrastructure. It normally returns
    /// <c>200 OK</c> whenever the application's <see cref="ILyciaReliabilityDiagnostics"/> can produce a
    /// snapshot, which requires no external system to be available. Composes with standard ASP.NET Core
    /// endpoint conventions such as authorization:
    /// <code>
    /// app.MapLyciaDiagnostics()
    ///     .RequireAuthorization("Operations");
    /// </code>
    /// This endpoint is entirely opt-in: calling <c>AddLycia(...)</c> never maps it on its own, and an
    /// application that never calls this method exposes no Lycia diagnostics route. Nothing beyond
    /// <c>Lycia.Extensions.AspNetCore</c> is required; <c>AddLycia(...)</c> already registers
    /// <see cref="ILyciaReliabilityDiagnostics"/>, which remains the single source of truth this endpoint
    /// projects to JSON - it never infers or re-derives persistence topology itself.
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder to map the diagnostics route onto.</param>
    /// <param name="pattern">
    /// The route pattern to map. Defaults to <see cref="DefaultPattern"/> (<c>/diagnostics/lycia</c>).
    /// </param>
    /// <returns>
    /// An <see cref="IEndpointConventionBuilder"/> for the mapped route, so the caller can add standard
    /// ASP.NET Core conventions (<c>RequireAuthorization</c>, <c>WithName</c>, <c>ExcludeFromDescription</c>,
    /// and so on) exactly as it would for any other minimal API endpoint.
    /// </returns>
    public static IEndpointConventionBuilder MapLyciaDiagnostics(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultPattern)
    {
        if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));
        if (string.IsNullOrWhiteSpace(pattern)) throw new ArgumentException("Route pattern must not be empty.", nameof(pattern));

        return endpoints
            .MapGet(pattern, (ILyciaReliabilityDiagnostics diagnostics) =>
                Results.Ok(LyciaDiagnosticsResponse.FromSnapshot(diagnostics.GetSnapshot())))
            .WithName("LyciaDiagnostics")
            .WithDisplayName("Lycia reliability diagnostics");
    }
}
