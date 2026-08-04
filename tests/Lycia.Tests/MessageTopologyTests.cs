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

        Assert.Equal("command.ReserveStockCommand.stockservice", commandA);
        Assert.Equal(commandA, commandB);
        Assert.DoesNotContain(nameof(StockHandler), commandA);
        Assert.Equal("event.StockReservedEvent.StockHandler.stockservice",
            MessagingNamingHelper.GetEventSubscriptionQueueName(typeof(StockReservedEvent), typeof(StockHandler), "StockService"));
        Assert.Equal("response.StockResponse.stockservice",
            MessagingNamingHelper.GetResponseQueueName(typeof(StockResponse), "StockService"));
        Assert.Equal("stockservice", MessagingNamingHelper.GetCommandRoutingKey(typeof(ReserveStockCommand)));
        Assert.DoesNotContain("#", MessagingNamingHelper.GetCommandRoutingKey(typeof(ReserveStockCommand)));
    }

    [Fact]
    public void RabbitMqTopology_UsesDirectCommandsFanoutEventsAndTargetedResponses()
    {
        Assert.Equal("direct", RabbitMqTopology.GetExchangeType(typeof(ReserveStockCommand)));
        Assert.Equal("fanout", RabbitMqTopology.GetExchangeType(typeof(StockReservedEvent)));
        Assert.Equal(string.Empty, RabbitMqTopology.GetBindingKey(typeof(StockReservedEvent), "StockService"));

        var response = new StockResponse { ResponseEndpoint = "Order-Service" };
        Assert.Equal("orderservice", RabbitMqTopology.GetPublishKey(response, typeof(StockResponse)));
        Assert.Equal("orderservice", RabbitMqTopology.GetBindingKey(typeof(StockResponse), "OrderService"));
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
    public void MessageIdentity_PropagatesRequestResponseAndCausationMetadata()
    {
        var sagaId = Guid.NewGuid();
        var current = new StockReservedEvent { SagaId = sagaId };
        var command = new ReserveStockCommand();
        command.PrepareCommand(current, sagaId, "orderservice");
        var response = new StockResponse();

        response.PrepareResponse(command, sagaId, "orderservice");

        Assert.Equal(command.MessageId, command.RequestId);
        Assert.Equal(current.MessageId, command.CausationId);
        Assert.Equal(current.MessageId, command.ParentMessageId);
        Assert.Equal(command.MessageId, response.RequestId);
        Assert.NotEqual(response.RequestId, response.MessageId);
        Assert.Equal(command.MessageId, response.CausationId);
        Assert.Equal(command.MessageId, response.ParentMessageId);
        Assert.Equal(current.CorrelationId, response.CorrelationId);
        Assert.Equal(sagaId, response.SagaId);
        Assert.Equal("orderservice", response.ResponseEndpoint);
    }

    [Fact]
    public void ResponseBase_DoesNotInferRequestIdFromParentMessageId()
    {
        var parent = Guid.NewGuid();
        var response = new StockResponse(parent);

        Assert.Equal(parent, response.ParentMessageId);
        Assert.Equal(Guid.Empty, response.RequestId);
        Assert.NotEqual(response.MessageId, response.RequestId);
    }

    [Theory]
    [InlineData("StockService")]
    [InlineData("stockservice")]
    [InlineData("stock-service")]
    [InlineData("stock_service")]
    [InlineData("STOCK.SERVICE")]
    [InlineData("stock service")]
    public void EndpointIdentityNormalizer_EquivalentSpellingsShareCanonicalKey(string value) =>
        Assert.Equal("stockservice", EndpointIdentityNormalizer.Default.Normalize(value));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-_.")]
    [InlineData("@@")]
    public void EndpointIdentityNormalizer_RejectsInvalidValues(string value) =>
        Assert.Throws<ArgumentException>(() => EndpointIdentityNormalizer.Default.Normalize(value));

    [Fact]
    public void PublishingAResponse_IsRejectedEvenForDualContractTypes()
    {
        var response = new InvalidBroadcastResponse();
        var current = new StockReservedEvent();

        var exception = Assert.Throws<InvalidOperationException>(
            () => response.PrepareEvent(current, Guid.NewGuid()));
        Assert.Contains("Context.Respond", exception.Message);
    }

    [Fact]
    public void NatsTopology_UsesOwnerSubscriptionAndRequesterResponseSubjects()
    {
        var command = new ReserveStockCommand();
        var response = new StockResponse { ResponseEndpoint = "Order-Service" };

        Assert.Equal("command.stockservice.ReserveStockCommand",
            NatsTopology.GetPublishSubject(command, command.GetType()));
        Assert.Equal("event.StockReservedEvent",
            NatsTopology.GetSubscriptionSubject(typeof(StockReservedEvent), "StockService"));
        Assert.Equal("response.orderservice.StockResponse",
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

        Assert.Equal("lycia.command.stockservice.ReserveStockCommand",
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
    private sealed class StockResponse : ResponseBase<ReserveStockCommand>
    {
        public StockResponse(Guid? parentMessageId = null) : base(parentMessageId) { }
    }
    private sealed class InvalidBroadcastResponse : ResponseBase<ReserveStockCommand>, IEvent { }
    private sealed class StockHandler { }
    private sealed class RenamedHandler { }
}
