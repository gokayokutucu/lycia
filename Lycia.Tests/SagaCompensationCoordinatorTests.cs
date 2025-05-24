using Xunit; // Changed
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
using Lycia.Saga.Extensions; 
using Lycia.Infrastructure.Compensating;

namespace Lycia.Tests
{
    // --- Test Stub Classes for Messages ---
    public class CoordinatorTest_CompensatableMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public string Data { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string ApplicationId { get; set; } = "CoordinatorTestsApp";
    }

    public class CoordinatorTest_AnotherCompensatableMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public int Value { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string ApplicationId { get; set; } = "CoordinatorTestsApp";
    }

    // --- Mock Handler Implementation ---
    public class CoordinatorTest_MockSagaCompensationHandler<TMessage> : ISagaCompensationHandler<TMessage>
        where TMessage : IMessage
    {
        public bool InitializeCalled { get; private set; }
        public bool CompensateAsyncCalled { get; private set; }
        public TMessage ReceivedMessage { get; private set; }
        public ISagaContext<TMessage> Context { get; private set; }
        public Action<TMessage> CompensateAction { get; set; } 

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

    // [TestClass] removed
    public class SagaCompensationCoordinatorTests
    {
        private readonly Mock<ISagaStore> _mockSagaStore;
        private readonly Mock<ISagaIdGenerator> _mockSagaIdGenerator;
        private readonly Mock<IEventBus> _mockEventBus;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly SagaCompensationCoordinator _coordinator;
        private readonly Guid _testSagaId;

        // Constructor for xUnit setup (formerly [TestInitialize])
        public SagaCompensationCoordinatorTests()
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
            // Ensure the messageType is from this test assembly or a known one for Type.GetType to work via AssemblyQualifiedName
            var typeName = messageType.AssemblyQualifiedName;
            if (typeName == null && messageType.IsGenericType) // Workaround for generic types if AssemblyQualifiedName is null
            {
                typeName = $"{messageType.Namespace}.{messageType.Name}[{string.Join(",", messageType.GetGenericArguments().Select(ga => ga.AssemblyQualifiedName))}], {messageType.Assembly.FullName}";
            }


            return new SagaStepMetadata
            {
                Status = status,
                MessageTypeName = typeName ?? messageType.FullName, // Fallback if still null
                MessagePayload = JsonSerializer.Serialize(payload, messageType), // Serialize with the concrete type
                RecordedAt = recordedAt,
                ApplicationId = "TestApp" 
            };
        }

        [Fact] // Changed
        public async Task TriggerCompensationAsync_NoSteps_CompletesGracefully()
        {
            _mockSagaStore.Setup(s => s.GetSagaStepsAsync(_testSagaId))
                          .ReturnsAsync(new Dictionary<string, SagaStepMetadata>());
            await _coordinator.TriggerCompensationAsync(_testSagaId, "anyFailedStep");
            _mockSagaStore.Verify(s => s.GetSagaStepsAsync(_testSagaId), Times.Once);
            _mockServiceProvider.Verify(sp => sp.GetServices(It.IsAny<Type>()), Times.Never);
        }

        [Fact] // Changed
        public async Task TriggerCompensationAsync_SkipsFailedStepAndNonCompletedSteps()
        {
            var failedStepName = typeof(CoordinatorTest_CompensatableMessage).ToSagaStepName() + "_Failed"; 
            var compensatableMsg = new CoordinatorTest_CompensatableMessage { Data = "CompData" };
            var compensatableStepType = typeof(CoordinatorTest_CompensatableMessage);

            var steps = new Dictionary<string, SagaStepMetadata>
            {
                { failedStepName, CreateStepMetadata(typeof(object), new {}, DateTime.UtcNow, StepStatus.Failed) }, 
                { "InProgressStep", CreateStepMetadata(compensatableStepType, compensatableMsg, DateTime.UtcNow.AddMinutes(-1), StepStatus.InProgress) },
                { "CompensatedStep", CreateStepMetadata(compensatableStepType, compensatableMsg, DateTime.UtcNow.AddMinutes(-2), StepStatus.Compensated) }
            };
            _mockSagaStore.Setup(s => s.GetSagaStepsAsync(_testSagaId)).ReturnsAsync(steps);
            SetupHandlerResolution<CoordinatorTest_CompensatableMessage>(compensatableStepType);

            await _coordinator.TriggerCompensationAsync(_testSagaId, failedStepName);
            _mockServiceProvider.Verify(sp => sp.GetServices(It.IsAny<Type>()), Times.Never);
        }

        [Fact] // Changed
        public async Task TriggerCompensationAsync_SkipsUnresolvableMessageType()
        {
            var steps = new Dictionary<string, SagaStepMetadata>
            {
                { "UnresolvableTypeStep", new SagaStepMetadata {
                    Status = StepStatus.Completed,
                    MessageTypeName = "NonExistent.Type, NonExistentAssembly", 
                    MessagePayload = "{}",
                    RecordedAt = DateTime.UtcNow.AddMinutes(-1)
                }}
            };
            _mockSagaStore.Setup(s => s.GetSagaStepsAsync(_testSagaId)).ReturnsAsync(steps);
            await _coordinator.TriggerCompensationAsync(_testSagaId, "anyFailedStep");
            _mockServiceProvider.Verify(sp => sp.GetServices(It.IsAny<Type>()), Times.Never);
        }

        [Fact] // Changed
        public async Task TriggerCompensationAsync_ProcessesStepsInReverseChronologicalOrder()
        {
            var msg1 = new CoordinatorTest_CompensatableMessage { Data = "Msg1" };
            var type1 = typeof(CoordinatorTest_CompensatableMessage);
            var stepName1 = type1.ToSagaStepName() + "1";

            var msg2 = new CoordinatorTest_AnotherCompensatableMessage { Value = 123 };
            var type2 = typeof(CoordinatorTest_AnotherCompensatableMessage);
            var stepName2 = type2.ToSagaStepName() + "2";

            var steps = new Dictionary<string, SagaStepMetadata>
            {
                { stepName1, CreateStepMetadata(type1, msg1, DateTime.UtcNow.AddMinutes(-2)) }, 
                { stepName2, CreateStepMetadata(type2, msg2, DateTime.UtcNow.AddMinutes(-1)) }  
            };
            _mockSagaStore.Setup(s => s.GetSagaStepsAsync(_testSagaId)).ReturnsAsync(steps);

            var handler1 = new CoordinatorTest_MockSagaCompensationHandler<CoordinatorTest_CompensatableMessage>();
            var handler2 = new CoordinatorTest_MockSagaCompensationHandler<CoordinatorTest_AnotherCompensatableMessage>();
            SetupHandlerResolution(type1, handler1);
            SetupHandlerResolution(type2, handler2);
            
            var callOrder = new List<string>();
            handler1.CompensateAction = _ => callOrder.Add(stepName1);
            handler2.CompensateAction = _ => callOrder.Add(stepName2);

            await _coordinator.TriggerCompensationAsync(_testSagaId, "nonExistentFailedStep");

            Assert.Equal(2, callOrder.Count); // Changed
            Assert.Equal(stepName2, callOrder[0]); // Changed
            Assert.Equal(stepName1, callOrder[1]); // Changed
        }

        [Fact] // Changed
        public async Task TriggerCompensationAsync_NoHandlersFoundForStep_ProceedsGracefully()
        {
            var msg = new CoordinatorTest_CompensatableMessage { Data = "TestData" };
            var msgType = typeof(CoordinatorTest_CompensatableMessage);
            var stepName = msgType.ToSagaStepName();
            var steps = new Dictionary<string, SagaStepMetadata> { { stepName, CreateStepMetadata(msgType, msg, DateTime.UtcNow.AddMinutes(-1)) } };
            _mockSagaStore.Setup(s => s.GetSagaStepsAsync(_testSagaId)).ReturnsAsync(steps);
            SetupHandlerResolution<CoordinatorTest_CompensatableMessage>(msgType); 

            await _coordinator.TriggerCompensationAsync(_testSagaId, "failedStep");
            _mockServiceProvider.Verify(sp => sp.GetServices(typeof(ISagaCompensationHandler<>).MakeGenericType(msgType)), Times.Once);
        }

        [Fact] // Changed
        public async Task TriggerCompensationAsync_HandlerCalled_InitializeAndCompensateAsyncCalled()
        {
            var msg = new CoordinatorTest_CompensatableMessage { Data = "CompensateThis" };
            var msgType = typeof(CoordinatorTest_CompensatableMessage);
            var stepName = msgType.ToSagaStepName();
            var steps = new Dictionary<string, SagaStepMetadata> { { stepName, CreateStepMetadata(msgType, msg, DateTime.UtcNow.AddMinutes(-1)) } };
            _mockSagaStore.Setup(s => s.GetSagaStepsAsync(_testSagaId)).ReturnsAsync(steps);

            var mockHandler = new CoordinatorTest_MockSagaCompensationHandler<CoordinatorTest_CompensatableMessage>();
            SetupHandlerResolution(msgType, mockHandler);

            await _coordinator.TriggerCompensationAsync(_testSagaId, "someFailedStep");

            Assert.True(mockHandler.InitializeCalled); // Changed
            Assert.True(mockHandler.CompensateAsyncCalled); // Changed
            Assert.NotNull(mockHandler.Context); // Changed
            Assert.Equal(_testSagaId, mockHandler.Context.SagaId); // Changed
            Assert.Equal(msg.Data, mockHandler.ReceivedMessage.Data); // Changed
        }

        [Fact] // Changed
        public async Task TriggerCompensationAsync_HandlerThrowsException_LogsCompensationFailed()
        {
            var msg = new CoordinatorTest_CompensatableMessage { Data = "WillFail" };
            var msgType = typeof(CoordinatorTest_CompensatableMessage);
            var stepName = msgType.ToSagaStepName();
            var steps = new Dictionary<string, SagaStepMetadata> { { stepName, CreateStepMetadata(msgType, msg, DateTime.UtcNow.AddMinutes(-1)) } };
            _mockSagaStore.Setup(s => s.GetSagaStepsAsync(_testSagaId)).ReturnsAsync(steps);

            var mockHandler = new CoordinatorTest_MockSagaCompensationHandler<CoordinatorTest_CompensatableMessage>
            {
                CompensateAction = _ => throw new InvalidOperationException("Compensation failed!")
            };
            SetupHandlerResolution(msgType, mockHandler);

            await _coordinator.TriggerCompensationAsync(_testSagaId, "someOtherFailedStep");

            Assert.True(mockHandler.InitializeCalled); // Changed
            Assert.True(mockHandler.CompensateAsyncCalled); // Changed
            _mockSagaStore.Verify(s => s.LogStepAsync(
                _testSagaId,
                msgType,
                StepStatus.CompensationFailed,
                It.Is<CoordinatorTest_CompensatableMessage>(m => m.Data == msg.Data) 
            ), Times.Once);
        }
    }
}
