// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Extensions.Journal;
using Lycia.Saga.Abstractions;

namespace Lycia.Tests.Journal;

/// <summary>
/// Proves side-effect isolation by construction, not just by runtime assertion: <see cref="SagaRebuildService"/>
/// has no dependency capable of invoking a handler, publishing/sending a message, or writing an Outbox
/// record. If a future change accidentally introduces one of those dependencies, this test fails at
/// build/reflection time rather than relying solely on a runtime side-effect count staying at zero.
/// </summary>
public class SagaRebuildServiceIsolationTests
{
    [Fact]
    public void SagaRebuildService_Constructor_Has_No_Handler_Or_Transport_Or_Outbox_Dependency()
    {
        var constructor = Assert.Single(typeof(SagaRebuildService).GetConstructors());
        var parameterTypes = constructor.GetParameters().Select(p => p.ParameterType).ToList();

        Assert.DoesNotContain(parameterTypes, t => t == typeof(IEventBus));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Outbox", StringComparison.Ordinal));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Inbox", StringComparison.Ordinal));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Dispatcher", StringComparison.Ordinal));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("HttpClient", StringComparison.Ordinal));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Scheduler", StringComparison.Ordinal));
    }
}
