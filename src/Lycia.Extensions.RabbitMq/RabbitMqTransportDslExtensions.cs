// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions.Configurations;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Extensions.RabbitMq;

/// <summary>
/// Contributes the RabbitMQ provider to <see cref="LyciaTransportBuilder"/>. Lycia.Extensions defines the
/// builder; this package only adds a provider method to it, so Lycia.Extensions never depends on
/// Lycia.Extensions.RabbitMq.
/// </summary>
public static class RabbitMqTransportDslExtensions
{
    /// <summary>
    /// Selects RabbitMQ as the transport. Connection settings are read from <c>Lycia:EventBus</c>
    /// (<see cref="EventBusOptions"/>) unless overridden by <paramref name="configure"/>.
    /// </summary>
    public static LyciaTransportBuilder RabbitMq(
        this LyciaTransportBuilder transport,
        Action<EventBusOptions>? configure = null)
    {
        if (transport == null) throw new ArgumentNullException(nameof(transport));

        transport.SelectProvider("RabbitMq");
        if (configure != null) transport.Services.Configure(configure);
        RabbitMqRegistrationExtensions.RegisterRabbitMq(transport.Services);
        return transport;
    }
}
