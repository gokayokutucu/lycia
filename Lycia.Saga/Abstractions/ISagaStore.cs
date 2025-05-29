using Lycia.Messaging;
using Lycia.Messaging.Enums;

namespace Lycia.Saga.Abstractions;

public interface ISagaStore
{
    /// <summary>
    /// Logs a saga step's execution status along with optional payload data.
    /// </summary>
    Task LogStepAsync(Guid sagaId, Type stepType, StepStatus status, object? payload = null);

    /// <summary>
    /// Checks whether a specific step in a saga has already been completed.
    /// Useful for enforcing idempotency in distributed workflows.
    /// </summary>
    Task<bool> IsStepCompletedAsync(Guid sagaId, Type stepType);
    
    /// <summary>
    /// Gets the status of a specific step in the saga.
    /// </summary>
    /// <param name="sagaId"></param>
    /// <param name="stepType"></param>
    /// <returns></returns>
    Task<StepStatus> GetStepStatusAsync(Guid sagaId, Type stepType);

    /// <summary>
    /// Retrieves all logged steps and their statuses for the given saga.
    /// </summary>
    Task<IReadOnlyDictionary<string, SagaStepMetadata>> GetSagaStepsAsync(Guid sagaId);

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
    Task<ISagaContext<TMessage, TSagaData>> LoadContextAsync<TMessage, TSagaData>(Guid sagaId) 
        where TSagaData : SagaData, new() 
        where TMessage : IMessage;
}