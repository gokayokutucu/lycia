using Lycia.Saga.Abstractions;
using Lycia.Messaging.Enums;
using Lycia.Saga.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

// Added for SagaContext<>

namespace Lycia.Infrastructure.Compensating;

public class SagaCompensationCoordinator(IServiceProvider serviceProvider) : ISagaCompensationCoordinator
{
    /// <summary>
    /// Compensates the saga steps corresponding to the specified failed step type.
    /// </summary>
    /// <param name="sagaId">The identifier of the saga.</param>
    /// <param name="failedStepType">The type of the failed step to compensate.</param>
    public async Task CompensateAsync(Guid sagaId, Type failedStepType)
    {
        try
        {
            if (serviceProvider.GetService(typeof(ISagaStore)) is not ISagaStore sagaStore) return;

            var steps = await sagaStore.GetSagaHandlerStepsAsync(sagaId);

            var failedSteps = steps.Where(s => s.Value.MessageTypeName == failedStepType.AssemblyQualifiedName);
            foreach (var step in failedSteps)
            {
                var stepType = Type.GetType(step.Key.stepType);
                if (stepType == null) continue;

                var payloadType = Type.GetType(step.Value.MessageTypeName);
                if (payloadType == null) continue;

                var messageObject = JsonConvert.DeserializeObject(step.Value.MessagePayload, payloadType);
                if (messageObject == null) continue;

                // Try to get the handler via ISagaCompensationHandler<>
                var handlerType = typeof(ISagaCompensationHandler<>).MakeGenericType(stepType);
                var handler = serviceProvider.GetService(handlerType);

                // If no handler found, try to find candidate handlers using the new method
                if (handler == null)
                {
                    var candidateHandlers = FindCompensationHandlers(stepType);

                    foreach (var candidateHandler in candidateHandlers)
                    {
                        handler = candidateHandler;
                        break;
                    }
                }

                if (handler == null) continue;

                var handlerBaseType = handler.GetType().BaseType;
                if (handlerBaseType == null)
                {
                    // Fallback: try to invoke CompensateAsync on handler directly
                    var compensateMethodFallback = handler.GetType().GetMethod("CompensateAsync", [stepType]);
                    if (compensateMethodFallback != null)
                    {
                        await (Task)compensateMethodFallback.Invoke(handler, [messageObject])!;
                    }
                    continue;
                }

                var handlerGenericDef = handlerBaseType.IsGenericType ? handlerBaseType.GetGenericTypeDefinition() : null;

                // Determine whether to invoke CompensateStartAsync or CompensateAsync based on base type
                if (handlerGenericDef == typeof(StartReactiveSagaHandler<>) || handlerGenericDef == typeof(StartCoordinatedSagaHandler<,,>))
                {
                    var compensateStartMethod = handler.GetType().GetMethod("CompensateStartAsync", [stepType]);
                    if (compensateStartMethod != null)
                    {
                        await (Task)compensateStartMethod.Invoke(handler, [messageObject])!;
                        continue;
                    }
                }

                var compensateMethod = handler.GetType().GetMethod("CompensateAsync", [stepType]);
                if (compensateMethod != null)
                {
                    await (Task)compensateMethod.Invoke(handler, [messageObject])!;
                }
            }
        }
        catch (Exception ex)
        {
            // Log exception here (e.g., using ILogger) before rethrowing
            throw;
        }
    }

    /// <summary>
    /// Compensates the parent saga step of the specified step type.
    /// </summary>
    /// <param name="sagaId">The identifier of the saga.</param>
    /// <param name="stepType">The type of the step whose parent is to be compensated.</param>
    public async Task CompensateParentAsync(Guid sagaId, Type stepType)
    {
        try
        {
            if (serviceProvider.GetService(typeof(ISagaStore)) is not ISagaStore sagaStore) return;

            var steps = await sagaStore.GetSagaHandlerStepsAsync(sagaId);

            var stepList = steps.ToList();
            var index = stepList.FindIndex(s => s.Key.stepType == stepType.FullName);
            if (index <= 0) return;

            for (var i = index - 1; i >= 0; i--)
            {
                var candidate = stepList[i];
                var candidateStepType = Type.GetType(candidate.Key.stepType);
                if (candidateStepType == null) continue;

                if (candidate.Value.Status is StepStatus.Compensated or StepStatus.Failed) continue;

                var payloadType = Type.GetType(candidate.Value.MessageTypeName);
                if (payloadType == null) continue;

                var messageObject = JsonConvert.DeserializeObject(candidate.Value.MessagePayload, payloadType);
                if (messageObject == null) continue;

                // Try to get the handler via ISagaCompensationHandler<>
                var handlerType = typeof(ISagaCompensationHandler<>).MakeGenericType(candidateStepType);
                var handler = serviceProvider.GetService(handlerType);

                // If no handler found, try to find candidate handlers using the new method
                if (handler == null)
                {
                    var candidateHandlers = FindCompensationHandlers(candidateStepType);

                    foreach (var candidateHandler in candidateHandlers)
                    {
                        handler = candidateHandler;
                        break;
                    }
                }

                if (handler == null) return;

                var handlerBaseType = handler.GetType().BaseType;
                if (handlerBaseType == null)
                {
                    // Fallback: try to invoke CompensateAsync on handler directly
                    var compensateMethodFallback = handler.GetType().GetMethod("CompensateAsync", [candidateStepType]);
                    if (compensateMethodFallback != null)
                    {
                        await (Task)compensateMethodFallback.Invoke(handler, [messageObject])!;
                    }
                    return;
                }

                var handlerGenericDef = handlerBaseType.IsGenericType ? handlerBaseType.GetGenericTypeDefinition() : null;

                // Determine whether to invoke CompensateStartAsync or CompensateAsync based on base type
                if (handlerGenericDef == typeof(StartReactiveSagaHandler<>) || handlerGenericDef == typeof(StartCoordinatedSagaHandler<,,>))
                {
                    var compensateStartMethod = handler.GetType().GetMethod("CompensateStartAsync", [candidateStepType]);
                    if (compensateStartMethod != null)
                    {
                        await (Task)compensateStartMethod.Invoke(handler, [messageObject])!;
                        return;
                    }
                }

                var compensateMethod = handler.GetType().GetMethod("CompensateAsync", [candidateStepType]);
                if (compensateMethod != null)
                {
                    await (Task)compensateMethod.Invoke(handler, [messageObject])!;
                }
                return;
            }
        }
        catch (Exception ex)
        {
            // Log exception here (e.g., using ILogger) before rethrowing
            throw;
        }
    }

    /// <summary>
    /// Finds all candidate compensation handlers for a given message type.
    /// It collects all registered ISagaStartHandler<>, ISagaHandler<>, and ISagaCompensationHandler<> services,
    /// then filters those whose types are subclasses of known saga handler base types or implement ISagaCompensationHandler<> for the message type.
    /// Duplicate handler types are removed.
    /// </summary>
    /// <param name="messageType">The message type to find handlers for.</param>
    /// <returns>An enumerable of handler instances.</returns>
    private IEnumerable<object> FindCompensationHandlers(Type messageType)
    {
        var knownBaseTypes = new[]
        {
            typeof(ReactiveSagaHandler<>),
            typeof(StartReactiveSagaHandler<>),
            typeof(CoordinatedSagaHandler<,,>),
            typeof(StartCoordinatedSagaHandler<,,>)
        };

        var handlerInterfaces = new[]
        {
            typeof(ISagaStartHandler<>).MakeGenericType(messageType),
            typeof(ISagaHandler<>).MakeGenericType(messageType),
            typeof(ISagaCompensationHandler<>).MakeGenericType(messageType)
        };

        var handlers = new List<object>();

        // Collect all handlers registered for the messageType for the relevant interfaces
        foreach (var handlerInterface in handlerInterfaces)
        {
            var services = serviceProvider.GetServices(handlerInterface);
            handlers.AddRange(services!);
        }

        // Filter handlers whose type is subclass of known saga handler base types or implement ISagaCompensationHandler<> for messageType
        var filteredHandlers = handlers.Where(handler =>
        {
            var handlerType = handler.GetType();

            // Check if handler implements ISagaCompensationHandler<> for messageType
            var implementsCompensationHandler = handlerType.GetInterfaces().Any(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>) &&
                i.GetGenericArguments()[0] == messageType);

            if (implementsCompensationHandler)
            {
                return true;
            }

            // Check base types for known saga handler base types
            var baseType = handlerType.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType)
                {
                    var genericDef = baseType.GetGenericTypeDefinition();
                    if (knownBaseTypes.Contains(genericDef))
                    {
                        // For CoordinatedSagaHandler and StartCoordinatedSagaHandler, the first generic argument is the message type
                        var genericArgs = baseType.GetGenericArguments();
                        if (genericArgs.Length > 0 && genericArgs[0] == messageType)
                        {
                            return true;
                        }
                    }
                }
                baseType = baseType.BaseType;
            }

            return false;
        });

        // Remove duplicate handler types
        var distinctHandlers = filteredHandlers
            .GroupBy(h => h.GetType())
            .Select(g => g.First());

        return distinctHandlers;
    }
}