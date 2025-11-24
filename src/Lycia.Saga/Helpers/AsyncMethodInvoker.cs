// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Helpers;

public static class AsyncMethodInvoker
{
    // Extension/Helper
    public static async Task<object?> InvokeGenericTaskResultAsync(this object target, string methodName, Type genericType, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName)?.MakeGenericMethod(genericType);
        var task = (Task)method?.Invoke(target, args)!;
        await task.ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result");
        return resultProperty?.GetValue(task);
    }
}