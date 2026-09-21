// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Abstractions.Outbox;

/// <summary>
/// An <see cref="IConfirmedEventBus"/> whose ability to confirm broker acceptance depends on configuration:
/// NATS confirms only through JetStream, and RabbitMQ only while publisher confirms are enabled.
/// </summary>
/// <remarks>
/// The Outbox dispatcher normally selects the confirmed path by interface alone. A transport that
/// implements this interface and reports <see cref="ConfirmationsAvailable"/> as <c>false</c> is treated as
/// an unconfirming transport instead: its plain <c>Send</c>/<c>Publish</c>/<c>Respond</c> are used and a
/// completed attempt is recorded as <see cref="OutboxMessageStatus.ConfirmationUnknown"/>, never as
/// <see cref="OutboxMessageStatus.Published"/>.
/// </remarks>
public interface IConditionalConfirmedEventBus : IConfirmedEventBus
{
    /// <summary>
    /// Gets whether the <c>*Confirmed</c> methods positively confirm broker acceptance in the transport's
    /// current configuration.
    /// </summary>
    bool ConfirmationsAvailable { get; }
}
