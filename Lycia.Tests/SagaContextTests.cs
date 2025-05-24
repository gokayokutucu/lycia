using Xunit; // Changed from MSTest
using Moq;
using System;
using System.Threading.Tasks;
using Lycia.Messaging;
using Lycia.Saga;
using Lycia.Saga.Abstractions; 
using Lycia.Saga.Enums; // Required for StepStatus
using Lycia.Saga.Extensions; // For ISagaIdGenerator

// Note: SagaStepFluent types (ReactiveSagaStepFluent, CoordinatedSagaStepFluent) are in Lycia.Saga namespace.

namespace Lycia.Tests
{
    // --- Test Stub Classes ---

    public class SagaContextTest_TestMessage : IMessage 
    { 
        public Guid MessageId { get; set; } = Guid.NewGuid(); 
        public DateTime Timestamp { get; set; } = DateTime.UtcNow; 
        public string ApplicationId { get; set; } = "TestApp";
    }
    public class SagaContextTest_TestCommand : SagaContextTest_TestMessage, ICommand { }
    public class SagaContextTest_TestEvent : SagaContextTest_TestMessage, IEvent { }
    
    // Correctly inherit from Lycia.Messaging.FailedEventBase
    public class SagaContextTest_FailedEvent : Lycia.Messaging.FailedEventBase 
    {
        public SagaContextTest_FailedEvent(string reason = "Test Failure") : base(reason) {}
        // Ensure it still satisfies IEvent if FailedEventBase doesn't make it obvious (it does via EventBase)
    }

    public class SagaContextTest_SagaData : SagaData { public string TestProperty { get; set; } }

    // [TestClass] removed for xUnit
    public class SagaContextTests
    {
        private readonly Mock<IEventBus> _mockEventBus;
        private readonly Mock<ISagaStore> _mockSagaStore;
        private readonly Mock<ISagaIdGenerator> _mockSagaIdGenerator;
        private readonly Guid _testSagaId;
        private readonly Guid _generatedSagaId;

        // Constructor used for setup in xUnit (formerly [TestInitialize])
        public SagaContextTests()
        {
            _mockEventBus = new Mock<IEventBus>();
            _mockSagaStore = new Mock<ISagaStore>();
            _mockSagaIdGenerator = new Mock<ISagaIdGenerator>();
            _testSagaId = Guid.NewGuid();
            _generatedSagaId = Guid.NewGuid();

            _mockSagaIdGenerator.Setup(g => g.Generate()).Returns(_generatedSagaId);
            _mockEventBus.Setup(eb => eb.Send(It.IsAny<ICommand>(), It.IsAny<Guid>())).Returns(Task.CompletedTask);
            _mockEventBus.Setup(eb => eb.Publish(It.IsAny<IEvent>(), It.IsAny<Guid>())).Returns(Task.CompletedTask);
            _mockSagaStore.Setup(ss => ss.LogStepAsync(It.IsAny<Guid>(), It.IsAny<Type>(), It.IsAny<Saga.Enums.StepStatus>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            _mockSagaStore.Setup(ss => ss.IsStepCompletedAsync(It.IsAny<Guid>(), It.IsAny<Type>())).Returns(Task.FromResult(false));
        }

        // --- Tests for SagaContext<TMessage> ---

        [Fact] // Changed from [TestMethod]
        public void SagaContext_WhenSagaIdIsEmpty_GeneratesSagaId()
        {
            var context = new SagaContext<SagaContextTest_TestMessage>(Guid.Empty, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            Assert.Equal(_generatedSagaId, context.SagaId); // Changed from Assert.AreEqual
            _mockSagaIdGenerator.Verify(g => g.Generate(), Times.Once);
        }

        [Fact] // Changed
        public void SagaContext_WhenSagaIdIsProvided_UsesProvidedSagaId()
        {
            var context = new SagaContext<SagaContextTest_TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            Assert.Equal(_testSagaId, context.SagaId); // Changed
            _mockSagaIdGenerator.Verify(g => g.Generate(), Times.Never);
        }

        [Fact] // Changed
        public async Task Send_CallsEventBusSend_WithCorrectParameters()
        {
            var context = new SagaContext<SagaContextTest_TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var command = new SagaContextTest_TestCommand();
            await context.Send(command);
            _mockEventBus.Verify(eb => eb.Send(command, _testSagaId), Times.Once);
        }

        [Fact] // Changed
        public async Task Publish_CallsEventBusPublish_WithCorrectParameters()
        {
            var context = new SagaContext<SagaContextTest_TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var testEvent = new SagaContextTest_TestEvent();
            await context.Publish(testEvent);
            _mockEventBus.Verify(eb => eb.Publish(testEvent, _testSagaId), Times.Once);
        }
        
        [Fact] // Changed
        public void PublishWithTracking_CallsEventBusPublish_AndReturnsReactiveSagaStepFluent()
        {
            var context = new SagaContext<SagaContextTest_TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var testEvent = new SagaContextTest_TestEvent();
            _mockEventBus.Setup(eb => eb.Publish(testEvent, _testSagaId)).Returns(Task.CompletedTask);
            var fluentStep = context.PublishWithTracking(testEvent);
            _mockEventBus.Verify(eb => eb.Publish(testEvent, _testSagaId), Times.Once);
            Assert.NotNull(fluentStep); // Changed
            Assert.IsAssignableFrom<ReactiveSagaStepFluent<SagaContextTest_TestEvent, SagaContextTest_TestMessage>>(fluentStep); // Changed
        }

        [Fact] // Changed
        public void SendWithTracking_CallsEventBusSend_AndReturnsReactiveSagaStepFluent()
        {
            var context = new SagaContext<SagaContextTest_TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var command = new SagaContextTest_TestCommand();
            _mockEventBus.Setup(eb => eb.Send(command, _testSagaId)).Returns(Task.CompletedTask);
            var fluentStep = context.SendWithTracking(command);
            _mockEventBus.Verify(eb => eb.Send(command, _testSagaId), Times.Once);
            Assert.NotNull(fluentStep); // Changed
            Assert.IsAssignableFrom<ReactiveSagaStepFluent<SagaContextTest_TestCommand, SagaContextTest_TestMessage>>(fluentStep); // Changed
        }

        [Fact] // Changed
        public async Task Compensate_CallsEventBusPublish_WithCorrectParameters()
        {
            var context = new SagaContext<SagaContextTest_TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var failedEvent = new SagaContextTest_FailedEvent();
            await context.Compensate(failedEvent);
            _mockEventBus.Verify(eb => eb.Publish(failedEvent, _testSagaId), Times.Once);
        }

        [Fact] // Changed
        public async Task MarkAsComplete_CallsSagaStoreLogStepAsync_WithCorrectParameters()
        {
            var context = new SagaContext<SagaContextTest_TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            await context.MarkAsComplete<SagaContextTest_TestEvent>();
            _mockSagaStore.Verify(ss => ss.LogStepAsync(_testSagaId, typeof(SagaContextTest_TestEvent), StepStatus.Completed, null), Times.Once);
        }

        [Fact] // Changed
        public async Task MarkAsFailed_CallsSagaStoreLogStepAsync_WithCorrectParameters()
        {
            var context = new SagaContext<SagaContextTest_TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            await context.MarkAsFailed<SagaContextTest_TestEvent>();
            _mockSagaStore.Verify(ss => ss.LogStepAsync(_testSagaId, typeof(SagaContextTest_TestEvent), StepStatus.Failed, null), Times.Once);
        }
        
        [Fact] // Changed
        public async Task MarkAsCompensated_CallsSagaStoreLogStepAsync_WithCorrectParameters()
        {
            var context = new SagaContext<SagaContextTest_TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            await context.MarkAsCompensated<SagaContextTest_TestEvent>();
            _mockSagaStore.Verify(ss => ss.LogStepAsync(_testSagaId, typeof(SagaContextTest_TestEvent), StepStatus.Compensated, null), Times.Once);
        }
        
        [Fact] // Changed
        public async Task MarkAsCompensationFailed_CallsSagaStoreLogStepAsync_WithCorrectParameters()
        {
            var context = new SagaContext<SagaContextTest_TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            await context.MarkAsCompensationFailed<SagaContextTest_TestEvent>();
            _mockSagaStore.Verify(ss => ss.LogStepAsync(_testSagaId, typeof(SagaContextTest_TestEvent), StepStatus.CompensationFailed, null), Times.Once);
        }

        [Fact] // Changed
        public async Task IsAlreadyCompleted_CallsSagaStoreIsStepCompletedAsync_AndReturnsResult()
        {
            var context = new SagaContext<SagaContextTest_TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            _mockSagaStore.Setup(ss => ss.IsStepCompletedAsync(_testSagaId, typeof(SagaContextTest_TestEvent))).Returns(Task.FromResult(true));
            var result = await context.IsAlreadyCompleted<SagaContextTest_TestEvent>();
            Assert.True(result); // Changed
            _mockSagaStore.Verify(ss => ss.IsStepCompletedAsync(_testSagaId, typeof(SagaContextTest_TestEvent)), Times.Once);
        }

        // --- Tests for ReactiveSagaStepFluent (via SagaContext<TMessage>) ---
        [Fact] // Changed
        public async Task ReactiveSagaStepFluent_ThenMarkAsComplete_UsesInitialMessageTypeForMarking()
        {
            var context = new SagaContext<SagaContextTest_TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var stepEvent = new SagaContextTest_TestEvent(); 
            var fluentStep = context.PublishWithTracking(stepEvent);
            await fluentStep.ThenMarkAsComplete();
            _mockSagaStore.Verify(s => s.LogStepAsync(_testSagaId, typeof(SagaContextTest_TestMessage), StepStatus.Completed, null), Times.Once);
        }

        [Fact] // Changed
        public async Task ReactiveSagaStepFluent_ThenMarkAsFailed_UsesInitialMessageTypeForMarking()
        {
            var context = new SagaContext<SagaContextTest_TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var stepEvent = new SagaContextTest_TestEvent(); 
            var fluentStep = context.PublishWithTracking(stepEvent);
            await fluentStep.ThenMarkAsFailed(new FailResponse());
            _mockSagaStore.Verify(s => s.LogStepAsync(_testSagaId, typeof(SagaContextTest_TestMessage), StepStatus.Failed, null), Times.Once);
        }

        // --- Tests for SagaContext<TMessage, TSagaData> ---
        [Fact] // Changed
        public void SagaContextWithData_InitializesBaseAndDataCorrectly()
        {
            var testData = new SagaContextTest_SagaData { TestProperty = "Sample" };
            var context = new SagaContext<SagaContextTest_TestMessage, SagaContextTest_SagaData>(
                _testSagaId, testData, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            Assert.Equal(_testSagaId, context.SagaId); // Changed
            Assert.Same(testData, context.Data);      // Changed
            _mockSagaIdGenerator.Verify(g => g.Generate(), Times.Never); 
        }

        [Fact] // Changed
        public void PublishWithTracking_ForContextWithData_ReturnsCoordinatedSagaStepFluent()
        {
            var testData = new SagaContextTest_SagaData();
            var parentContext = new SagaContext<SagaContextTest_TestMessage, SagaContextTest_SagaData>(
                _testSagaId, testData, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var stepEvent = new SagaContextTest_TestEvent();
            _mockEventBus.Setup(eb => eb.Publish(stepEvent, _testSagaId)).Returns(Task.CompletedTask);
            var fluentStep = parentContext.PublishWithTracking(stepEvent);
            _mockEventBus.Verify(eb => eb.Publish(stepEvent, _testSagaId), Times.Once); 
            Assert.NotNull(fluentStep); // Changed
            Assert.IsAssignableFrom<CoordinatedSagaStepFluent<SagaContextTest_TestEvent, SagaContextTest_SagaData>>(fluentStep); // Changed
        }

        [Fact] // Changed
        public void SendWithTracking_ForContextWithData_ReturnsCoordinatedSagaStepFluent()
        {
            var testData = new SagaContextTest_SagaData();
            var parentContext = new SagaContext<SagaContextTest_TestMessage, SagaContextTest_SagaData>(
                _testSagaId, testData, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var stepCommand = new SagaContextTest_TestCommand();
            _mockEventBus.Setup(eb => eb.Send(stepCommand, _testSagaId)).Returns(Task.CompletedTask);
            var fluentStep = parentContext.SendWithTracking(stepCommand);
            _mockEventBus.Verify(eb => eb.Send(stepCommand, _testSagaId), Times.Once); 
            Assert.NotNull(fluentStep); // Changed
            Assert.IsAssignableFrom<CoordinatedSagaStepFluent<SagaContextTest_TestCommand, SagaContextTest_SagaData>>(fluentStep); // Changed
        }

        // --- Tests for StepSpecificSagaContextAdapter (via CoordinatedSagaStepFluent) ---
        [Fact] // Changed
        public async Task CoordinatedSagaStepFluent_ThenMarkAsComplete_UsesAdapterAndMarksTStepCorrectly()
        {
            var testData = new SagaContextTest_SagaData();
            var parentContext = new SagaContext<SagaContextTest_TestMessage, SagaContextTest_SagaData>(
                _testSagaId, testData, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var stepEvent = new SagaContextTest_TestEvent(); 
            var fluentStep = parentContext.PublishWithTracking(stepEvent);
            await fluentStep.ThenMarkAsComplete();
            _mockSagaStore.Verify(s => s.LogStepAsync(_testSagaId, typeof(SagaContextTest_TestEvent), StepStatus.Completed, null), Times.Once);
        }

        [Fact] // Changed
        public async Task CoordinatedSagaStepFluent_Adapter_PublishWithTracking_DelegatesCorrectly()
        {
            var testData = new SagaContextTest_SagaData();
            var parentContext = new SagaContext<SagaContextTest_TestMessage, SagaContextTest_SagaData>(
                _testSagaId, testData, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var firstStepEvent = new SagaContextTest_TestEvent(); 
            var coordinatedFluentStep1 = parentContext.PublishWithTracking(firstStepEvent);
            var secondStepEvent = new SagaContextTest_TestEvent { MessageId = Guid.NewGuid() };
            _mockEventBus.ResetCalls(); 
            _mockEventBus.Setup(eb => eb.Publish(secondStepEvent, _testSagaId)).Returns(Task.CompletedTask);
            
            var coordinatedFluentStep2 = coordinatedFluentStep1.Context.PublishWithTracking(secondStepEvent);
            
            _mockEventBus.Verify(eb => eb.Publish(secondStepEvent, _testSagaId), Times.Once);
            Assert.NotNull(coordinatedFluentStep2); // Changed
            Assert.IsAssignableFrom<CoordinatedSagaStepFluent<SagaContextTest_TestEvent, SagaContextTest_SagaData>>(coordinatedFluentStep2); // Changed
        }
    }
}
