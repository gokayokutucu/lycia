namespace Lycia.Saga.Extensions;

public static class TypeExtensions
{
    public static string ToSagaStepName(this Type type)
    {
        return $"{type.FullName}:assembly:{type.Assembly.GetName().Name}";
    }
    
    public static Type? TryResolveSagaStepType(this string qualifiedName)
    {
        var type = Type.GetType(qualifiedName);
        if (type == null)
        {
            Console.WriteLine($"[WARN] Could not resolve type: {qualifiedName}");
        }
        return type;
    }
    
    public static bool IsSubclassOfRawGeneric(this Type? handlerType, Type interfaceType)
    {
        return handlerType?
            .GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == interfaceType) ?? false;
    }
    
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