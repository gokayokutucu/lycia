// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Helpers;
using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Scheduling;
using Lycia.Saga.Abstractions.Serializers;
using Lycia.Saga.Extensions;
using Lycia.Saga.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lycia.Scheduling;

/// <summary>Creates durable, idempotent schedule records and selects validated native transport strategies.</summary>
public sealed class MessageScheduler(
    IScheduleStore store,
    IMessageSerializer serializer,
    IEventBus eventBus,
    ISchedulingClock clock,
    IOptions<SchedulingOptions> options,
    ISchedulingResourceRegistry resourceRegistry,
    ILogger<MessageScheduler> logger) : IMessageScheduler
{
    /// <inheritdoc />
    public Task<Guid> ScheduleAsync(IMessage message, IMessage currentMessage, Type handlerType, Guid sagaId,
        ScheduleDelay delay, Guid? scheduleId = null, CancellationToken cancellationToken = default)
    {
        var duration = ScheduleDelayResolver.GetDuration(delay);
        return CreateAsync(message, currentMessage, handlerType, sagaId, clock.UtcNow.Add(duration), duration,
            true, ScheduleDelayResolver.GetSuffix(delay), "delay:" + ScheduleDelayResolver.GetSuffix(delay), scheduleId,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Guid> ScheduleAsync(IMessage message, IMessage currentMessage, Type handlerType, Guid sagaId,
        TimeSpan delay, Guid? scheduleId = null, CancellationToken cancellationToken = default)
    {
        ValidateDelay(delay);
        return CreateAsync(message, currentMessage, handlerType, sagaId, clock.UtcNow.Add(delay), delay,
            false, GetDynamicSuffix(delay), "delay:" + GetDynamicSuffix(delay), scheduleId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Guid> ScheduleAtAsync(IMessage message, IMessage currentMessage, Type handlerType, Guid sagaId,
        DateTimeOffset dueAtUtc, Guid? scheduleId = null, CancellationToken cancellationToken = default)
    {
        var normalized = dueAtUtc.ToUniversalTime();
        var delay = normalized - clock.UtcNow;
        ValidateDelay(delay);
        return CreateAsync(message, currentMessage, handlerType, sagaId, normalized, delay,
            false, GetDynamicSuffix(delay), "at:" + normalized.ToUnixTimeMilliseconds(), scheduleId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CancelAsync(Guid scheduleId, CancellationToken cancellationToken = default) =>
        store.CancelAsync(RequireScheduleId(scheduleId), cancellationToken);

    /// <inheritdoc />
    public Task<bool> RescheduleAsync(Guid scheduleId, DateTimeOffset dueAtUtc,
        CancellationToken cancellationToken = default)
    {
        var normalized = dueAtUtc.ToUniversalTime();
        ValidateDelay(normalized - clock.UtcNow);
        return store.RescheduleAsync(RequireScheduleId(scheduleId), normalized, cancellationToken);
    }

    private async Task<Guid> CreateAsync(IMessage message, IMessage currentMessage, Type handlerType, Guid sagaId,
        DateTimeOffset dueAtUtc, TimeSpan delay, bool isPredefined, string suffix, string idempotencyKey,
        Guid? requestedScheduleId,
        CancellationToken cancellationToken)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        if (currentMessage == null) throw new ArgumentNullException(nameof(currentMessage));
        if (handlerType == null) throw new ArgumentNullException(nameof(handlerType));
        ValidateDelay(delay);
        var scheduleId = requestedScheduleId.HasValue ? RequireScheduleId(requestedScheduleId.Value) : GuidV7.NewGuidV7();
        var kind = PrepareMessage(message, currentMessage, sagaId, eventBus.ApplicationId);
        using var activity = SchedulingMetrics.ActivitySource.StartActivity("lycia.schedule");
        activity?.SetTag("lycia.schedule_id", scheduleId);
        activity?.SetTag("lycia.message_id", message.MessageId);
        activity?.SetTag("lycia.correlation_id", message.CorrelationId);
        activity?.SetTag("lycia.causation_id", message.CausationId);
        activity?.SetTag("lycia.parent_message_id", message.ParentMessageId);
        activity?.SetTag("lycia.saga_id", sagaId);
        activity?.SetTag("lycia.due_at", dueAtUtc);
        activity?.SetTag("lycia.scheduling_transport", eventBus.GetType().Name);
        var record = CreateRecord(scheduleId, message, currentMessage, kind, dueAtUtc, isPredefined, suffix,
            idempotencyKey);
        var creation = await store.CreateAsync(record, cancellationToken).ConfigureAwait(false);
        SchedulingMetrics.Requests.Add(1,
            new KeyValuePair<string, object?>("message.kind", kind.ToString()),
            new KeyValuePair<string, object?>("schedule.created", creation.Created));
        logger.LogInformation(
            "Accepted schedule {ScheduleId} for message {MessageId} kind {MessageKind} request {RequestId} correlation {CorrelationId} causation {CausationId} parent {ParentMessageId} saga {SagaId} due {DueAtUtc}",
            scheduleId, message.MessageId, kind, message is IRequestRoutingMetadata routing ? routing.RequestId : null,
            message.CorrelationId, message.CausationId, message.ParentMessageId, sagaId, dueAtUtc);
        if (!creation.Created) return creation.ScheduleId;

        var envelope = new NativeScheduleEnvelope { Record = record, Delay = delay };
        var native = await SelectNativeTransportAsync(envelope, isPredefined, cancellationToken).ConfigureAwait(false);
        if (native == null) return scheduleId;

        var resourceId = await native.ScheduleNativeAsync(envelope, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            await resourceRegistry.UpsertAsync(CreateResourceRecord(record, resourceId!, delay, native.TransportName),
                cancellationToken).ConfigureAwait(false);
            SchedulingMetrics.ResourcesCreated.Add(1,
                new KeyValuePair<string, object?>("transport", native.TransportName),
                new KeyValuePair<string, object?>("resource.class", record.IsPredefined ? "predefined" : "dynamic"));
        }
        var completed = await store.CompleteNativeAsync(scheduleId, resourceId, record.Strategy, clock.UtcNow,
            cancellationToken).ConfigureAwait(false);
        if (!completed)
            throw new InvalidOperationException($"Native scheduling state for ScheduleId '{scheduleId}' changed before acceptance could be recorded.");
        logger.LogInformation(
            "Accepted native schedule {ScheduleId} for message {MessageId} via {Transport} resource {ResourceId} due at {DueAtUtc}",
            scheduleId, message.MessageId, native.TransportName, resourceId, dueAtUtc);
        return scheduleId;
    }

    private SchedulingResourceRecord CreateResourceRecord(ScheduleRecord record, string resourceId, TimeSpan delay,
        string transport) => new()
    {
        ResourceId = resourceId,
        Transport = transport,
        ResourceType = transport == "rabbitmq" ? "queue" : "scheduling-resource",
        CanonicalName = resourceId,
        CanonicalApplicationKey = EndpointIdentityNormalizer.Default.Normalize(eventBus.ApplicationId),
        MessageType = record.MessageType,
        MessageKind = record.MessageKind,
        Destination = record.Destination,
        Delay = delay,
        DelaySuffix = record.DelaySuffix,
        IsPredefined = record.IsPredefined,
        IsDynamic = !record.IsPredefined,
        ManagementMode = record.IsPredefined
            ? SchedulingResourceManagementMode.LyciaManaged
            : SchedulingResourceManagementMode.DynamicScheduling,
        Lifecycle = SchedulingResourceLifecycle.Active,
        CreatedAtUtc = clock.UtcNow,
        LastDeclaredAtUtc = clock.UtcNow,
        LastUsedAtUtc = clock.UtcNow,
        LastPublishAtUtc = clock.UtcNow,
        FrameworkVersion = typeof(MessageScheduler).Assembly.GetName().Version?.ToString() ?? "unknown"
    };

    private ScheduleRecord CreateRecord(Guid scheduleId, IMessage message, IMessage currentMessage,
        ScheduledMessageKind kind, DateTimeOffset dueAtUtc, bool isPredefined, string suffix, string idempotencyKey)
    {
        var type = message.GetType();
        var (_, context) = serializer.CreateContextFor(type);
        var serialized = serializer.Serialize(message, context);
        byte[]? requestPayload = null;
        Dictionary<string, object?>? requestHeaders = null;
        string? requestType = null;
        if (kind == ScheduledMessageKind.Response)
        {
            var currentType = currentMessage.GetType();
            var (_, requestContext) = serializer.CreateContextFor(currentType);
            var request = serializer.Serialize(currentMessage, requestContext);
            requestPayload = request.Body;
            requestHeaders = CopyHeaders(request.Headers);
            requestType = currentType.AssemblyQualifiedName;
        }

        return new ScheduleRecord
        {
            ScheduleId = scheduleId,
            MessageId = message.MessageId,
            RequestId = message is IRequestRoutingMetadata routing ? routing.RequestId : null,
            CorrelationId = message.CorrelationId,
            CausationId = message.CausationId,
            ParentMessageId = message.ParentMessageId,
            SagaId = message.SagaId,
            ResponseEndpoint = message is IRequestRoutingMetadata endpoint ? endpoint.ResponseEndpoint : null,
            MessageType = type.AssemblyQualifiedName
                          ?? throw new InvalidOperationException($"Message type '{type.FullName}' has no assembly-qualified name."),
            MessageKind = kind,
            Destination = ResolveDestination(message, kind),
            DueAtUtc = dueAtUtc.ToUniversalTime(),
            ScheduledAtUtc = clock.UtcNow,
            Status = ScheduleStatus.Pending,
            Transport = eventBus.GetType().Name,
            Strategy = SchedulingStrategy.DurableWorker,
            Payload = serialized.Body,
            Headers = CopyHeaders(serialized.Headers),
            RequestPayload = requestPayload,
            RequestType = requestType,
            RequestHeaders = requestHeaders,
            IsPredefined = isPredefined,
            DelaySuffix = suffix,
            IdempotencyKey = idempotencyKey
        };
    }

    private async Task<INativeSchedulingTransport?> SelectNativeTransportAsync(NativeScheduleEnvelope envelope,
        bool isPredefined, CancellationToken cancellationToken)
    {
        if (!options.Value.PreferNativeTransportScheduling) return null;
        if (isPredefined && !options.Value.PredefinedDelays.Any(delay =>
                string.Equals(ScheduleDelayResolver.GetSuffix(delay), envelope.Record.DelaySuffix,
                    StringComparison.Ordinal))) return null;
        if (!isPredefined && !options.Value.AllowDynamicDelays) return null;
        if (eventBus is INativeSchedulingTransport transport &&
            await transport.CanScheduleAsync(envelope, cancellationToken).ConfigureAwait(false))
            return transport;
        return null;
    }

    private ScheduledMessageKind PrepareMessage(IMessage message, IMessage current, Guid sagaId,
        string applicationId)
    {
        if (message is IResponse response)
        {
            if (!ImplementsResponseFor(message.GetType(), current.GetType()))
                throw new InvalidOperationException(
                    $"Response '{message.GetType().FullName}' does not target current request '{current.GetType().FullName}'.");
            if (message.MessageId == Guid.Empty || message.MessageId == current.MessageId)
                message.MessageId = GuidV7.NewGuidV7();
            response.RequestId = current.MessageId;
            response.ResponseEndpoint = RequestRouting.RequireResponseEndpoint(current, response);
            PropagateWorkflow(message, current, sagaId);
            return ScheduledMessageKind.Response;
        }
        if (message is ICommand command)
        {
            command.PrepareCommand(current, sagaId, applicationId);
            return ScheduledMessageKind.Command;
        }
        if (message is IEvent @event)
        {
            @event.PrepareEvent(current, sagaId);
            return ScheduledMessageKind.Event;
        }
        throw new InvalidOperationException(
            $"Message '{message.GetType().FullName}' must be a command, event, or targeted response to be scheduled.");
    }

    private static bool ImplementsResponseFor(Type responseType, Type requestType) => responseType.GetInterfaces().Any(type =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IResponse<>) &&
        type.GetGenericArguments()[0].IsAssignableFrom(requestType));

    private static void PropagateWorkflow(IMessage outgoing, IMessage current, Guid sagaId)
    {
        outgoing.CorrelationId = current.CorrelationId == Guid.Empty ? current.MessageId : current.CorrelationId;
        outgoing.CausationId = current.MessageId;
        outgoing.ParentMessageId = current.MessageId;
        outgoing.SagaId = sagaId;
    }

    private string ResolveDestination(IMessage message, ScheduledMessageKind kind)
    {
        switch (kind)
        {
            case ScheduledMessageKind.Command:
                return EndpointIdentityNormalizer.Default.Normalize(CommandEndpointResolver.Default.Resolve(message.GetType()));
            case ScheduledMessageKind.Event:
                return EndpointIdentityNormalizer.Default.Normalize(eventBus.ApplicationId);
            case ScheduledMessageKind.Response:
                return EndpointIdentityNormalizer.Default.Normalize(((IRequestRoutingMetadata)message).ResponseEndpoint!);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown scheduled message kind.");
        }
    }

    private void ValidateDelay(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay), "Scheduling delay must be positive.");
        if (delay > options.Value.MaximumDelay)
            throw new ArgumentOutOfRangeException(nameof(delay),
                $"Scheduling delay exceeds the configured maximum of {options.Value.MaximumDelay}.");
    }

    private static Guid RequireScheduleId(Guid scheduleId)
    {
        if (scheduleId == Guid.Empty) throw new ArgumentException("ScheduleId cannot be empty.", nameof(scheduleId));
        return scheduleId;
    }

    private static Dictionary<string, object?> CopyHeaders(IReadOnlyDictionary<string, object?> headers)
    {
        var copy = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in headers) copy[pair.Key] = pair.Value;
        return copy;
    }

    private static string GetDynamicSuffix(TimeSpan delay) =>
        $"{checked((long)Math.Ceiling(delay.TotalMilliseconds))}ms";
}
