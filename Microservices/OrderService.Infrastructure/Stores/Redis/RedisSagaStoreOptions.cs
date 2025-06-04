namespace OrderService.Infrastructure.Stores.Redis
{
    public class RedisSagaStoreOptions
    {
        public string ConnectionString { get; set; }
        public string InstanceName { get; set; } = "sagas"; // Default prefix, similar to KeyPrefix
    }
}
