// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions;
using Lycia.Extensions.Nats;
using Lycia.Extensions.RabbitMq;
using Lycia.Extensions.Scheduling;
using Lycia.Middleware;
using Lycia.Persistence.Redis;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Middlewares;
using Lycia.Saga.Messaging;
using Lycia.Saga.Messaging.Handlers;
using Lycia.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lycia.Tests;

public class LyciaDslTests
{
    private static IConfiguration Configuration(string applicationId = "DslTestApp") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApplicationId"] = applicationId,
                ["Lycia:EventBus:ConnectionString"] = "amqp://guest:guest@localhost:5672/",
                ["Lycia:EventStore:ConnectionString"] = "localhost:6379"
            })
            .Build();

    // 1 & 10: root DSL finalizes registrations without a second explicit Build() call.
    [Fact]
    public void AddLycia_With_Callback_Finalizes_Without_Explicit_Build()
    {
        var services = new ServiceCollection();

        // No .Build() call after this — the callback boundary itself finalizes registration.
        services.AddLycia(Configuration(), lycia =>
        {
            lycia.AddSaga(typeof(DslProbeSagaHandler));
        });

        var provider = services.BuildServiceProvider();
        var map = provider.GetRequiredService<IDictionary<string, (Type MessageType, Type HandlerType)>>();

        Assert.Contains(map.Values, v => v.HandlerType == typeof(DslProbeSagaHandler));
    }

    // 2: AddSagas().FromCurrentAssembly() delegates to the existing discovery implementation.
    // Registration into the service collection happens immediately (not deferred to Build()),
    // so this is checked without finalizing — avoiding a whole-assembly topology validation pass.
    [Fact]
    public void AddSagas_FromCurrentAssembly_Delegates_To_Existing_Discovery()
    {
        var services = new ServiceCollection();
        var builder = services.AddLycia(Configuration());

        builder.AddSagas().FromCurrentAssembly();

        Assert.Contains(services, sd => sd.ServiceType == typeof(DslProbeSagaHandler));
    }

    // 3: UseTransport().RabbitMq() registers the same effective transport as the existing API.
    [Fact]
    public void UseTransport_RabbitMq_Registers_Same_Effective_Transport_As_Legacy_Api()
    {
        var legacyServices = new ServiceCollection();
        legacyServices.AddLycia(Configuration());
#pragma warning disable CS0618
        legacyServices.AddLyciaRabbitMq();
#pragma warning restore CS0618

        var dslServices = new ServiceCollection();
        var dslBuilder = dslServices.AddLycia(Configuration());
        dslBuilder.UseTransport().RabbitMq();

        var legacyDescriptor = Assert.Single(legacyServices, sd => sd.ServiceType == typeof(IEventBus));
        var dslDescriptor = Assert.Single(dslServices, sd => sd.ServiceType == typeof(IEventBus));

        Assert.Equal(ServiceLifetime.Singleton, legacyDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, dslDescriptor.Lifetime);
        Assert.NotNull(legacyDescriptor.ImplementationFactory);
        Assert.NotNull(dslDescriptor.ImplementationFactory);
    }

    // 4: duplicate transport provider selection fails clearly instead of last-registration-wins.
    [Fact]
    public void UseTransport_With_Two_Different_Providers_Fails_Clearly()
    {
        var services = new ServiceCollection();
        var builder = services.AddLycia(Configuration());

        builder.UseTransport().RabbitMq();

        var ex = Assert.Throws<InvalidOperationException>(() => builder.UseTransport().Nats());
        Assert.Contains("RabbitMq", ex.Message);
        Assert.Contains("Nats", ex.Message);
    }

    // 5 & 6: predefined/dynamic delay semantic mapping onto SchedulingOptions.AllowDynamicDelays.
    [Fact]
    public void Scheduling_WithPredefinedDelays_Sets_AllowDynamicDelays_False()
    {
        var services = new ServiceCollection();
        var builder = services.AddLycia(Configuration());

        builder.AddScheduling().WithPredefinedDelays();

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<SchedulingOptions>>().Value;
        Assert.False(options.AllowDynamicDelays);
    }

    [Fact]
    public void Scheduling_WithDynamicDelays_Sets_AllowDynamicDelays_True()
    {
        var services = new ServiceCollection();
        var builder = services.AddLycia(Configuration());

        builder.AddScheduling().WithDynamicDelays();

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<SchedulingOptions>>().Value;
        Assert.True(options.AllowDynamicDelays);
    }

    // 7: WithWorker(...) applies worker configuration onto SchedulingOptions.Worker.
    [Fact]
    public void Scheduling_WithWorker_Applies_Worker_Configuration()
    {
        var services = new ServiceCollection();
        var builder = services.AddLycia(Configuration());

        builder.AddScheduling().WithWorker(w =>
        {
            w.LeaseDuration = TimeSpan.FromSeconds(30);
            w.LeaseRenewInterval = TimeSpan.FromSeconds(10);
        });

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<SchedulingOptions>>().Value;
        Assert.Equal(TimeSpan.FromSeconds(30), options.Worker.LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Worker.LeaseRenewInterval);
    }

    // 8: middleware fluent methods register/configure the existing ISagaMiddleware slots.
    [Fact]
    public void Middleware_With_Fluent_Methods_Configure_Existing_Slots()
    {
        var services = new ServiceCollection();
        var builder = services.AddLycia(Configuration());

        builder.AddMiddleware()
            .WithLogging<DslProbeLoggingMiddleware>()
            .WithTracing()
            .WithRetry();

        var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IReadOnlyList<Type>>();

        Assert.Equal(typeof(DslProbeLoggingMiddleware), pipeline[0]);
        Assert.Equal(typeof(ActivityTracingMiddleware), pipeline[1]);
        Assert.Equal(typeof(RetryMiddleware), pipeline[2]);
        Assert.Single(services, sd => sd.ServiceType == typeof(ISagaMiddleware) && sd.ImplementationType == typeof(DslProbeLoggingMiddleware));
        Assert.DoesNotContain(services, sd => sd.ServiceType == typeof(ISagaMiddleware) && sd.ImplementationType == typeof(LoggingMiddleware));
    }

    // 11: the persistence builder exists as its own concern-specific type (not scattered on the root
    // LyciaBuilder) and registers the same ISagaStore the default bootstrap already provides, so future
    // provider packages can extend LyciaPersistenceBuilder without touching LyciaBuilder itself.
    [Fact]
    public void UsePersistence_WithRedisSagaStore_Registers_ISagaStore()
    {
        var services = new ServiceCollection();
        var builder = services.AddLycia(Configuration());

        var persistence = builder.UsePersistence().WithRedisSagaStore();

        Assert.IsType<LyciaPersistenceBuilder>(persistence);
        Assert.Single(services, sd => sd.ServiceType == typeof(ISagaStore));
    }

    // 9: legacy compatibility wrappers still work when kept.
    [Fact]
    public void Legacy_AddLyciaScheduling_And_AddLyciaInMemoryScheduling_Still_Work()
    {
        var services = new ServiceCollection();
        services.AddLycia(Configuration());

#pragma warning disable CS0618
        services.AddLyciaScheduling();
        services.AddLyciaInMemoryScheduling();
#pragma warning restore CS0618

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IOptions<SchedulingOptions>>());
    }

    private sealed class DslProbeSagaHandler : StartReactiveSagaHandler<DslProbeEvent>
    {
        public override Task HandleStartAsync(DslProbeEvent message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class DslProbeEvent : EventBase;

    private sealed class DslProbeLoggingMiddleware : ISagaMiddleware, ILoggingSagaMiddleware
    {
        public Task InvokeAsync(IInvocationContext context, Func<Task> next) => next();
    }
}
