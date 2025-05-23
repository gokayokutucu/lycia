using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Lycia.Messaging;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Enums;
using Lycia.Saga.Extensions; // For ToSagaStepName and TryResolveSagaStepType
using Lycia.Infrastructure.Compensating;

namespace Lycia.Tests
{
    // --- Test Stub Classes for Messages ---
    public class CompensatableTestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public string Data { get; set; }
    }

    public class AnotherCompensatableTestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public int Value { get; set; }
    }

    // --- Mock Handler Implementation ---
    public class MockSagaCompensationHandler<TMessage> : ISagaCompensationHandler<TMessage>
        where TMessage : IMessage
    {
        public bool InitializeCalled { get; private set; }
        public bool CompensateAsyncCalled { get; private set; }
        public TMessage ReceivedMessage { get; private set; }
        public ISagaContext<TMessage> Context { get; private set; }
        public Action<TMessage> CompensateAction { get; set; } // To simulate success or throw exception

        public void Initialize(ISagaContext<TMessage> context)
        {
            InitializeCalled = true;
            Context = context;
        }

        public Task CompensateAsync(TMessage message)
        {
            CompensateAsyncCalled = true;
            ReceivedMessage = message;
            CompensateAction?.Invoke(message);
            return Task.CompletedTask;
        }
    }

    [TestClass]
    public class SagaCompensationCoordinatorTests
    {
        private Mock<ISagaStore> _mockSagaStore;
        private Mock<ISagaIdGenerator> _mockSagaIdGenerator;
        private Mock<IEventBus> _mockEventBus;
        private Mock<IServiceProvider> _mockServiceProvider;
        private SagaCompensationCoordinator _coordinator;
        private Guid _testSagaId;

        [TestInitialize]
        public void Setup()
        {
            _mockSagaStore = new Mock<ISagaStore>();
            _mockSagaIdGenerator = new Mock<ISagaIdGenerator>();
            _mockEventBus = new Mock<IEventBus>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _testSagaId = Guid.NewGuid();

            _coordinator = new SagaCompensationCoordinator(
                _mockSagaStore.Object,
                _mockSagaIdGenerator.Object,
                _mockEventBus.Object,
                _mockServiceProvider.Object
            );
        }

        private void SetupHandlerResolution<TMessage>(Type messageType, params ISagaCompensationHandler<TMessage>[] handlers)
            where TMessage : IMessage
        {
            var handlerInterfaceType = typeof(ISagaCompensationHandler<>).MakeGenericType(messageType);
            _mockServiceProvider.Setup(sp => sp.GetServices(handlerInterfaceType))
                                .Returns(handlers.Cast<object>().ToList());
        }
        
        private SagaStepMetadata CreateStepMetadata(Type messageType, object payload, DateTime recordedAt, StepStatus status = StepStatus.Completed)
        {
            return new SagaStepMetadata
            {
                Status = status,
                MessageTypeName = messageType.AssemblyQualifiedName, // Crucial for TryResolveSagaStepType
                MessagePayload = JsonSerializer.Serialize(payload, messageType),
                RecordedAt = recordedAt,
                ApplicationId = "TestApp" 
            };
        }

        [TestMethod]
        public async Task TriggerCompensationAsync_NoSteps_CompletesGracefully()
        {
            // Arrange
            _mockSagaStore.Setup(s => s.GetSagaStepsAsync(_testSagaId))
                          .ReturnsAsync(new Dictionary<string, SagaStepMetadata>());

            // Act
            await _coordinator.TriggerCompensationAsync(_testSagaId, "anyFailedStep");

            // Assert
            _mockSagaStore.Verify(s => s.GetSagaStepsAsync(_testSagaId), Times.Once);
            _mockServiceProvider.Verify(sp => sp.GetServices(It.IsAny<Type>()), Times.Never);
        }

        [TestMethod]
        public async Task TriggerCompensationAsync_SkipsFailedStepAndNonCompletedSteps()
        {
            // Arrange
            var failedStepName = typeof(CompensatableTestMessage).ToSagaStepName() + "_Failed"; // Ensure it's unique if type is reused
            var compensatableMsg = new CompensatableTestMessage { Data = "CompData" };
            var compensatableStepType = typeof(CompensatableTestMessage);

            var steps = new Dictionary<string, SagaStepMetadata>
            {
                { failedStepName, CreateStepMetadata(typeof(object), new {}, DateTime.UtcNow, StepStatus.Failed) }, // The actual failed step
                { "InProgressStep", CreateStepMetadata(compensatableStepType, compensatableMsg, DateTime.UtcNow.AddMinutes(-1), StepStatus.InProgress) },
                { "CompensatedStep", CreateStepMetadata(compensatableStepType, compensatableMsg, DateTime.UtcNow.AddMinutes(-2), StepStatus.Compensated) }
            };
            _mockSagaStore.Setup(s => s.GetSagaStepsAsync(_testSagaId)).ReturnsAsync(steps);
            
            // Setup no handlers, as none should be called
            SetupHandlerResolution<CompensatableTestMessage>(compensatableStepType);


            // Act
            await _coordinator.TriggerCompensationAsync(_testSagaId, failedStepName);

            // Assert
            _mockServiceProvider.Verify(sp => sp.GetServices(It.IsAny<Type>()), Times.Never);
        }

        [TestMethod]
        public async Task TriggerCompensationAsync_SkipsUnresolvableMessageType()
        {
            // Arrange
            var steps = new Dictionary<string, SagaStepMetadata>
            {
                { "UnresolvableTypeStep", new SagaStepMetadata {
                    Status = StepStatus.Completed,
                    MessageTypeName = "NonExistent.Type, NonExistentAssembly", // This won't resolve
                    MessagePayload = "{}",
                    RecordedAt = DateTime.UtcNow.AddMinutes(-1)
                }}
            };
            _mockSagaStore.Setup(s => s.GetSagaStepsAsync(_testSagaId)).ReturnsAsync(steps);

            // Act
            await _coordinator.TriggerCompensationAsync(_testSagaId, "anyFailedStep");

            // Assert
            _mockServiceProvider.Verify(sp => sp.GetServices(It.IsAny<Type>()), Times.Never);
        }

        [TestMethod]
        public async Task TriggerCompensationAsync_ProcessesStepsInReverseChronologicalOrder()
        {
            // Arrange
            var msg1 = new CompensatableTestMessage { Data = "Msg1" };
            var type1 = typeof(CompensatableTestMessage);
            var stepName1 = type1.ToSagaStepName() + "1";

            var msg2 = new AnotherCompensatableTestMessage { Value = 123 };
            var type2 = typeof(AnotherCompensatableTestMessage);
            var stepName2 = type2.ToSagaStepName() + "2";

            var steps = new Dictionary<string, SagaStepMetadata>
            {
                // Step 1 happened first, Step 2 happened later (more recent)
                { stepName1, CreateStepMetadata(type1, msg1, DateTime.UtcNow.AddMinutes(-2)) }, 
                { stepName2, CreateStepMetadata(type2, msg2, DateTime.UtcNow.AddMinutes(-1)) }  
            };
            _mockSagaStore.Setup(s => s.GetSagaStepsAsync(_testSagaId)).ReturnsAsync(steps);

            var handler1 = new MockSagaCompensationHandler<CompensatableTestMessage>();
            var handler2 = new MockSagaCompensationHandler<AnotherCompensatableTestMessage>();
            SetupHandlerResolution(type1, handler1);
            SetupHandlerResolution(type2, handler2);
            
            var callOrder = new List<string>();
            handler1.CompensateAction = _ => callOrder.Add(stepName1);
            handler2.CompensateAction = _ => callOrder.Add(stepName2);

            // Act
            await _coordinator.TriggerCompensationAsync(_testSagaId, "nonExistentFailedStep");

            // Assert
            Assert.AreEqual(2, callOrder.Count);
            Assert.AreEqual(stepName2, callOrder[0], "Step 2 (more recent) should be compensated first.");
            Assert.AreEqual(stepName1, callOrder[1], "Step 1 (older) should be compensated second.");
        }

        [TestMethod]
        public async Task TriggerCompensationAsync_NoHandlersFoundForStep_ProceedsGracefully()
        {
            // Arrange
            var msg = new CompensatableTestMessage { Data = "TestData" };
            var msgType = typeof(CompensatableTestMessage);
            var stepName = msgType.ToSagaStepName();
            var steps = new Dictionary<string, SagaStepMetadata>
            {
                { stepName, CreateStepMetadata(msgType, msg, DateTime.UtcNow.AddMinutes(-1)) }
            };
            _mockSagaStore.Setup(s => s.GetSagaStepsAsync(_testSagaId)).ReturnsAsync(steps);
            SetupHandlerResolution<CompensatableTestMessage>(msgType); // No handlers registered

            // Act
            await _coordinator.TriggerCompensationAsync(_testSagaId, "failedStep");

            // Assert
            _mockServiceProvider.Verify(sp => sp.GetServices(typeof(ISagaCompensationHandler<>).MakeGenericType(msgType)), Times.Once);
            // No error should be thrown.
        }

        [TestMethod]
        public async Task TriggerCompensationAsync_HandlerCalled_InitializeAndCompensateAsyncCalled()
        {
            // Arrange
            var msg = new CompensatableTestMessage { Data = "CompensateThis" };
            var msgType = typeof(CompensatableTestMessage);
            var stepName = msgType.ToSagaStepName();
            var steps = new Dictionary<string, SagaStepMetadata>
            {
                { stepName, CreateStepMetadata(msgType, msg, DateTime.UtcNow.AddMinutes(-1)) }
            };
            _mockSagaStore.Setup(s => s.GetSagaStepsAsync(_testSagaId)).ReturnsAsync(steps);

            var mockHandler = new MockSagaCompensationHandler<CompensatableTestMessage>();
            SetupHandlerResolution(msgType, mockHandler);

            // Act
            await _coordinator.TriggerCompensationAsync(_testSagaId, "someFailedStep");

            // Assert
            Assert.IsTrue(mockHandler.InitializeCalled, "Initialize should be called.");
            Assert.IsTrue(mockHandler.CompensateAsyncCalled, "CompensateAsync should be called.");
            Assert.IsNotNull(mockHandler.Context, "Context should not be null.");
            Assert.AreEqual(_testSagaId, mockHandler.Context.SagaId, "Context should have correct SagaId.");
            Assert.AreEqual(msg.Data, mockHandler.ReceivedMessage.Data, "Handler received deserialized message.");
        }

        [TestMethod]
        public async Task TriggerCompensationAsync_HandlerThrowsException_LogsCompensationFailed()
        {
            // Arrange
            var msg = new CompensatableTestMessage { Data = "WillFail" };
            var msgType = typeof(CompensatableTestMessage);
            var stepName = msgType.ToSagaStepName();
            var steps = new Dictionary<string, SagaStepMetadata>
            {
                { stepName, CreateStepMetadata(msgType, msg, DateTime.UtcNow.AddMinutes(-1)) }
            };
            _mockSagaStore.Setup(s => s.GetSagaStepsAsync(_testSagaId)).ReturnsAsync(steps);

            var mockHandler = new MockSagaCompensationHandler<CompensatableTestMessage>
            {
                CompensateAction = _ => throw new InvalidOperationException("Compensation failed!")
            };
            SetupHandlerResolution(msgType, mockHandler);

            // Act
            await _coordinator.TriggerCompensationAsync(_testSagaId, "someOtherFailedStep");

            // Assert
            Assert.IsTrue(mockHandler.InitializeCalled);
            Assert.IsTrue(mockHandler.CompensateAsyncCalled);
            _mockSagaStore.Verify(s => s.LogStepAsync(
                _testSagaId,
                msgType,
                StepStatus.CompensationFailed,
                It.Is<CompensatableTestMessage>(m => m.Data == msg.Data) // Verify payload
            ), Times.Once);
        }
    }
}
