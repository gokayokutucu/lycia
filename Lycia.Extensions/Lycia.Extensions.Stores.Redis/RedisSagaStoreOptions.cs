namespace Lycia.Extensions.Stores.Redis
{
    public class RedisSagaStoreOptions
    {
        public string KeyPrefix { get; set; } = "sagas:";
    }
}
