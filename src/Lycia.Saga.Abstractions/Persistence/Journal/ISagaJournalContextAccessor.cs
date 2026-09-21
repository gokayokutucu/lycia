// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
namespace Lycia.Saga.Abstractions.Persistence.Journal;

/// <summary>
/// Correlation metadata for the message currently being dispatched, made available to the canonical
/// journal append without widening the public <c>ISagaStore</c> contract. Populated by
/// <c>SagaDispatcher</c> before invoking a handler and cleared afterward — framework-managed, never
/// set by saga handler code.
/// </summary>
public sealed class SagaJournalTransitionContext
{
    public Guid? MessageId { get; set; }
    public Guid? RequestId { get; set; }
    public Guid? CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public Guid? ParentMessageId { get; set; }
    public string? ApplicationId { get; set; }
    public string? HandlerType { get; set; }
    public string? MessageType { get; set; }
}

/// <summary>
/// Holds the current dispatch's <see cref="SagaJournalTransitionContext"/> for the current
/// dependency-injection scope. Scoped state, not ambient static or <c>AsyncLocal</c> storage — mirrors
/// <c>ILyciaPersistenceSessionAccessor</c>.
/// </summary>
public interface ISagaJournalContextAccessor
{
    SagaJournalTransitionContext? Current { get; set; }
}

/// <summary>Default scoped implementation of <see cref="ISagaJournalContextAccessor"/>.</summary>
public sealed class SagaJournalContextAccessor : ISagaJournalContextAccessor
{
    public SagaJournalTransitionContext? Current { get; set; }
}
