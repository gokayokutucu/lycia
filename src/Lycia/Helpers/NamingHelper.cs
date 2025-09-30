// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Extensions;

namespace Lycia.Helpers;

public static class NamingHelper
{
    //TODO: “TypeName:Version” can be add to the end of the step name to ensure uniqueness across different versions of the same step.
    /// <summary>
    /// Constructs the dictionary key used for storing step metadata, combining step type and handler type.
    /// </summary>
    public static string GetStepNameWithHandler(Type stepType, Type handlerType, Guid messageId) =>
        $"step:{stepType.ToSagaStepName()}:handler:{handlerType.ToSagaStepName()}:message-id:{messageId}";
}