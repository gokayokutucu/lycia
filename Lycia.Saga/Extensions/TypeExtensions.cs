namespace Lycia.Saga.Extensions;

public static class TypeExtensions
{
    public static string ToSagaStepName(this Type type)
    {
        return $"{type.FullName}, {type.Assembly.GetName().Name}";
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
}