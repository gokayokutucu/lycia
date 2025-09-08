// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Messaging.Extensions;

public static class MessageExtensions
{
    public static TOut Next<TIn, TOut>(this TIn previous)
        where TIn : IMessage
        where TOut : IMessage, new()
    {
        var next = new TOut
        {
            CorrelationId = previous.CorrelationId,
        };
        return next;
    }
}