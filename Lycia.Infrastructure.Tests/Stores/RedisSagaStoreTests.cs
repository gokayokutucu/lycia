using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Lycia.Infrastructure.Redis;
using Lycia.Infrastructure.Stores;
using Lycia.Saga; // For SagaData
using Lycia.Saga.Abstractions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Lycia.Infrastructure.Tests.Stores
{
    public class RedisSagaStoreTests
    {
        private readonly Mock<IRedisConnectionFactory> _mockRedisConnectionFactory;
        private readonly Mock<IDatabase> _mockDatabase;
        private readonly Mock<IEventBus> _mockEventBus;
        private readonly Mock<ISagaIdGenerator> _mockSagaIdGenerator;
        private readonly Mock<ILogger<RedisSagaStore>> _mockLogger;

        // Concrete SagaData implementation for testing
        public class TestSagaData : SagaData
        {
            public TestSagaData() : base() { }
            public string? TestProperty { get; set; }
        }

        public RedisSagaStoreTests()
        {
            _mockRedisConnectionFactory = new Mock<IRedisConnectionFactory>();
            _mockDatabase = new Mock<IDatabase>();
            _mockEventBus = new Mock<IEventBus>();
            _mockSagaIdGenerator = new Mock<ISagaIdGenerator>();
            _mockLogger = new Mock<ILogger<RedisSagaStore>>();

            _mockRedisConnectionFactory.Setup(f => f.GetDatabase(It.IsAny<int>()))
                .Returns(_mockDatabase.Object);
        }

        [Fact]
        public async Task SaveAndLoadSagaData_WhenDataExists_ShouldPersistAndRetrieveCorrectly()
        {
            // Arrange
            var sagaId = Guid.NewGuid();
            var originalSagaData = new TestSagaData 
            { 
                TestProperty = "TestValue"
            };
            originalSagaData.Extras["ExtraKey"] = "ExtraValue";
            originalSagaData.Extras["AnotherKey"] = 123; 

            RedisValue capturedJson = default;
            var expectedRedisKey = $"saga:{sagaId}:data";

            _mockDatabase.Setup(db => db.StringSetAsync(
                It.Is<RedisKey>(k => k == expectedRedisKey), 
                It.IsAny<RedisValue>(), 
                null, When.Always, CommandFlags.None))
                .Callback<RedisKey, RedisValue, TimeSpan?, When, CommandFlags>((key, value, expiry, when, flags) => capturedJson = value)
                .ReturnsAsync(true);

            _mockDatabase.Setup(db => db.StringGetAsync(It.Is<RedisKey>(k => k == expectedRedisKey), CommandFlags.None))
                .ReturnsAsync(() => capturedJson); 

            var redisSagaStore = new RedisSagaStore(
                _mockRedisConnectionFactory.Object,
                _mockEventBus.Object,
                _mockSagaIdGenerator.Object,
                _mockLogger.Object);

            // Act
            await redisSagaStore.SaveSagaDataAsync(sagaId, originalSagaData);
            var loadedSagaData = await redisSagaStore.LoadSagaDataAsync(sagaId);

            // Assert
            _mockDatabase.Verify(db => db.StringSetAsync(
                expectedRedisKey, 
                capturedJson, 
                null, 
                When.Always, 
                CommandFlags.None), Times.Once);

            _mockDatabase.Verify(db => db.StringGetAsync(
                expectedRedisKey, 
                CommandFlags.None), Times.Once);

            loadedSagaData.Should().NotBeNull();
            loadedSagaData.Should().BeAssignableTo<SagaData>(); 
            
            loadedSagaData!.Extras.Should().ContainKey("ExtraKey");
            loadedSagaData.Extras["ExtraKey"].Should().BeEquivalentTo("ExtraValue"); 
            
            loadedSagaData.Extras.Should().ContainKey("AnotherKey");
            loadedSagaData.Extras["AnotherKey"].Should().BeEquivalentTo(123);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            // The capturedJson is the actual value from StringSetAsync.
            // LoadSagaDataAsync uses an internal wrapper, so for full verification of TestProperty,
            // we deserialize the captured JSON to the concrete TestSagaData type.
            var deserializedForVerification = JsonSerializer.Deserialize<TestSagaData>(capturedJson.ToString(), options);
            
            deserializedForVerification.Should().NotBeNull();
            deserializedForVerification!.TestProperty.Should().Be("TestValue");
            
            deserializedForVerification.Extras.Should().ContainKey("ExtraKey");
            if (deserializedForVerification.Extras["ExtraKey"] is JsonElement extraKeyElement)
            {
                extraKeyElement.GetString().Should().Be("ExtraValue");
            }
            else
            {
                 deserializedForVerification.Extras["ExtraKey"].ToString().Should().Be("ExtraValue");
            }

            if (deserializedForVerification.Extras["AnotherKey"] is JsonElement anotherKeyElement)
            {
                 anotherKeyElement.GetInt32().Should().Be(123);
            }
            else
            {
                Convert.ToInt32(deserializedForVerification.Extras["AnotherKey"]).Should().Be(123);
            }
        }
    }
}
