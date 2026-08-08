// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Middleware;
using Lycia.Retry;
using Lycia.Saga.Abstractions.Middlewares;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#if NET8_0_OR_GREATER
using Polly.Retry;
#endif

namespace Lycia.Extensions;

/// <summary>
/// Fluent middleware DSL reached via <see cref="LyciaBuilder.AddMiddleware"/>. Configures the same
/// three fixed pipeline slots (logging, tracing, retry) that <see cref="LyciaBuilder.UseSagaMiddleware"/>
/// and <see cref="LyciaBuilder.UseLoggingMiddleware{TLogging}"/> already drive via
/// <see cref="ISagaMiddleware"/> registrations and the ordered <c>IReadOnlyList&lt;Type&gt;</c> pipeline;
/// this DSL does not introduce a second middleware pipeline.
/// </summary>
public sealed class LyciaMiddlewareBuilder
{
    private readonly LyciaBuilder _builder;
    private readonly IServiceCollection _services;

    // Reflect the defaults LyciaRegistrationExtensions.RegisterMiddlewareAndPolicies already registered.
    private Type _loggingType = typeof(LoggingMiddleware);
    private Type _tracingType = typeof(ActivityTracingMiddleware);
    private Type _retryType = typeof(RetryMiddleware);

    internal LyciaMiddlewareBuilder(LyciaBuilder builder, IServiceCollection services)
    {
        _builder = builder;
        _services = services;
    }

    /// <summary>Uses the built-in logging middleware (the default logging slot).</summary>
    public LyciaMiddlewareBuilder WithLogging() => WithLogging<LoggingMiddleware>();

    /// <summary>Replaces the logging middleware slot with a custom implementation.</summary>
    public LyciaMiddlewareBuilder WithLogging<TLogging>()
        where TLogging : class, ISagaMiddleware, ILoggingSagaMiddleware
    {
        ReplaceSlot(ref _loggingType, typeof(TLogging));
        Apply();
        return this;
    }

    /// <summary>Uses the built-in tracing middleware (the default tracing slot).</summary>
    public LyciaMiddlewareBuilder WithTracing() => WithTracing<ActivityTracingMiddleware>();

    /// <summary>Replaces the tracing middleware slot with a custom implementation.</summary>
    public LyciaMiddlewareBuilder WithTracing<TTracing>()
        where TTracing : class, ISagaMiddleware, ITracingSagaMiddleware
    {
        ReplaceSlot(ref _tracingType, typeof(TTracing));
        Apply();
        return this;
    }

    /// <summary>Uses the built-in Polly-based retry middleware, optionally configuring <see cref="RetryStrategyOptions"/>.</summary>
    public LyciaMiddlewareBuilder WithRetry(Action<RetryStrategyOptions>? configure = null)
    {
        if (configure != null) _builder.ConfigureRetry(configure);
        return WithRetry<RetryMiddleware>();
    }

    /// <summary>Replaces the retry middleware slot with a custom implementation.</summary>
    public LyciaMiddlewareBuilder WithRetry<TRetry>()
        where TRetry : class, ISagaMiddleware, IRetrySagaMiddleware
    {
        ReplaceSlot(ref _retryType, typeof(TRetry));
        Apply();
        return this;
    }

    private void ReplaceSlot(ref Type slot, Type next)
    {
        var previous = slot;
        slot = next;
        if (previous == next) return;

        for (var i = _services.Count - 1; i >= 0; i--)
        {
            var sd = _services[i];
            if (sd.ServiceType == typeof(ISagaMiddleware) && sd.ImplementationType == previous)
                _services.RemoveAt(i);
        }
    }

    private void Apply()
    {
        foreach (var slot in new[] { _loggingType, _tracingType, _retryType })
        {
            var exists = _services.Any(sd => sd.ServiceType == typeof(ISagaMiddleware) && sd.ImplementationType == slot);
            if (!exists) _services.AddScoped(typeof(ISagaMiddleware), slot);
        }

        _services.RemoveAll(typeof(IReadOnlyList<Type>));
        var loggingType = _loggingType;
        var tracingType = _tracingType;
        var retryType = _retryType;
        _services.AddScoped<IReadOnlyList<Type>>(_ => new List<Type> { loggingType, tracingType, retryType });
    }
}
