using System;
using System.Threading;
using System.Threading.Tasks;
using Lycia.Saga.Abstractions; // For IEventBus
using Lycia.Messaging; // For IMessage
using Moq;
using InventoryService.Application.Features.Stocks.Consumers; // The consumer
using InventoryService.Application.Features.Stocks.Notifications; // The MediatR notification wrapper
using InventoryService.Application.Contracts.Persistence; // For IInventoryRepository
using Sample.Shared.Messages.Events;
using Xunit;

public class InventoryServiceConsumerTests
{
    private readonly Mock<IEventBus> _mockEventBus;
    private readonly Mock<IInventoryRepository> _mockInventoryRepository; // Mocked but not strictly used by current consumer logic
    private readonly OrderCreationInitiatedConsumer _consumer;

    public InventoryServiceConsumerTests()
    {
        _mockEventBus = new Mock<IEventBus>();
        _mockInventoryRepository = new Mock<IInventoryRepository>();

        _consumer = new OrderCreationInitiatedConsumer(_mockEventBus.Object, _mockInventoryRepository.Object);
    }

    [Fact]
    public async Task Handle_WhenQuantityIsLow_PublishesStockAvailableEvent()
    {
        // Arrange
        var originalEvent = new OrderCreationInitiatedEvent
        {
            OrderId = Guid.NewGuid(),
            ProductId = "P123",
            Quantity = 5, // Quantity < 50, should result in StockAvailableEvent
            SagaId = Guid.NewGuid()
        };
        var notification = new OrderCreationInitiatedMediatRNotification(originalEvent);

        // No setup needed for _mockInventoryRepository as consumer's current logic is:
        // bool isAvailable = notification.Quantity < 50;

        _mockEventBus.Setup(eb => eb.Publish(It.IsAny<StockAvailableEvent>(), It.IsAny<Guid?>()))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Handle(notification, CancellationToken.None);

        // Assert
        _mockEventBus.Verify(eb => eb.Publish(It.Is<StockAvailableEvent>(e =>
            e.OrderId == originalEvent.OrderId &&
            e.ProductId == originalEvent.ProductId &&
            e.Quantity == originalEvent.Quantity),
            originalEvent.SagaId), Times.Once);
        _mockEventBus.Verify(eb => eb.Publish(It.IsAny<StockUnavailableEvent>(), It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenQuantityIsHigh_PublishesStockUnavailableEvent()
    {
        // Arrange
        var originalEvent = new OrderCreationInitiatedEvent
        {
            OrderId = Guid.NewGuid(),
            ProductId = "P456",
            Quantity = 75, // Quantity >= 50, should result in StockUnavailableEvent
            SagaId = Guid.NewGuid()
        };
        var notification = new OrderCreationInitiatedMediatRNotification(originalEvent);

        // No setup needed for _mockInventoryRepository due to current consumer logic

        _mockEventBus.Setup(eb => eb.Publish(It.IsAny<StockUnavailableEvent>(), It.IsAny<Guid?>()))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Handle(notification, CancellationToken.None);

        // Assert
        _mockEventBus.Verify(eb => eb.Publish(It.Is<StockUnavailableEvent>(e =>
            e.OrderId == originalEvent.OrderId &&
            e.ProductId == originalEvent.ProductId &&
            e.Quantity == originalEvent.Quantity &&
            !string.IsNullOrEmpty(e.Reason)), // Consumer generates a reason
            originalEvent.SagaId), Times.Once);
        _mockEventBus.Verify(eb => eb.Publish(It.IsAny<StockAvailableEvent>(), It.IsAny<Guid?>()), Times.Never);
    }
}
