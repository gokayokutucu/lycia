// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Persistence;
using Lycia.Saga.Abstractions.Scheduling;
using Lycia.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lycia.Persistence.InMemory;

/// <summary>Registers <see cref="InMemorySagaStore"/> as the active <see cref="ISagaStore"/>.</summary>
internal static class InMemorySagaStoreRegistrationExtensions
{
    internal static void RegisterInMemorySagaStore(IServiceCollection services)
    {
        // RemoveAll (not TryAdd) so an explicit InMemory selection always wins over any legacy default registration.
        services.RemoveAll(typeof(ISagaStore));
        services.AddScoped<ISagaStore>(sp => new InMemorySagaStore(
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<ISagaIdGenerator>(),
            sp.GetRequiredService<ISagaCompensationCoordinator>(),
            sp.GetService<IMessageScheduler>()));

        // InMemory cannot join a real cross-store transaction; register the non-atomic default so
        // ILyciaPersistenceSessionFactory is always resolvable regardless of the selected provider.
        services.TryAddSingleton<ILyciaPersistenceSessionFactory, NonAtomicPersistenceSessionFactory>();
    }
}
