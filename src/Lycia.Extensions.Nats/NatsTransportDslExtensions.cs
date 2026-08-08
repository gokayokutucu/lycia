// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions;

namespace Lycia.Extensions.Nats;

/// <summary>
/// Contributes the NATS provider to <see cref="LyciaTransportBuilder"/>. Lycia.Extensions defines the
/// builder; this package only adds a provider method to it, so Lycia.Extensions never depends on
/// Lycia.Extensions.Nats.
/// </summary>
public static class NatsTransportDslExtensions
{
    /// <summary>Selects NATS (JetStream by default) as the transport, with code-first options.</summary>
    public static LyciaTransportBuilder Nats(
        this LyciaTransportBuilder transport,
        Action<NatsEventBusOptions>? configure = null)
    {
        if (transport == null) throw new ArgumentNullException(nameof(transport));

        transport.SelectProvider("Nats");
        NatsRegistrationExtensions.RegisterNats(transport.Services, configure ?? (_ => { }));
        return transport;
    }
}
