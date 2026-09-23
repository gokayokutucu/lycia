// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Reflection;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Compensating;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Compensating;
using Lycia.Saga.Contexts;
using Lycia.Tests.Messages;
using Moq;

namespace Lycia.Tests;

/// <summary>
/// Proves, by inspecting the actual compiled public API surface (not by convention or code review), the
/// staged-fluent-grammar invariant: an intermediate fluent operation must not be independently callable
/// application code. Where the grammar can be made a compile-time impossibility (a method simply does not
/// exist on the public interface an application ever holds), these tests assert that absence via
/// reflection. Where a runtime check is the only reasonable enforcement (a custom <see cref="ISagaContext{T}"/>
/// implementation that does not support bubble-up), these tests assert that check's behavior instead.
/// </summary>
public class FluentApiEncapsulationTests
{
    // --- Compensation grammar: ContinueCompensation() -> ICompensationContinuation ->
    //     ThenMarkAsCompensated<T>() -> ICompensatedContinuation -> ThenBubbleUp(ct) ---

    [Fact]
    public void ISagaContext_Does_Not_Expose_ThenBubbleUp()
    {
        var members = typeof(ISagaContext<>).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name);
        Assert.DoesNotContain("ThenBubbleUp", members);
    }

    [Fact]
    public void ISagaContext_Does_Not_Expose_BubbleUpCompensationAsync()
    {
        // This is the primitive ContinueCompensation()...ThenBubbleUp(ct) uses internally. It must never be
        // directly callable on the context - Context.BubbleUpCompensationAsync<T>(ct) must not compile.
        var members = typeof(ISagaContext<>).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name);
        Assert.DoesNotContain("BubbleUpCompensationAsync", members);
    }

    [Fact]
    public void ISagaContext_Generic_With_SagaData_Does_Not_Expose_BubbleUpCompensationAsync_Either()
    {
        var members = typeof(ISagaContext<,>).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name);
        Assert.DoesNotContain("BubbleUpCompensationAsync", members);
        Assert.DoesNotContain("ThenBubbleUp", members);
    }

    [Fact]
    public void ICompensationContinuation_Does_Not_Expose_ThenBubbleUp()
    {
        // ContinueCompensation().ThenBubbleUp(ct) must not compile - ThenBubbleUp only exists on the
        // continuation returned by the no-token ThenMarkAsCompensated<T>() call.
        var members = typeof(ICompensationContinuation).GetMethods().Select(m => m.Name);
        Assert.DoesNotContain("ThenBubbleUp", members);
    }

    [Fact]
    public void ICompensatedContinuation_Exposes_Exactly_ThenBubbleUp()
    {
        var members = typeof(ICompensatedContinuation).GetMethods().Select(m => m.Name).ToList();
        Assert.Contains("ThenBubbleUp", members);
        Assert.Single(members); // no other member could serve as an alternate, undocumented escape hatch
    }

    [Fact]
    public void ContinueCompensation_Returns_ICompensationContinuation()
    {
        var method = typeof(ISagaContext<>).GetMethod(nameof(ISagaContext<Lycia.Saga.Abstractions.Messaging.IMessage>.ContinueCompensation));
        Assert.NotNull(method);
        Assert.Equal(typeof(ICompensationContinuation), method!.ReturnType);
    }

    [Fact]
    public void NoToken_ThenMarkAsCompensated_Returns_ICompensatedContinuation()
    {
        // This is the type-system mechanism that makes ThenBubbleUp reachable only after this specific
        // call: only this overload returns a type exposing ThenBubbleUp at all.
        var method = typeof(ICompensationContinuation).GetMethods()
            .Single(m => m.Name == nameof(ICompensationContinuation.ThenMarkAsCompensated) && m.GetParameters().Length == 0);
        Assert.Equal(typeof(ICompensatedContinuation), method.ReturnType);
    }

    [Fact]
    public void TwoStage_ThenMarkAsCompensated_With_Token_Returns_Task_Not_A_Continuation()
    {
        // The two-stage terminal overload must return Task (it is terminal, no further staging) - it must
        // not itself return something exposing ThenBubbleUp.
        var method = typeof(ICompensationContinuation).GetMethods()
            .Single(m => m.Name == nameof(ICompensationContinuation.ThenMarkAsCompensated) && m.GetParameters().Length == 1);
        Assert.Equal(typeof(Task), method.ReturnType);
    }

    [Fact]
    public void The_BubbleUp_Primitive_Type_Is_Not_Public()
    {
        // Found by name via reflection rather than a static reference, so this test does not itself need
        // InternalsVisibleTo to prove the type is inaccessible to an ordinary external assembly.
        var primitiveType = typeof(ICompensatedContinuation).Assembly.GetTypes()
            .SingleOrDefault(t => t.Name == "IBubbleUpCompensationPrimitive");
        Assert.NotNull(primitiveType);
        Assert.False(primitiveType!.IsPublic, "The bubble-up execution primitive must not be a public type.");
    }

    [Fact]
    public void Concrete_SagaContext_Does_Not_Publicly_Expose_BubbleUpCompensationAsync()
    {
        // SagaContext<T> is a public class (constructed directly by some tests/samples), so this is the
        // strongest possible proof: even holding the concrete type, not just the interface, application
        // code cannot call BubbleUpCompensationAsync - it is an explicit interface implementation of an
        // internal interface, which GetMethods() (public members only, by default) never returns.
        var publicMethodNames = typeof(SagaContext<DummyEvent>).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name);
        Assert.DoesNotContain("BubbleUpCompensationAsync", publicMethodNames);
    }

    [Fact]
    public async Task ThenMarkAsCompensated_NoToken_On_A_Custom_ISagaContext_Without_The_Primitive_Fails_Clearly()
    {
        // A hand-written ISagaContext<T> implementation (not one of Lycia's own context types) cannot
        // implement the internal IBubbleUpCompensationPrimitive from outside Lycia.Saga - this is the one
        // place that invariant is enforced at runtime instead of compile time, because "does this custom
        // context support bubble-up" cannot be expressed in the public type system without exposing the
        // primitive itself. The failure must be a clear, diagnosable exception, not a bare InvalidCastException.
        var mockContext = new Mock<ISagaContext<DummyEvent>>();
        var continuation = new SagaCompensationContinuation<DummyEvent>(mockContext.Object);

        var ex = Assert.Throws<InvalidOperationException>(() => continuation.ThenMarkAsCompensated<DummyEvent>());
        Assert.Contains("IBubbleUpCompensationPrimitive", ex.Message);
        await Task.CompletedTask;
    }

    // --- WithTracking grammar: entry methods only defer; terminal Then* methods execute exactly once ---

    [Fact]
    public void ISagaStepFluent_Exposes_Only_Terminal_Then_Methods_All_Returning_Task()
    {
        // Every member must be a valid terminal transition (returns Task) - none may return another
        // fluent/continuation type that would let application code chain past a single terminal call, and
        // there must be no generic Execute()-style escape hatch.
        var methods = typeof(ISagaStepFluent).GetMethods();
        Assert.All(methods, m => Assert.StartsWith("Then", m.Name));
        Assert.All(methods, m => Assert.Equal(typeof(Task), m.ReturnType));
        Assert.DoesNotContain(methods, m => m.Name.Equals("Execute", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ISagaContext_WithTracking_Entry_Methods_Return_Only_ISagaStepFluent()
    {
        var trackingMethodNames = new[] { "SendWithTracking", "PublishWithTracking", "RespondWithTracking" };
        var methods = typeof(ISagaContext<>).GetMethods()
            .Where(m => trackingMethodNames.Contains(m.Name));
        Assert.NotEmpty(methods);
        Assert.All(methods, m => Assert.Equal(typeof(ISagaStepFluent), m.ReturnType));
    }

    [Fact]
    public void SchedulingSagaContext_ScheduleWithTracking_Returns_Only_ISagaStepFluent()
    {
        var method = typeof(Lycia.Saga.Abstractions.Scheduling.ISchedulingSagaContext)
            .GetMethods().Single(m => m.Name == "ScheduleWithTracking");
        Assert.Equal(typeof(ISagaStepFluent), method.ReturnType);
    }

    [Fact]
    public void CoordinatedSagaStepFluent_Does_Not_Publicly_Expose_RunAsync_Or_Any_Execute_Primitive()
    {
        // RunAsync (the deferred operation+transition runner) must stay a private implementation detail -
        // proves there is no way to invoke the deferred operation without going through a Then* terminal.
        var publicMethodNames = typeof(CoordinatedSagaStepFluent<,>).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .Distinct()
            .ToList();
        Assert.DoesNotContain("RunAsync", publicMethodNames);
        Assert.DoesNotContain(publicMethodNames, n => n.Equals("Execute", StringComparison.OrdinalIgnoreCase));
        Assert.All(publicMethodNames.Where(n => n != "Create" && n != "Equals" && n != "GetHashCode"
            && n != "GetType" && n != "ToString"), n => Assert.StartsWith("Then", n));
    }

    [Fact]
    public void ReactiveSagaStepFluent_Does_Not_Publicly_Expose_RunAsync_Or_Any_Execute_Primitive()
    {
        var publicMethodNames = typeof(ReactiveSagaStepFluent<>).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .Distinct()
            .ToList();
        Assert.DoesNotContain("RunAsync", publicMethodNames);
        Assert.DoesNotContain(publicMethodNames, n => n.Equals("Execute", StringComparison.OrdinalIgnoreCase));
        Assert.All(publicMethodNames.Where(n => n != "Create" && n != "Equals" && n != "GetHashCode"
            && n != "GetType" && n != "ToString"), n => Assert.StartsWith("Then", n));
    }
}
