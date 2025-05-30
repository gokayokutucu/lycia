using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lycia.Saga.Abstractions;
using Moq;
using Sample.Shared.Messages.Commands; // For OrderItem
using Sample.Shared.Messages.Events;
using Sample.Shared.Messages.Sagas;
using Xunit;

namespace Lycia.Tests
{
    public class InventorySagaHandlerTests
    {
        private Mock<IPublishContext> _mockPublishContext;

        public InventorySagaHandlerTests()
        {
            _mockPublishContext = new Mock<IPublishContext>();
            _mockPublishContext.Setup(pc => pc.ThenMarkAsComplete()).Returns(Task.CompletedTask);
            _mockPublishContext.Setup(pc => pc.ThenMarkAsFaulted<LyciaSagaStartedEvent>()).Returns(Task.CompletedTask);
             // Add for InventoryUpdatedEvent if compensation for PaymentSagaHandler is tested here
            _mockPublishContext.Setup(pc => pc.ThenMarkAsFaulted<InventoryUpdatedEvent>()).Returns(Task.CompletedTask);
        }

        // Test Case 1: HandleAsync_WhenInventoryUpdateSucceeds_Should_UpdateSagaData_And_Publish_InventoryUpdatedEvent
        [Fact]
        public async Task HandleAsync_WhenInventoryUpdateSucceeds_Should_UpdateSagaData_And_Publish_InventoryUpdatedEvent()
        {
            // Arrange
            var orderId = Guid.NewGuid(); // Ensure this OrderId does not contain "bad"
            var sagaStartedEvent = new LyciaSagaStartedEvent { OrderId = orderId, UserId = Guid.NewGuid(), TotalPrice = 50m };
            var sagaData = new LyciaSagaData { OrderId = orderId, Items = new List<OrderItem>() };

            var mockSagaContext = new Mock<ISagaContext<LyciaSagaStartedEvent, LyciaSagaData>>();
            mockSagaContext.Setup(sc => sc.SagaData).Returns(sagaData);
            mockSagaContext.Setup(sc => sc.PublishWithTracking(It.IsAny<InventoryUpdatedEvent>()))
                           .Returns(_mockPublishContext.Object)
                           .Verifiable();

            var handler = new InventorySagaHandler();
            handler.Initialize(mockSagaContext.Object);

            // Act
            await handler.HandleAsync(sagaStartedEvent);

            // Assert
            Assert.Equal("InventoryUpdated", sagaData.OrderStatus);
            mockSagaContext.Verify(sc => sc.PublishWithTracking(It.Is<InventoryUpdatedEvent>(e => e.OrderId == orderId)), Times.Once);
            _mockPublishContext.Verify(pc => pc.ThenMarkAsComplete(), Times.Once);
        }

        // Test Case 2: HandleAsync_WhenInventoryUpdateFails_Should_UpdateSagaData_And_Publish_InventoryUpdateFailedEvent
        [Fact]
        public async Task HandleAsync_WhenInventoryUpdateFails_Should_UpdateSagaData_And_Publish_InventoryUpdateFailedEvent()
        {
            // Arrange
            // Ensure OrderId contains "bad" to trigger failure logic in handler
            var orderId = Guid.Parse($"bad-{Guid.NewGuid().ToString().Substring(4)}"); 
            var sagaStartedEvent = new LyciaSagaStartedEvent { OrderId = orderId, UserId = Guid.NewGuid(), TotalPrice = 75m };
            var sagaData = new LyciaSagaData { 
                OrderId = orderId, 
                Items = new List<OrderItem> { new OrderItem { ProductId = Guid.NewGuid(), Quantity = 1}} 
            };

            var mockSagaContext = new Mock<ISagaContext<LyciaSagaStartedEvent, LyciaSagaData>>();
            mockSagaContext.Setup(sc => sc.SagaData).Returns(sagaData);
            mockSagaContext.Setup(sc => sc.PublishWithTracking(It.IsAny<InventoryUpdateFailedEvent>()))
                           .Returns(_mockPublishContext.Object)
                           .Verifiable();

            var handler = new InventorySagaHandler();
            handler.Initialize(mockSagaContext.Object);

            // Act
            await handler.HandleAsync(sagaStartedEvent);

            // Assert
            Assert.Equal("InventoryUpdateFailed", sagaData.OrderStatus);
            Assert.False(string.IsNullOrEmpty(sagaData.FailureReason));
            mockSagaContext.Verify(sc => sc.PublishWithTracking(It.Is<InventoryUpdateFailedEvent>(e => 
                e.OrderId == orderId &&
                e.Reason == sagaData.FailureReason &&
                e.FailedProductIds != null && e.FailedProductIds.Any()
            )), Times.Once);
            _mockPublishContext.Verify(pc => pc.ThenMarkAsComplete(), Times.Once); // As per current handler logic
        }

        // Test Case 3: CompensateAsync_ForPaymentFailedEvent_WhenCompensationSucceeds_Should_UpdateSagaData_And_MarkAsCompensated
        [Fact]
        public async Task CompensateAsync_ForPaymentFailedEvent_WhenCompensationSucceeds_Should_UpdateSagaData_And_MarkAsCompensated()
        {
            // Arrange
            var orderId = Guid.NewGuid();
            var paymentFailedEvent = new PaymentFailedEvent { OrderId = orderId, Reason = "Test payment failure" };
            var sagaData = new LyciaSagaData { OrderId = orderId };

            var mockSagaContext = new Mock<ISagaContext<PaymentFailedEvent, LyciaSagaData>>();
            mockSagaContext.Setup(sc => sc.SagaData).Returns(sagaData);
            // For MarkAsCompensated, it's a method on ISagaContext, not IPublishContext
            mockSagaContext.Setup(sc => sc.MarkAsCompensated<LyciaSagaStartedEvent>()).Returns(Task.CompletedTask).Verifiable();

            var handler = new InventorySagaHandler();
            // Compensation handlers don't use the base class's Initialize method for context typically,
            // as the context is passed directly to CompensateAsync.

            // Act
            await handler.CompensateAsync(paymentFailedEvent, mockSagaContext.Object);

            // Assert
            Assert.Equal("InventoryCompensatedAfterPaymentFailure", sagaData.OrderStatus);
            Assert.Contains(paymentFailedEvent.Reason, sagaData.FailureReason);
            mockSagaContext.Verify(sc => sc.MarkAsCompensated<LyciaSagaStartedEvent>(), Times.Once);
        }

        // Test Case 4: CompensateAsync_ForPaymentFailedEvent_WhenCompensationFails_Should_Publish_LyciaSagaFailedEvent_And_MarkAsFaulted
        [Fact]
        public async Task CompensateAsync_ForPaymentFailedEvent_WhenCompensationFails_Should_Publish_LyciaSagaFailedEvent_And_MarkAsFaulted()
        {
            // Arrange
            var orderId = Guid.NewGuid();
            var paymentFailedEvent = new PaymentFailedEvent { OrderId = orderId, Reason = "Original payment failure" };
            var sagaData = new LyciaSagaData { OrderId = orderId };
            var compensationExceptionMessage = "Simulated DB error during compensation";

            var mockSagaContext = new Mock<ISagaContext<PaymentFailedEvent, LyciaSagaData>>();
            mockSagaContext.Setup(sc => sc.SagaData).Returns(sagaData);
            
            // Simulate failure during compensation itself by making a context call throw an exception
            mockSagaContext.Setup(sc => sc.MarkAsCompensated<LyciaSagaStartedEvent>())
                           .ThrowsAsync(new Exception(compensationExceptionMessage));
            
            mockSagaContext.Setup(sc => sc.PublishWithTracking(It.IsAny<LyciaSagaFailedEvent>()))
                           .Returns(_mockPublishContext.Object) // Use the shared mock IPublishContext
                           .Verifiable();
            // Note: MarkAsCompensationFailed is not explicitly called in the handler's catch block if ThenMarkAsFaulted is used.
            // The handler currently calls ThenMarkAsFaulted. If MarkAsCompensationFailed was also required, the mock setup would change.

            var handler = new InventorySagaHandler();

            // Act
            await handler.CompensateAsync(paymentFailedEvent, mockSagaContext.Object);

            // Assert
            Assert.Equal("InventoryCompensationFailedAfterPaymentFailure", sagaData.OrderStatus);
            Assert.Contains(compensationExceptionMessage, sagaData.FailureReason);
            Assert.Contains(paymentFailedEvent.Reason, sagaData.FailureReason);

            mockSagaContext.Verify(sc => sc.PublishWithTracking(It.Is<LyciaSagaFailedEvent>(e =>
                e.OrderId == orderId &&
                e.FailedStep == "InventoryCompensationAfterPaymentFailure" &&
                e.FailureReason == sagaData.FailureReason
            )), Times.Once);

            _mockPublishContext.Verify(pc => pc.ThenMarkAsFaulted<LyciaSagaStartedEvent>(), Times.Once);
            // Verify MarkAsCompensated was attempted
            mockSagaContext.Verify(sc => sc.MarkAsCompensated<LyciaSagaStartedEvent>(), Times.Once); 
        }

        // Test Case 5: CompensateAsync_ForShipmentFailedEvent_WhenCompensationSucceeds_Should_UpdateSagaData_And_MarkAsCompensated
        [Fact]
        public async Task CompensateAsync_ForShipmentFailedEvent_WhenCompensationSucceeds_Should_UpdateSagaData_And_MarkAsCompensated()
        {
            // Arrange
            var orderId = Guid.NewGuid();
            var shipmentFailedEvent = new ShipmentFailedEvent { OrderId = orderId, Reason = "Test shipment failure" };
            var sagaData = new LyciaSagaData { OrderId = orderId };

            var mockSagaContext = new Mock<ISagaContext<ShipmentFailedEvent, LyciaSagaData>>();
            mockSagaContext.Setup(sc => sc.SagaData).Returns(sagaData);
            mockSagaContext.Setup(sc => sc.MarkAsCompensated<LyciaSagaStartedEvent>()).Returns(Task.CompletedTask).Verifiable();

            var handler = new InventorySagaHandler();

            // Act
            await handler.CompensateAsync(shipmentFailedEvent, mockSagaContext.Object);

            // Assert
            Assert.Equal("InventoryCompensatedAfterShipmentFailure", sagaData.OrderStatus);
            Assert.Contains(shipmentFailedEvent.Reason, sagaData.FailureReason);
            mockSagaContext.Verify(sc => sc.MarkAsCompensated<LyciaSagaStartedEvent>(), Times.Once);
        }

        // Test Case 6: CompensateAsync_ForShipmentFailedEvent_WhenCompensationFails_Should_Publish_LyciaSagaFailedEvent_And_MarkAsFaulted
        [Fact]
        public async Task CompensateAsync_ForShipmentFailedEvent_WhenCompensationFails_Should_Publish_LyciaSagaFailedEvent_And_MarkAsFaulted()
        {
            // Arrange
            var orderId = Guid.NewGuid();
            var shipmentFailedEvent = new ShipmentFailedEvent { OrderId = orderId, Reason = "Original shipment failure" };
            var sagaData = new LyciaSagaData { OrderId = orderId };
            var compensationExceptionMessage = "Simulated error during inventory compensation for shipment failure";

            var mockSagaContext = new Mock<ISagaContext<ShipmentFailedEvent, LyciaSagaData>>();
            mockSagaContext.Setup(sc => sc.SagaData).Returns(sagaData);
            mockSagaContext.Setup(sc => sc.MarkAsCompensated<LyciaSagaStartedEvent>())
                           .ThrowsAsync(new Exception(compensationExceptionMessage));
            
            mockSagaContext.Setup(sc => sc.PublishWithTracking(It.IsAny<LyciaSagaFailedEvent>()))
                           .Returns(_mockPublishContext.Object)
                           .Verifiable();

            var handler = new InventorySagaHandler();

            // Act
            await handler.CompensateAsync(shipmentFailedEvent, mockSagaContext.Object);

            // Assert
            Assert.Equal("InventoryCompensationFailedAfterShipmentFailure", sagaData.OrderStatus);
            Assert.Contains(compensationExceptionMessage, sagaData.FailureReason);
            Assert.Contains(shipmentFailedEvent.Reason, sagaData.FailureReason);

            mockSagaContext.Verify(sc => sc.PublishWithTracking(It.Is<LyciaSagaFailedEvent>(e =>
                e.OrderId == orderId &&
                e.FailedStep == "InventoryCompensationAfterShipmentFailure" &&
                e.FailureReason == sagaData.FailureReason
            )), Times.Once);

            _mockPublishContext.Verify(pc => pc.ThenMarkAsFaulted<LyciaSagaStartedEvent>(), Times.Once);
            mockSagaContext.Verify(sc => sc.MarkAsCompensated<LyciaSagaStartedEvent>(), Times.Once); // Verify it was attempted
        }
    }
}
