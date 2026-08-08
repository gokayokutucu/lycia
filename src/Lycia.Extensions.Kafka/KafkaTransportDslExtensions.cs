// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions;

namespace Lycia.Extensions.Kafka;

/// <summary>
/// Contributes the Kafka provider to <see cref="LyciaTransportBuilder"/>. Lycia.Extensions defines the
/// builder; this package only adds a provider method to it, so Lycia.Extensions never depends on
/// Lycia.Extensions.Kafka.
/// </summary>
public static class KafkaTransportDslExtensions
{
    /// <summary>Selects Kafka as the transport, with code-first options.</summary>
    public static LyciaTransportBuilder Kafka(
        this LyciaTransportBuilder transport,
        Action<KafkaEventBusOptions>? configure = null)
    {
        if (transport == null) throw new ArgumentNullException(nameof(transport));

        transport.SelectProvider("Kafka");
        KafkaRegistrationExtensions.RegisterKafka(transport.Services, configure ?? (_ => { }));
        return transport;
    }
}
