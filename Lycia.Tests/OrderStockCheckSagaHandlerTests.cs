using System;
using System.Threading.Tasks;
using Lycia.Saga; // For SagaData
using Lycia.Saga.Abstractions;
using Lycia.Messaging; // For IMessage
using Moq;
using OrderService.Application.Features.Orders.Sagas; // The handler
using Sample.Shared.Messages.Events;
using Sample.Shared.Messages.Sagas; // The SagaData
using Xunit;

public class OrderStockCheckSagaHandlerTests
{
    private readonly OrderStockCheckSagaHandler _handler;
    private readonly OrderStockCheckSagaData _sagaData;

    public OrderStockCheckSagaHandlerTests()
    {
        _sagaData = new OrderStockCheckSagaData();
        _handler = new OrderStockCheckSagaHandler();
        // Assuming handler has no constructor dependencies for now.
        // If it did (e.g. ILogger), they would be mocked and injected here.
    }

    [Fact]
    public async Task HandleStartAsync_InitializesSagaDataCorrectly()
    {
        // Arrange
        var command = new OrderCreationInitiatedEvent
        {
            OrderId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ProductId = "TestProduct",
            Quantity = 10,
            TotalPrice = 100m,
            SagaId = Guid.NewGuid() // Events should carry SagaId if part of an existing saga or starting one
        };

        var mockContext = new Mock<ISagaContext<OrderCreationInitiatedEvent, OrderStockCheckSagaData>>();
        mockContext.Setup(c => c.Data).Returns(_sagaData);
        mockContext.Setup(c => c.SagaId).Returns(command.SagaId.Value);
        // Setup Publish on this context if HandleStartAsync is expected to publish something
        // For this handler, HandleStartAsync doesn't publish directly.

        // Act
        await _handler.HandleStartAsync(command, mockContext.Object);

        // Assert
        Assert.Equal(command.OrderId, _sagaData.OrderId);
        Assert.Equal(command.UserId, _sagaData.UserId);
        Assert.Equal(command.ProductId, _sagaData.ProductId);
        Assert.Equal(command.Quantity, _sagaData.Quantity);
        Assert.Equal(command.TotalPrice, _sagaData.TotalPrice);

        // Verify no StockCheckRequestedEvent is published by this specific handler's HandleStartAsync
        mockContext.Verify(c => c.Publish(It.IsAny<StockCheckRequestedEvent>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_StockAvailableEvent_PublishesOrderConfirmedEventAndCompletes()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid(); // SagaId can be different from OrderId
        _sagaData.OrderId = orderId; // Simulate saga data already initialized

        var stockEvent = new StockAvailableEvent { OrderId = orderId, ProductId = "Test1", Quantity = 5, SagaId = sagaId };

        var mockContext = new Mock<ISagaContext<StockAvailableEvent, OrderStockCheckSagaData>>();
        mockContext.Setup(c => c.Data).Returns(_sagaData);
        mockContext.Setup(c => c.SagaId).Returns(sagaId);
        // Setup context.Publish to complete successfully
        mockContext.Setup(c => c.Publish(It.IsAny<OrderConfirmedEvent>())).Returns(Task.CompletedTask);
        // Setup context.MarkAsComplete to complete successfully
        mockContext.Setup(c => c.MarkAsComplete<StockAvailableEvent>()).Returns(Task.CompletedTask);


        // Act
        await _handler.HandleAsync(stockEvent, mockContext.Object);

        // Assert
        // Verify that context.Publish was called with the correct OrderConfirmedEvent
        mockContext.Verify(c => c.Publish(It.Is<OrderConfirmedEvent>(e => e.OrderId == orderId)), Times.Once);
        // Verify that context.MarkAsComplete was called
        mockContext.Verify(c => c.MarkAsComplete<StockAvailableEvent>(), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_StockUnavailableEvent_PublishesOrderFailedEventAndFailsSaga()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();
        _sagaData.OrderId = orderId;

        var stockEvent = new StockUnavailableEvent { OrderId = orderId, ProductId = "Test2", Quantity = 10, Reason = "Out of stock", SagaId = sagaId };

        var mockContext = new Mock<ISagaContext<StockUnavailableEvent, OrderStockCheckSagaData>>();
        mockContext.Setup(c => c.Data).Returns(_sagaData);
        mockContext.Setup(c => c.SagaId).Returns(sagaId);
        // Setup context.Publish to complete successfully
        mockContext.Setup(c => c.Publish(It.IsAny<OrderCreationFailedEvent>())).Returns(Task.CompletedTask);
        // Setup context.MarkAsFailed to complete successfully
        mockContext.Setup(c => c.MarkAsFailed<StockUnavailableEvent>()).Returns(Task.CompletedTask);

        // Act
        await _handler.HandleAsync(stockEvent, mockContext.Object);

        // Assert
        // Verify that context.Publish was called with the correct OrderCreationFailedEvent
        mockContext.Verify(c => c.Publish(It.Is<OrderCreationFailedEvent>(e => e.OrderId == orderId && e.Reason == "Out of stock")), Times.Once);
        // Verify that context.MarkAsFailed was called
        mockContext.Verify(c => c.MarkAsFailed<StockUnavailableEvent>(), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_StockAvailableEvent_IncorrectOrderId_IgnoresEvent()
    {
        // Arrange
        var sagaOrderId = Guid.NewGuid();
        var eventOrderId = Guid.NewGuid(); // Different OrderId
        var sagaId = Guid.NewGuid();
        _sagaData.OrderId = sagaOrderId;

        var stockEvent = new StockAvailableEvent { OrderId = eventOrderId, ProductId = "Test1", Quantity = 5, SagaId = sagaId };

        var mockContext = new Mock<ISagaContext<StockAvailableEvent, OrderStockCheckSagaData>>();
        mockContext.Setup(c => c.Data).Returns(_sagaData);
        mockContext.Setup(c => c.SagaId).Returns(sagaId);

        // Act
        await _handler.HandleAsync(stockEvent, mockContext.Object);

        // Assert
        mockContext.Verify(c => c.Publish(It.IsAny<OrderConfirmedEvent>()), Times.Never);
        mockContext.Verify(c => c.MarkAsComplete<StockAvailableEvent>(), Times.Never);
    }

     [Fact]
    public async Task HandleAsync_StockUnavailableEvent_IncorrectOrderId_IgnoresEvent()
    {
        // Arrange
        var sagaOrderId = Guid.NewGuid();
        var eventOrderId = Guid.NewGuid(); // Different OrderId
        var sagaId = Guid.NewGuid();
        _sagaData.OrderId = sagaOrderId;

        var stockEvent = new StockUnavailableEvent { OrderId = eventOrderId, ProductId = "Test2", Quantity = 10, Reason = "Out of stock", SagaId = sagaId };

        var mockContext = new Mock<ISagaContext<StockUnavailableEvent, OrderStockCheckSagaData>>();
        mockContext.Setup(c => c.Data).Returns(_sagaData);
        mockContext.Setup(c => c.SagaId).Returns(sagaId);

        // Act
        await _handler.HandleAsync(stockEvent, mockContext.Object);

        // Assert
        mockContext.Verify(c => c.Publish(It.IsAny<OrderCreationFailedEvent>()), Times.Never);
        mockContext.Verify(c => c.MarkAsFailed<StockUnavailableEvent>(), Times.Never);
    }
}
