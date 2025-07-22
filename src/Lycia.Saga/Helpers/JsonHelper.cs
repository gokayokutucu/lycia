using Newtonsoft.Json;

namespace Lycia.Saga.Helpers;

public static class JsonHelper
{
    public static string SerializeSafe(object? obj)
    {
        var settings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };
        return JsonConvert.SerializeObject(obj, settings);
    }
    
    public static string SerializeSafe(object? obj, Type type, JsonSerializerSettings? settings = null)
    {
        if (obj == null)
            return string.Empty;
        try
        {
            return JsonConvert.SerializeObject(obj, type, settings ?? new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            });
        }
        catch (Exception ex)
        {
            return $"[SerializationError: {ex.Message}]";
        }
    }

    public static string SerializeSafe(object? obj, Type type, ReferenceLoopHandling loopHandling)
    {
        return SerializeSafe(obj, type, new JsonSerializerSettings
        {
            ReferenceLoopHandling = loopHandling
        });
    }
}