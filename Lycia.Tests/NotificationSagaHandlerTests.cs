using System;
using System.Threading.Tasks;
using Lycia.Saga.Abstractions;
using Moq;
using Sample.Shared.Messages.Events;
using Sample.Shared.Messages.Sagas;
using Xunit;

namespace Lycia.Tests
{
    public class NotificationSagaHandlerTests
    {
        private Mock<IPublishContext> _mockPublishContext;

        public NotificationSagaHandlerTests()
        {
            _mockPublishContext = new Mock<IPublishContext>();
            _mockPublishContext.Setup(pc => pc.ThenMarkAsComplete()).Returns(Task.CompletedTask);
        }

        // Test Case 1: For OrderConfirmationNotificationSagaHandler
        [Fact]
        public async Task HandleAsync_ForShipmentDispatched_Should_UpdateSagaData_And_Publish_NotificationSentEvent_OrderConfirmed()
        {
            // Arrange
            var orderId = Guid.NewGuid();
            var shipmentDispatchedEvent = new ShipmentDispatchedEvent 
            { 
                OrderId = orderId, 
                TrackingNumber = "TRK123", 
                DispatchDate = DateTime.UtcNow 
            };
            var sagaData = new LyciaSagaData { OrderId = orderId, UserEmail = "test@example.com" };

            var mockSagaContext = new Mock<ISagaContext<ShipmentDispatchedEvent, LyciaSagaData>>();
            mockSagaContext.Setup(sc => sc.SagaData).Returns(sagaData);
            mockSagaContext.Setup(sc => sc.PublishWithTracking(It.IsAny<NotificationSentEvent>()))
                           .Returns(_mockPublishContext.Object)
                           .Verifiable();

            var handler = new OrderConfirmationNotificationSagaHandler();
            handler.Initialize(mockSagaContext.Object);

            // Act
            await handler.HandleAsync(shipmentDispatchedEvent);

            // Assert
            Assert.Equal("NotificationSent_OrderConfirmed", sagaData.OrderStatus);
            mockSagaContext.Verify(sc => sc.PublishWithTracking(It.Is<NotificationSentEvent>(e => 
                e.OrderId == orderId &&
                e.NotificationType == "OrderConfirmed"
            )), Times.Once);
            _mockPublishContext.Verify(pc => pc.ThenMarkAsComplete(), Times.Once);
        }

        // Test Case 2: For SagaFailureNotificationHandler
        [Fact]
        public async Task HandleAsync_ForLyciaSagaFailedEvent_Should_UpdateSagaData_And_Publish_NotificationSentEvent_SagaFailure()
        {
            // Arrange
            var orderId = Guid.NewGuid();
            var lyciaSagaFailedEvent = new LyciaSagaFailedEvent 
            { 
                OrderId = orderId, 
                FailedStep = "PaymentProcessing", 
                FailureReason = "Insufficient funds" 
            };
            var sagaData = new LyciaSagaData { OrderId = orderId, UserEmail = "user@example.com" };

            var mockSagaContext = new Mock<ISagaContext<LyciaSagaFailedEvent, LyciaSagaData>>();
            mockSagaContext.Setup(sc => sc.SagaData).Returns(sagaData);
            mockSagaContext.Setup(sc => sc.PublishWithTracking(It.IsAny<NotificationSentEvent>()))
                           .Returns(_mockPublishContext.Object)
                           .Verifiable();

            var handler = new SagaFailureNotificationHandler();
            handler.Initialize(mockSagaContext.Object);

            // Act
            await handler.HandleAsync(lyciaSagaFailedEvent);

            // Assert
            Assert.Equal($"NotificationSent_SagaFailure_{lyciaSagaFailedEvent.FailedStep}", sagaData.OrderStatus);
            mockSagaContext.Verify(sc => sc.PublishWithTracking(It.Is<NotificationSentEvent>(e => 
                e.OrderId == orderId &&
                e.NotificationType == $"SagaFailure_{lyciaSagaFailedEvent.FailedStep}"
            )), Times.Once);
            _mockPublishContext.Verify(pc => pc.ThenMarkAsComplete(), Times.Once);
        }
    }
}
