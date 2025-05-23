namespace Lycia.Saga;

public abstract class SagaData
{
    public Dictionary<string, object> Extras { get; set; } = new();

    public T Get<T>(string key)
    {
        return Extras.TryGetValue(key, out var value)
            ? (T)value
            : throw new KeyNotFoundException($"Key '{key}' not found in SagaData.Extras");
    }

    public void Set<T>(string key, T value)
    {
        Extras[key] = value!;
    }
}