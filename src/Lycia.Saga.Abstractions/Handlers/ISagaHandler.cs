// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Common.Configurations;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Messaging;
using Microsoft.Extensions.Options;

namespace Lycia.Saga.Abstractions.Handlers;

public interface ISagaHandler<TMessage> 
    where TMessage : IMessage
{
    void Initialize(ISagaContext<IMessage> context, IOptions<SagaOptions> sagaOptions);
}

public interface ISagaHandler<TMessage, TSagaData> 
    where TMessage: IMessage
    where TSagaData : SagaData
{
    void Initialize(ISagaContext<IMessage, TSagaData> context, IOptions<SagaOptions> sagaOptions);
}