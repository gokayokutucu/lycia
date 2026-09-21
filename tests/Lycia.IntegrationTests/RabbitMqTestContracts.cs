// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// Shared (linked) by the .NET and .NET Framework integration test projects.
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Messaging;

namespace Lycia.IntegrationTests;

/// <summary>Message contracts for the publisher-confirm tests. Each unrouted scenario uses its own exchange.</summary>
public interface IProbeServiceCommand : ICommandEndpoint;

public interface IOrphanServiceCommand : ICommandEndpoint;

public interface IRejectingServiceCommand : ICommandEndpoint;

public sealed class ProbeEvent : EventBase
{
    public string Payload { get; set; } = string.Empty;
}

public sealed class ProbeCommand : CommandBase, IProbeServiceCommand
{
    public string Payload { get; set; } = string.Empty;
}

public sealed class ProbeResponse : ResponseBase<ProbeCommand>;

public sealed class OrphanCommand : CommandBase, IOrphanServiceCommand;

public sealed class RejectingCommand : CommandBase, IRejectingServiceCommand;
