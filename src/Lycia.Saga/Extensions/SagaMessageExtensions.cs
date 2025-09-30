// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga.Extensions;

public static class SagaMessageExtensions
{
    public static void SetSagaId(this IMessage message, Guid sagaId)
    {
        var prop = message.GetType().GetProperty("SagaId");
        if (prop != null && prop.CanWrite)
            prop.SetValue(message, sagaId);
    }    
    
    public static void SetParentMessageId(this IMessage message, Guid parentMessageId)
    {
        var prop = message.GetType().GetProperty("ParentMessageId");
        if (prop != null && prop.CanWrite)
            prop.SetValue(message, parentMessageId);
    }
}