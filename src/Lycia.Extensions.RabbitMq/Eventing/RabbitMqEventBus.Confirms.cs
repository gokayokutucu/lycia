// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Outbox;

namespace Lycia.Extensions.Eventing;

/// <summary>
/// Publisher-confirm behavior that does not depend on the RabbitMQ client generation: the capability the
/// Outbox reads, the confirmed entry points, and the routing policy. The publish itself lives in the
/// client-specific partial files.
/// </summary>
public sealed partial class RabbitMqEventBus : IConditionalConfirmedEventBus
{
    private enum PublishKind
    {
        Command,
        Event,
        Response
    }

    /// <inheritdoc />
    public bool ConfirmationsAvailable => _options.PublisherConfirms;

    /// <inheritdoc />
    public async Task SendConfirmed<TCommand>(TCommand command, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken = default) where TCommand : ICommand
    {
        RequireConfirmations();
        await Send(command, handlerType, sagaId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PublishConfirmed<TEvent>(TEvent message, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        RequireConfirmations();
        await Publish(message, handlerType, sagaId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RespondConfirmed<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType,
        Guid? sagaId, CancellationToken cancellationToken = default)
        where TRequest : IMessage where TResponse : IResponse<TRequest>
    {
        RequireConfirmations();
        await Respond(request, response, handlerType, sagaId, cancellationToken).ConfigureAwait(false);
    }

    private void RequireConfirmations()
    {
        if (!_options.PublisherConfirms)
            throw new InvalidOperationException(
                "RabbitMQ publisher confirms are disabled (EventBusOptions.PublisherConfirms = false), so this " +
                "transport cannot positively confirm a publish.");
    }

    /// <summary>
    /// Whether an unroutable message must be returned rather than dropped. A command or response has exactly
    /// one owner queue, so no route is a failure. An event may legitimately have no subscriber, so it is only
    /// required to route when <see cref="Configurations.EventBusOptions.RequireRoutableEvents"/> says so.
    /// Without publisher confirms the historical fire-and-forget behavior (no mandatory flag) is kept.
    /// </summary>
    private bool RequiresRoute(PublishKind kind) =>
        _options.PublisherConfirms && (kind != PublishKind.Event || _options.RequireRoutableEvents);

    private static void ValidateConfirmOptions(Configurations.EventBusOptions options)
    {
        if (options.PublisherConfirms && options.PublisherConfirmTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("EventBusOptions.PublisherConfirmTimeout must be positive.");
    }
}
