using Lycia.Messaging;
using Lycia.Messaging.Enums;

namespace Lycia.Saga.Abstractions;

public interface ISagaStore
{
    /// <summary>
    /// Logs a saga step's execution status along with optional payload data.
    /// The step is uniquely identified by its type and the handler type.
    /// </summary>
    Task LogStepAsync(Guid sagaId, Guid messageId, Guid? parentMessageId, Type stepType, StepStatus status, Type handlerType, object? payload);

    /// <summary>
    /// Checks whether a specific step in a saga has already been completed.
    /// The step is uniquely identified by its type and the handler type.
    /// Useful for enforcing idempotency in distributed workflows.
    /// </summary>
    Task<bool> IsStepCompletedAsync(Guid sagaId, Guid messageId, Type stepType, Type handlerType);

    /// <summary>
    /// Gets the status of a specific step in the saga.
    /// The step is uniquely identified by its type and the handler type.
    /// </summary>
    /// <param name="sagaId"></param>
    /// <param name="messageId"></param>
    /// <param name="stepType"></param>
    /// <param name="handlerType"></param>
    /// <returns></returns>
    Task<StepStatus> GetStepStatusAsync(Guid sagaId, Guid messageId, Type stepType, Type handlerType);


    /// <summary>
    /// Gets the metadata for a specific step-handler pair in the saga.
    /// </summary>
    /// <param name="sagaId"></param>
    /// <param name="messageId"></param>
    /// <returns></returns>
    Task<KeyValuePair<(string stepType, string handlerType, string messageId), SagaStepMetadata>?>
        GetSagaHandlerStepAsync(Guid sagaId, Guid messageId);
    
    /// <summary>
    /// Retrieves all logged step-handler pairs and their statuses for the given saga.
    /// </summary>
    Task<IReadOnlyDictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata>> GetSagaHandlerStepsAsync(Guid sagaId);

    /// <summary>
    /// Loads the persisted saga data for a given saga instance.
    /// </summary>
    Task<SagaData?> LoadSagaDataAsync(Guid sagaId);

    /// <summary>
    /// Saves the saga's state data to persistent storage.
    /// </summary>
    Task SaveSagaDataAsync(Guid sagaId, SagaData data);
    /// <summary>
    /// Loads the full saga context (including metadata and tracking state) for the given saga identifier.
    /// </summary>
    Task<ISagaContext<TMessage, TSagaData>> LoadContextAsync<TMessage, TSagaData>(Guid sagaId, TMessage message, Type handlerType) 
        where TSagaData : SagaData, new() 
        where TMessage : IMessage;
}