using Lycia.Extensions.Helpers;
using Lycia.Helpers;
using Lycia.Messaging;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Messaging;

namespace Lycia.Tests;

public class MessageTopologyTests
{
    [Fact]
    public void OwnershipAndNaming_AreNetFrameworkCompatible()
    {
        Assert.Equal("StockService", CommandEndpointResolver.Default.Resolve(typeof(ReserveStockCommand)));
        Assert.Equal("command.ReserveStockCommand.stockservice",
            MessagingNamingHelper.GetCommandQueueName(typeof(ReserveStockCommand), "StockService"));
        Assert.Equal("direct", RabbitMqTopology.GetExchangeType(typeof(ReserveStockCommand)));
        Assert.Equal("fanout", RabbitMqTopology.GetExchangeType(typeof(StockReservedEvent)));
    }

    [Theory]
    [InlineData("StockService")]
    [InlineData("stock-service")]
    [InlineData("STOCK.SERVICE")]
    public void EndpointIdentity_IsCanonicalOnNetFramework(string value) =>
        Assert.Equal("stockservice", EndpointIdentityNormalizer.Default.Normalize(value));

    [Fact]
    public void StartupValidation_RejectsWrongOwnerAndDuplicateHandlers()
    {
        Assert.Throws<InvalidOperationException>(() => CommandTopologyValidator.Validate(
            "OrderService", new[] { (typeof(ReserveStockCommand), typeof(HandlerA)) }));
        Assert.Throws<InvalidOperationException>(() => CommandTopologyValidator.Validate(
            "StockService", new[]
            {
                (typeof(ReserveStockCommand), typeof(HandlerA)),
                (typeof(ReserveStockCommand), typeof(HandlerB))
            }));
    }

    private interface IStockServiceCommand : ICommand, ICommandEndpoint { }
    private sealed class ReserveStockCommand : CommandBase, IStockServiceCommand { }
    private sealed class StockReservedEvent : EventBase { }
    private sealed class HandlerA { }
    private sealed class HandlerB { }
}
