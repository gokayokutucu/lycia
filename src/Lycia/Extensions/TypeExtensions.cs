// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Messaging;

namespace Lycia.Extensions;

/// <summary>Provides message, response, and saga-handler type classification helpers.</summary>
public static class TypeExtensions
{
    /// <summary>Creates the stable persisted saga-step type name for a runtime type.</summary>
    public static string ToSagaStepName(this Type type)
    {
        return $"{type.FullName}:assembly:{type.Assembly.GetName().Name}";
    }

    /// <summary>Resolves a previously persisted saga-step type name, returning <see langword="null"/> when unavailable.</summary>
    public static Type? TryResolveSagaStepType(this string qualifiedName)
    {
        var type = Type.GetType(qualifiedName);
        if (type == null)
        {
            Console.WriteLine($"[WARN] Could not resolve type: {qualifiedName}");
        }

        return type;
    }
    
    /// <summary>Creates an assembly-qualified name without version, culture, or public-key metadata.</summary>
    public static string GetSimplifiedQualifiedName(this Type type)
    {
        return $"{type.FullName}, {type.Assembly.GetName().Name}";
    }

    /// <summary>Determines whether a type implements a strongly typed success-response contract.</summary>
    public static bool IsSuccessResponse(this Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISuccessResponse<>));
    
    /// <summary>Determines whether a type derives from any closed <see cref="ResponseBase{TRequest}"/> type.</summary>
    public static bool IsSubclassOfResponseBase(this Type? type)
    {
        while (type != null && type != typeof(object))
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ResponseBase<>))
                return true;
            type = type.BaseType;
        }

        return false;
    }

    /// <summary>Determines whether a handler implements a closed form of the specified generic interface.</summary>
    public static bool IsSubclassOfRawGeneric(this Type? handlerType, Type interfaceType)
    {
        return handlerType?
            .GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == interfaceType) ?? false;
    }

    /// <summary>Determines whether a type derives from a closed form of the specified generic base type.</summary>
    public static bool IsSubclassOfRawGenericBase(this Type? type, Type genericBaseType)
    {
        while (type != null && type != typeof(object))
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == genericBaseType)
                return true;
            type = type.BaseType;
        }

        return false;
    }
}
