using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Handlers;
using Moq;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Events;
using Sample.Shared.Messages.Sagas;
using Xunit;

namespace Lycia.Tests
{
    public class StartLyciaSagaHandlerTests
    {
        [Fact]
        public async Task HandleStartAsync_Should_Initialize_SagaData_And_Publish_LyciaSagaStartedEvent()
        {
            // Arrange
            var command = new StartLyciaSagaCommand
            {
                OrderId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                TotalPrice = 100.50m,
                Items = new List<OrderItem> { new OrderItem { ProductId = Guid.NewGuid(), Quantity = 2 } },
                CardDetails = "1234-5678-9012-3456",
                ShippingAddress = "123 Test St",
                UserEmail = "test@example.com"
            };

            var mockPublishContext = new Mock<IPublishContext>();
            mockPublishContext.Setup(pc => pc.ThenMarkAsComplete()).Returns(Task.CompletedTask).Verifiable();

            var sagaData = new LyciaSagaData(); // Real SagaData instance that the handler will populate

            var mockSagaContext = new Mock<ISagaContext<StartLyciaSagaCommand, LyciaSagaData>>();
            mockSagaContext.Setup(sc => sc.SagaData).Returns(sagaData);
            mockSagaContext.Setup(sc => sc.PublishWithTracking(It.IsAny<LyciaSagaStartedEvent>()))
                           .Returns(mockPublishContext.Object) // Return the mock IPublishContext
                           .Verifiable();

            var handler = new StartLyciaSagaHandler();

            // The StartReactiveSagaHandler base class has an Initialize method that sets the Context.
            // We need to call this to set our mock context on the handler.
            // The actual SagaContext object is created by the dispatcher in real flow, here we mock its interface.
            // The base handler's Context property has a public setter, so we can also set it directly if Initialize is protected/internal.
            // Let's assume direct setting for simplicity if Initialize is not straightforwardly public for test setup.
            // However, StartReactiveSagaHandler's Context is { get; protected set; }
            // and Initialize(ISagaContext<TCommand, TData> context) is public.
            handler.Initialize(mockSagaContext.Object);


            // Act
            await handler.HandleStartAsync(command);

            // Assert
            // Verify SagaData population
            Assert.Equal(command.OrderId, sagaData.OrderId);
            Assert.Equal(command.UserId, sagaData.UserId);
            Assert.Equal(command.TotalPrice, sagaData.TotalPrice);
            Assert.Equal(command.Items, sagaData.Items);
            Assert.Equal(command.CardDetails, sagaData.CardDetails);
            Assert.Equal(command.ShippingAddress, sagaData.ShippingAddress);
            Assert.Equal(command.UserEmail, sagaData.UserEmail);
            Assert.Equal("SagaStarted", sagaData.OrderStatus);

            // Verify that PublishWithTracking was called with the correct event type and data
            mockSagaContext.Verify(sc => sc.PublishWithTracking(It.Is<LyciaSagaStartedEvent>(e =>
                e.OrderId == command.OrderId &&
                e.UserId == command.UserId &&
                e.TotalPrice == command.TotalPrice
            )), Times.Once);

            // Verify that ThenMarkAsComplete was called on the result of PublishWithTracking
            mockPublishContext.Verify(pc => pc.ThenMarkAsComplete(), Times.Once);
        }
    }
}
