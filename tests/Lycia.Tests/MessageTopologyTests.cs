using Lycia.Extensions.Helpers;
using Lycia.Extensions.Kafka;
using Lycia.Extensions.Nats;
using Lycia.Helpers;
using Lycia.Messaging;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Extensions;
using Lycia.Saga.Messaging;

namespace Lycia.Tests;

public class MessageTopologyTests
{
    [Fact]
    public void EndpointResolver_ResolvesSingleMarkerDeterministically()
    {
        var resolver = new CommandEndpointResolver();

        Assert.Equal("StockService", resolver.Resolve(typeof(ReserveStockCommand)));
        Assert.Equal("StockService", resolver.Resolve(typeof(ReserveStockCommand)));
    }

    [Fact]
    public void EndpointResolver_RejectsMissingMarker() =>
        Assert.Contains("exactly one", Assert.Throws<InvalidOperationException>(
            () => CommandEndpointResolver.Default.Resolve(typeof(UnownedCommand))).Message);

    [Fact]
    public void EndpointResolver_RejectsMultipleMarkers() =>
        Assert.Contains("multiple", Assert.Throws<InvalidOperationException>(
            () => CommandEndpointResolver.Default.Resolve(typeof(AmbiguousCommand))).Message,
            StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void EndpointResolver_DoesNotTreatICommandAsEndpoint() =>
        Assert.Throws<InvalidOperationException>(() => CommandEndpointResolver.Default.Resolve(typeof(UnownedCommand)));

    [Fact]
    public void EndpointResolver_RejectsInvalidMarkerName() =>
        Assert.Contains("I{LogicalOwner}Command", Assert.Throws<InvalidOperationException>(
            () => CommandEndpointResolver.Default.Resolve(typeof(InvalidMarkerCommand))).Message);

    [Fact]
    public void Naming_SeparatesCommandEventAndResponseIdentity()
    {
        var commandA = MessagingNamingHelper.GetCommandQueueName(typeof(ReserveStockCommand), "StockService");
        var commandB = MessagingNamingHelper.GetQueueName(typeof(ReserveStockCommand), typeof(RenamedHandler), "StockService");

        Assert.Equal("command.ReserveStockCommand.StockService", commandA);
        Assert.Equal(commandA, commandB);
        Assert.DoesNotContain(nameof(StockHandler), commandA);
        Assert.Equal("event.StockReservedEvent.StockHandler.StockService",
            MessagingNamingHelper.GetEventSubscriptionQueueName(typeof(StockReservedEvent), typeof(StockHandler), "StockService"));
        Assert.Equal("response.StockResponse.StockService",
            MessagingNamingHelper.GetResponseQueueName(typeof(StockResponse), "StockService"));
        Assert.Equal("StockService", MessagingNamingHelper.GetCommandRoutingKey(typeof(ReserveStockCommand)));
        Assert.DoesNotContain("#", MessagingNamingHelper.GetCommandRoutingKey(typeof(ReserveStockCommand)));
    }

    [Fact]
    public void RabbitMqTopology_UsesDirectCommandsFanoutEventsAndTargetedResponses()
    {
        Assert.Equal("direct", RabbitMqTopology.GetExchangeType(typeof(ReserveStockCommand)));
        Assert.Equal("fanout", RabbitMqTopology.GetExchangeType(typeof(StockReservedEvent)));
        Assert.Equal(string.Empty, RabbitMqTopology.GetBindingKey(typeof(StockReservedEvent), "StockService"));

        var response = new StockResponse { ReplyTo = "OrderService" };
        Assert.Equal("OrderService", RabbitMqTopology.GetPublishKey(response, typeof(StockResponse)));
        Assert.Equal("OrderService", RabbitMqTopology.GetBindingKey(typeof(StockResponse), "OrderService"));
    }

    [Fact]
    public void StartupValidation_AcceptsOneOwnerAndRepeatedReplicaRegistration()
    {
        CommandTopologyValidator.Validate("stockservice", new[]
        {
            (typeof(ReserveStockCommand), typeof(StockHandler)),
            (typeof(ReserveStockCommand), typeof(StockHandler))
        });
    }

    [Fact]
    public void StartupValidation_RejectsWrongApplication()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CommandTopologyValidator.Validate(
            "OrderService", new[] { (typeof(ReserveStockCommand), typeof(StockHandler)) }));
        Assert.Contains(typeof(ReserveStockCommand).FullName!, exception.Message);
        Assert.Contains(typeof(StockHandler).FullName!, exception.Message);
        Assert.Contains("StockService", exception.Message);
        Assert.Contains("OrderService", exception.Message);
    }

    [Fact]
    public void StartupValidation_RejectsDuplicateCommandHandlers()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CommandTopologyValidator.Validate(
            "StockService", new[]
            {
                (typeof(ReserveStockCommand), typeof(StockHandler)),
                (typeof(ReserveStockCommand), typeof(RenamedHandler))
            }));
        Assert.Contains(typeof(StockHandler).FullName!, exception.Message);
        Assert.Contains(typeof(RenamedHandler).FullName!, exception.Message);
    }

    [Fact]
    public void StartupValidation_AllowsMultipleEventHandlers() =>
        CommandTopologyValidator.Validate("StockService", new[]
        {
            (typeof(StockReservedEvent), typeof(StockHandler)),
            (typeof(StockReservedEvent), typeof(RenamedHandler))
        });

    [Fact]
    public void ResponseRouting_PropagatesRequestMetadata()
    {
        var command = new ReserveStockCommand { ReplyTo = "OrderService", RequestId = Guid.NewGuid() };
        var response = new StockResponse();

        response.PropagateResponseRouting(command);

        Assert.Equal(command.ReplyTo, response.ReplyTo);
        Assert.Equal(command.RequestId, response.RequestId);
    }

    [Fact]
    public void NatsTopology_UsesOwnerSubscriptionAndRequesterResponseSubjects()
    {
        var command = new ReserveStockCommand();
        var response = new StockResponse { ReplyTo = "OrderService" };

        Assert.Equal("command.StockService.ReserveStockCommand",
            NatsTopology.GetPublishSubject(command, command.GetType()));
        Assert.Equal("event.StockReservedEvent",
            NatsTopology.GetSubscriptionSubject(typeof(StockReservedEvent), "StockService"));
        Assert.Equal("response.OrderService.StockResponse",
            NatsTopology.GetPublishSubject(response, response.GetType()));
        Assert.Equal("lycia_event_StockReservedEvent_StockHandler_StockService",
            NatsTopology.GetQueueGroup("event.StockReservedEvent.StockHandler.StockService"));
    }

    [Fact]
    public void KafkaTopology_UsesDistinctSubscriptionGroupsAndStablePartitionKey()
    {
        var command = new ReserveStockCommand { CorrelationId = Guid.NewGuid() };
        var groupA = KafkaTopology.GetConsumerGroup("lycia", "event.StockReservedEvent.StockHandler.StockService");
        var groupB = KafkaTopology.GetConsumerGroup("lycia", "event.StockReservedEvent.RenamedHandler.StockService");

        Assert.Equal("lycia.command.StockService.ReserveStockCommand",
            KafkaTopology.GetPublishTopic("lycia", command, command.GetType()));
        Assert.NotEqual(groupA, groupB);
        Assert.Equal(command.CorrelationId.ToString("N"), KafkaTopology.GetPartitionKey(command));
    }

    private interface IStockServiceCommand : ICommand, ICommandEndpoint { }
    private interface IWarehouseCommand : ICommand, ICommandEndpoint { }
    private interface IStockEndpoint : ICommand, ICommandEndpoint { }
    private sealed class ReserveStockCommand : CommandBase, IStockServiceCommand { }
    private sealed class UnownedCommand : CommandBase { }
    private sealed class AmbiguousCommand : CommandBase, IStockServiceCommand, IWarehouseCommand { }
    private sealed class InvalidMarkerCommand : CommandBase, IStockEndpoint { }
    private sealed class StockReservedEvent : EventBase { }
    private sealed class StockResponse : ResponseBase<ReserveStockCommand> { }
    private sealed class StockHandler { }
    private sealed class RenamedHandler { }
}
