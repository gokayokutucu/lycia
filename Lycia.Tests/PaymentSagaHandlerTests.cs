using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lycia.Saga.Abstractions;
using Moq;
using Sample.Shared.Messages.Events;
using Sample.Shared.Messages.Sagas;
using Xunit;

namespace Lycia.Tests
{
    public class PaymentSagaHandlerTests
    {
        private Mock<IPublishContext> _mockPublishContext;

        public PaymentSagaHandlerTests()
        {
            _mockPublishContext = new Mock<IPublishContext>();
            _mockPublishContext.Setup(pc => pc.ThenMarkAsComplete()).Returns(Task.CompletedTask);
            _mockPublishContext.Setup(pc => pc.ThenMarkAsFaulted<InventoryUpdatedEvent>()).Returns(Task.CompletedTask);
        }

        // Test Case 1: HandleAsync_WhenPaymentSucceeds_Should_UpdateSagaData_And_Publish_PaymentProcessedEvent
        [Fact]
        public async Task HandleAsync_WhenPaymentSucceeds_Should_UpdateSagaData_And_Publish_PaymentProcessedEvent()
        {
            // Arrange
            var orderId = Guid.NewGuid();
            var inventoryUpdatedEvent = new InventoryUpdatedEvent { OrderId = orderId };
            var sagaData = new LyciaSagaData { OrderId = orderId, CardDetails = "valid-card" }; // Ensure success

            var mockSagaContext = new Mock<ISagaContext<InventoryUpdatedEvent, LyciaSagaData>>();
            mockSagaContext.Setup(sc => sc.SagaData).Returns(sagaData);
            mockSagaContext.Setup(sc => sc.PublishWithTracking(It.IsAny<PaymentProcessedEvent>()))
                           .Returns(_mockPublishContext.Object)
                           .Verifiable();

            var handler = new PaymentSagaHandler();
            handler.Initialize(mockSagaContext.Object);

            // Act
            await handler.HandleAsync(inventoryUpdatedEvent);

            // Assert
            Assert.Equal("PaymentProcessed", sagaData.OrderStatus);
            Assert.NotEqual(Guid.Empty, sagaData.PaymentId);
            mockSagaContext.Verify(sc => sc.PublishWithTracking(It.Is<PaymentProcessedEvent>(e => 
                e.OrderId == orderId && 
                e.PaymentId == sagaData.PaymentId
            )), Times.Once);
            _mockPublishContext.Verify(pc => pc.ThenMarkAsComplete(), Times.Once);
        }

        // Test Case 2: HandleAsync_WhenPaymentFails_Should_UpdateSagaData_And_Publish_PaymentFailedEvent
        [Fact]
        public async Task HandleAsync_WhenPaymentFails_Should_UpdateSagaData_And_Publish_PaymentFailedEvent()
        {
            // Arrange
            var orderId = Guid.NewGuid();
            var inventoryUpdatedEvent = new InventoryUpdatedEvent { OrderId = orderId };
            // Configure CardDetails to trigger failure logic in handler
            var sagaData = new LyciaSagaData { OrderId = orderId, CardDetails = "fail-card-details" }; 

            var mockSagaContext = new Mock<ISagaContext<InventoryUpdatedEvent, LyciaSagaData>>();
            mockSagaContext.Setup(sc => sc.SagaData).Returns(sagaData);
            mockSagaContext.Setup(sc => sc.PublishWithTracking(It.IsAny<PaymentFailedEvent>()))
                           .Returns(_mockPublishContext.Object)
                           .Verifiable();

            var handler = new PaymentSagaHandler();
            handler.Initialize(mockSagaContext.Object);

            // Act
            await handler.HandleAsync(inventoryUpdatedEvent);

            // Assert
            Assert.Equal("PaymentFailed", sagaData.OrderStatus);
            Assert.False(string.IsNullOrEmpty(sagaData.FailureReason));
            mockSagaContext.Verify(sc => sc.PublishWithTracking(It.Is<PaymentFailedEvent>(e => 
                e.OrderId == orderId &&
                e.Reason == sagaData.FailureReason
            )), Times.Once);
            _mockPublishContext.Verify(pc => pc.ThenMarkAsComplete(), Times.Once); // As per current handler logic
        }

        // Test Case 3: CompensateAsync_ForShipmentFailedEvent_WhenCompensationSucceeds_Should_UpdateSagaData_And_MarkAsCompensated
        [Fact]
        public async Task CompensateAsync_ForShipmentFailedEvent_WhenCompensationSucceeds_Should_UpdateSagaData_And_MarkAsCompensated()
        {
            // Arrange
            var orderId = Guid.NewGuid();
            var shipmentFailedEvent = new ShipmentFailedEvent { OrderId = orderId, Reason = "Test shipment failure" };
            var sagaData = new LyciaSagaData { OrderId = orderId, PaymentId = Guid.NewGuid() };

            var mockSagaContext = new Mock<ISagaContext<ShipmentFailedEvent, LyciaSagaData>>();
            mockSagaContext.Setup(sc => sc.SagaData).Returns(sagaData);
            mockSagaContext.Setup(sc => sc.MarkAsCompensated<InventoryUpdatedEvent>()).Returns(Task.CompletedTask).Verifiable();

            var handler = new PaymentSagaHandler();

            // Act
            await handler.CompensateAsync(shipmentFailedEvent, mockSagaContext.Object);

            // Assert
            Assert.Equal("PaymentRefundedAfterShipmentFailure", sagaData.OrderStatus);
            Assert.Contains(shipmentFailedEvent.Reason, sagaData.FailureReason);
            Assert.Contains(sagaData.PaymentId.ToString(), sagaData.FailureReason);
            mockSagaContext.Verify(sc => sc.MarkAsCompensated<InventoryUpdatedEvent>(), Times.Once);
        }

        // Test Case 4: CompensateAsync_ForShipmentFailedEvent_WhenCompensationFails_Should_Publish_LyciaSagaFailedEvent_And_MarkAsFaulted
        [Fact]
        public async Task CompensateAsync_ForShipmentFailedEvent_WhenCompensationFails_Should_Publish_LyciaSagaFailedEvent_And_MarkAsFaulted()
        {
            // Arrange
            var orderId = Guid.NewGuid();
            var shipmentFailedEvent = new ShipmentFailedEvent { OrderId = orderId, Reason = "Original shipment failure" };
            var sagaData = new LyciaSagaData { OrderId = orderId, PaymentId = Guid.NewGuid() };
            var compensationExceptionMessage = "Simulated error during payment refund";

            var mockSagaContext = new Mock<ISagaContext<ShipmentFailedEvent, LyciaSagaData>>();
            mockSagaContext.Setup(sc => sc.SagaData).Returns(sagaData);
            mockSagaContext.Setup(sc => sc.MarkAsCompensated<InventoryUpdatedEvent>())
                           .ThrowsAsync(new Exception(compensationExceptionMessage));
            
            mockSagaContext.Setup(sc => sc.PublishWithTracking(It.IsAny<LyciaSagaFailedEvent>()))
                           .Returns(_mockPublishContext.Object)
                           .Verifiable();

            var handler = new PaymentSagaHandler();

            // Act
            await handler.CompensateAsync(shipmentFailedEvent, mockSagaContext.Object);

            // Assert
            Assert.Equal("PaymentRefundFailedAfterShipmentFailure", sagaData.OrderStatus);
            Assert.Contains(compensationExceptionMessage, sagaData.FailureReason);
            Assert.Contains(shipmentFailedEvent.Reason, sagaData.FailureReason);

            mockSagaContext.Verify(sc => sc.PublishWithTracking(It.Is<LyciaSagaFailedEvent>(e =>
                e.OrderId == orderId &&
                e.FailedStep == "PaymentRefundFailedAfterShipmentFailure" &&
                e.FailureReason == sagaData.FailureReason
            )), Times.Once);

            _mockPublishContext.Verify(pc => pc.ThenMarkAsFaulted<InventoryUpdatedEvent>(), Times.Once);
            mockSagaContext.Verify(sc => sc.MarkAsCompensated<InventoryUpdatedEvent>(), Times.Once); // Verify it was attempted
        }
    }
}
