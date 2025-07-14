namespace Lycia.IntegrationTests;

public class RabbitMqSagaCompensationIntegrationTests : IAsyncLifetime
{
    // RabbitMqContainer ve RedisContainer ayarla
    // (Gerekirse appSettings üzerinden connection stringleri paylaş)

    public async Task InitializeAsync()
    {
        // Start containers
    }

    public async Task DisposeAsync()
    {
        // Stop containers
    }

    [Fact]
    public async Task SagaChain_Should_Compensate_On_Failure()
    {
        // 1. Test için SagaEventBus, SagaStore ve SagaDispatcher kur.
        // 2. Başlangıç mesajı publish et.
        // 3. Bir step bilinçli olarak hata fırlatsın.
        // 4. Compensation chain otomatik tetiklensin.
        // 5. Hem RabbitMQ kuyruğunda hem Redis’te step status’ler assert edilsin:
        //    - Failed, Compensated, CompensationFailed vs.
    }

    // Testte kullanacağın dummy event/command, handler ve compensate handler’larını burada tanımla.
}