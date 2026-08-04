// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Messaging;

namespace Sample.Shared.Messages.Commands;

/// <summary>Logical owner for commands handled by the orchestration sample.</summary>
public interface ISampleOrderConsumerCommand : ICommand, ICommandEndpoint { }

/// <summary>Logical stock-service endpoint used by the strongly typed ownership example.</summary>
public interface IStockServiceCommand : ICommand, ICommandEndpoint { }
