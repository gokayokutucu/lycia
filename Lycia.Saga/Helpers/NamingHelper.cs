using Lycia.Saga.Extensions;

namespace Lycia.Saga.Helpers;

public static class NamingHelper
{
    /// <summary>
    /// Constructs the dictionary key used for storing step metadata, combining step type and handler type.
    /// </summary>
    public static string GetStepNameWithHandler(Type stepType, Type handlerType) =>
        $"{stepType.ToSagaStepName()}_{handlerType.FullName}";
}