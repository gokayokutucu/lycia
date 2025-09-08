// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Messaging.Utility;

namespace Lycia.Messaging;

public abstract class ResponseBase<TPrevious> :
    ISuccessResponse<TPrevious>,
    IFailResponse<TPrevious>,
    IEvent
    where TPrevious : IMessage
{
    protected ResponseBase(Guid? parentMessageId = null, Guid? correlationId = null)
    {
        MessageId = GuidV7.NewGuidV7();
        ParentMessageId = parentMessageId ?? Guid.Empty;
        CorrelationId = correlationId ?? MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId  = EventMetadata.ApplicationId;
    }
    
    public Guid MessageId { get; set; }
    public Guid ParentMessageId { get; set; }
    public DateTime Timestamp { get; set; } 
    public string ApplicationId { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid? SagaId { get; set; }
}