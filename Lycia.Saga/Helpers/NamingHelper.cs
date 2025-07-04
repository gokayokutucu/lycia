using Lycia.Saga.Extensions;

namespace Lycia.Saga.Helpers;

public static class NamingHelper
{
    //TODO: “TypeName:Version” can be add to the end of the step name to ensure uniqueness across different versions of the same step.
    /// <summary>
    /// Constructs the dictionary key used for storing step metadata, combining step type and handler type.
    /// </summary>
    public static string GetStepNameWithHandler(Type stepType, Type handlerType, Guid messageId) =>
        $"step:{stepType.ToSagaStepName()}:handler:{handlerType.FullName}:message-id:{messageId}";
}