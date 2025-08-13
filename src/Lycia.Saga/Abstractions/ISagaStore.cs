using Lycia.Messaging;
using Lycia.Messaging.Enums;

namespace Lycia.Saga.Abstractions;

public interface ISagaStore
{
    /// <summary>
    /// Logs the execution status of a saga step along with optional payload data and failure details.
    /// This method tracks the state of a specific step performed by a handler within a saga workflow.
    /// </summary>
    /// <param name="sagaId">The unique identifier of the saga instance.</param>
    /// <param name="messageId">The unique identifier of the message triggering the step.</param>
    /// <param name="parentMessageId">The unique identifier of the parent message, if applicable.</param>
    /// <param name="stepType">The type of the step being logged in the saga process.</param>
    /// <param name="status">The current status of the step execution.</param>
    /// <param name="handlerType">The type of the handler managing the saga step.</param>
    /// <param name="payload">Optional payload data associated with the saga step.</param>
    /// <param name="exception">An optional exception providing details in case of a failure.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task LogStepAsync(Guid sagaId, Guid messageId, Guid? parentMessageId, Type stepType, StepStatus status,
        Type handlerType, object? payload, Exception? exception, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs a saga step's execution status along with optional payload data and failure information.
    /// The step is uniquely identified by its type and the handler type.
    /// </summary>
    /// <param name="sagaId">The unique identifier for the saga.</param>
    /// <param name="messageId">The unique identifier for the message.</param>
    /// <param name="parentMessageId">The unique identifier for the parent message, if applicable.</param>
    /// <param name="stepType">The type of the saga step being logged.</param>
    /// <param name="status">The status of the saga step, indicating its current state.</param>
    /// <param name="handlerType">The type of the handler processing the saga step.</param>
    /// <param name="payload">An optional object containing additional payload data for the saga step.</param>
    /// <param name="failureInfo">Optional information about a failure, including the reason and exception details.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task LogStepAsync(Guid sagaId, Guid messageId, Guid? parentMessageId, Type stepType, StepStatus status,
        Type handlerType, object? payload, SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a specific step in a saga has already been completed.
    /// The step is uniquely identified by its type and the handler type.
    /// Useful for enforcing idempotency in distributed workflows.
    /// </summary>
    Task<bool> IsStepCompletedAsync(Guid sagaId, Guid messageId, Type stepType, Type handlerType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a specific step in the saga.
    /// The step is uniquely identified by its type and the handler type.
    /// </summary>
    /// <param name="sagaId"></param>
    /// <param name="messageId"></param>
    /// <param name="stepType"></param>
    /// <param name="handlerType"></param>
    /// <returns></returns>
    Task<StepStatus> GetStepStatusAsync(Guid sagaId, Guid messageId, Type stepType, Type handlerType, CancellationToken cancellationToken = default);


    /// <summary>
    /// Gets the metadata for a specific step-handler pair in the saga.
    /// </summary>
    /// <param name="sagaId"></param>
    /// <param name="messageId"></param>
    /// <returns></returns>
    Task<KeyValuePair<(string stepType, string handlerType, string messageId), SagaStepMetadata>?>
        GetSagaHandlerStepAsync(Guid sagaId, Guid messageId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Retrieves all logged step-handler pairs and their statuses for the given saga.
    /// </summary>
    Task<IReadOnlyDictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata>> GetSagaHandlerStepsAsync(Guid sagaId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a saga step message associated with the given saga identifier and step type.
    /// This method retrieves the message related to a specific step in the saga workflow.
    /// </summary>
    /// <param name="sagaId">The unique identifier of the saga instance.</param>
    /// <param name="stepType">The type of the step associated with the message being loaded.</param>
    /// <returns>The message associated with the specified saga step, or null if not found.</returns>
    Task<IMessage?> LoadSagaStepMessageAsync(Guid sagaId, Type stepType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the message associated with a specific step of the saga process.
    /// This method retrieves the message instance linked to the given saga and message identifiers.
    /// </summary>
    /// <param name="sagaId">The unique identifier of the saga instance.</param>
    /// <param name="messageId">The unique identifier of the message triggering the step.</param>
    /// <returns>The message instance associated with the specified saga and step, or null if no message is found.</returns>
    Task<IMessage?> LoadSagaStepMessageAsync(Guid sagaId, Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the persisted saga data for a given saga instance.
    /// </summary>
    Task<TSagaData> LoadSagaDataAsync<TSagaData>(Guid sagaId, CancellationToken cancellationToken = default) where TSagaData : SagaData, new();

    /// <summary>
    /// Saves the saga's state data to persistent storage.
    /// </summary>
    Task SaveSagaDataAsync<TSagaData>(Guid sagaId, TSagaData? data, CancellationToken cancellationToken = default) where TSagaData : SagaData;
    /// <summary>
    /// Loads the full saga context (including metadata and tracking state) for the given saga identifier.
    /// </summary>
    Task<ISagaContext<TMessage, TSagaData>> LoadContextAsync<TMessage, TSagaData>(Guid sagaId, TMessage message, Type handlerType, CancellationToken cancellationToken = default) 
        where TSagaData : SagaData
        where TMessage : IMessage;
}