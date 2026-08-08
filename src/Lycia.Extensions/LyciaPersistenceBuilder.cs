// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Extensions;

/// <summary>
/// Fluent persistence DSL reached via <see cref="LyciaBuilder.UsePersistence"/>. Only exposes providers that
/// exist today (the Redis saga store); future persistence packages (inbox/outbox/split-store providers)
/// extend this builder with their own <c>With...()</c> extension methods, the same pattern
/// <see cref="LyciaTransportBuilder"/> uses for transport packages.
/// </summary>
public sealed class LyciaPersistenceBuilder
{
    /// <summary>The underlying service collection persistence providers register into.</summary>
    public IServiceCollection Services { get; }

    /// <summary>The configuration Lycia was bootstrapped with.</summary>
    public IConfiguration Configuration { get; }

    internal LyciaPersistenceBuilder(IServiceCollection services, IConfiguration configuration)
    {
        Services = services;
        Configuration = configuration;
    }

    /// <summary>
    /// Registers the Redis-backed saga store. This is Lycia's current default persistence provider, so
    /// calling this is only required to be explicit or to restore Redis after another
    /// <c>UsePersistence()</c>/<c>UseSagaStore&lt;T&gt;()</c> call replaced it.
    /// </summary>
    public LyciaPersistenceBuilder WithRedisSagaStore()
    {
        LyciaRegistrationExtensions.RegisterRedisSagaStore(Services);
        return this;
    }
}
