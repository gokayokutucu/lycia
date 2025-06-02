using System;
using System.Threading.Tasks;
using Lycia.Saga.Abstractions;
using Moq;
using Sample.Shared.Messages.Events;
using Sample.Shared.Messages.Sagas;
using Xunit;

namespace Lycia.Tests
{
    public class ShipmentSagaHandlerTests
    {
        private Mock<IPublishContext> _mockPublishContext;

        public ShipmentSagaHandlerTests()
        {
            _mockPublishContext = new Mock<IPublishContext>();
            _mockPublishContext.Setup(pc => pc.ThenMarkAsComplete()).Returns(Task.CompletedTask);
            // No ThenMarkAsFaulted needed here as ShipmentSagaHandler's HandleAsync failures
            // are expected to publish events that cause other handlers to compensate,
            // rather than faulting its own triggering event (PaymentProcessedEvent) directly in these tests.
        }

        // Test Case 1: HandleAsync_WhenShipmentSucceeds_Should_UpdateSagaData_And_Publish_ShipmentDispatchedEvent
        [Fact]
        public async Task HandleAsync_WhenShipmentSucceeds_Should_UpdateSagaData_And_Publish_ShipmentDispatchedEvent()
        {
            // Arrange
            var orderId = Guid.NewGuid();
            var paymentProcessedEvent = new PaymentProcessedEvent { OrderId = orderId, PaymentId = Guid.NewGuid(), PaymentDate = DateTime.UtcNow };
            var sagaData = new LyciaSagaData { OrderId = orderId, ShippingAddress = "valid address" }; // Ensure success

            var mockSagaContext = new Mock<ISagaContext<PaymentProcessedEvent, LyciaSagaData>>();
            mockSagaContext.Setup(sc => sc.SagaData).Returns(sagaData);
            mockSagaContext.Setup(sc => sc.PublishWithTracking(It.IsAny<ShipmentDispatchedEvent>()))
                           .Returns(_mockPublishContext.Object)
                           .Verifiable();

            var handler = new ShipmentSagaHandler();
            handler.Initialize(mockSagaContext.Object);

            // Act
            await handler.HandleAsync(paymentProcessedEvent);

            // Assert
            Assert.Equal("ShipmentDispatched", sagaData.OrderStatus);
            Assert.False(string.IsNullOrEmpty(sagaData.ShipmentTrackingNumber));
            Assert.StartsWith("TRK-", sagaData.ShipmentTrackingNumber);
            mockSagaContext.Verify(sc => sc.PublishWithTracking(It.Is<ShipmentDispatchedEvent>(e =>
                e.OrderId == orderId &&
                e.TrackingNumber == sagaData.ShipmentTrackingNumber
            )), Times.Once);
            _mockPublishContext.Verify(pc => pc.ThenMarkAsComplete(), Times.Once);
        }

        // Test Case 2: HandleAsync_WhenShipmentFails_Should_UpdateSagaData_And_Publish_ShipmentFailedEvent
        [Fact]
        public async Task HandleAsync_WhenShipmentFails_Should_UpdateSagaData_And_Publish_ShipmentFailedEvent()
        {
            // Arrange
            var orderId = Guid.NewGuid();
            var paymentProcessedEvent = new PaymentProcessedEvent { OrderId = orderId, PaymentId = Guid.NewGuid(), PaymentDate = DateTime.UtcNow };
            // Configure ShippingAddress to trigger failure logic in handler
            var sagaData = new LyciaSagaData { OrderId = orderId, ShippingAddress = "invalid-address-for-failure" };

            var mockSagaContext = new Mock<ISagaContext<PaymentProcessedEvent, LyciaSagaData>>();
            mockSagaContext.Setup(sc => sc.SagaData).Returns(sagaData);
            mockSagaContext.Setup(sc => sc.PublishWithTracking(It.IsAny<ShipmentFailedEvent>()))
                           .Returns(_mockPublishContext.Object)
                           .Verifiable();

            var handler = new ShipmentSagaHandler();
            handler.Initialize(mockSagaContext.Object);

            // Act
            await handler.HandleAsync(paymentProcessedEvent);

            // Assert
            Assert.Equal("ShipmentFailed", sagaData.OrderStatus);
            Assert.False(string.IsNullOrEmpty(sagaData.FailureReason));
            mockSagaContext.Verify(sc => sc.PublishWithTracking(It.Is<ShipmentFailedEvent>(e =>
                e.OrderId == orderId &&
                e.Reason == sagaData.FailureReason
            )), Times.Once);
            _mockPublishContext.Verify(pc => pc.ThenMarkAsComplete(), Times.Once); // As per current handler logic
        }
    }
}
