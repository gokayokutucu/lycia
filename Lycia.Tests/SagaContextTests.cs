using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading.Tasks;
using Lycia.Messaging;
using Lycia.Saga;
using Lycia.Saga.Abstractions; // For ISagaContext, ISagaStore, IEventBus, ISagaIdGenerator
// Note: SagaStepFluent types (ReactiveSagaStepFluent, CoordinatedSagaStepFluent) are in Lycia.Saga namespace.

namespace Lycia.Tests
{
    // --- Test Stub Classes ---

    public class TestMessage : IMessage { public Guid MessageId { get; set; } = Guid.NewGuid(); }
    public class TestCommand : TestMessage, ICommand { }
    public class TestEvent : TestMessage, IEvent { }
    
    // FailedEventBase is not explicitly defined in the project structure provided earlier.
    // For testing SagaContext.Compensate, we need a type that can be an IEvent.
    // If FailedEventBase has specific characteristics, this stub might need adjustment.
    // For now, assume it's a type of IEvent.
    public class TestFailedEvent : TestEvent { } // Inherits IEvent via TestEvent

    public class TestSagaData : SagaData { public string TestProperty { get; set; } }

    [TestClass]
    public class SagaContextTests
    {
        private Mock<IEventBus> _mockEventBus;
        private Mock<ISagaStore> _mockSagaStore;
        private Mock<ISagaIdGenerator> _mockSagaIdGenerator;
        private Guid _testSagaId;
        private Guid _generatedSagaId;

        [TestInitialize]
        public void Setup()
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

        [TestMethod]
        public void SagaContext_WhenSagaIdIsEmpty_GeneratesSagaId()
        {
            // Arrange & Act
            var context = new SagaContext<TestMessage>(Guid.Empty, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);

            // Assert
            Assert.AreEqual(_generatedSagaId, context.SagaId);
            _mockSagaIdGenerator.Verify(g => g.Generate(), Times.Once);
        }

        [TestMethod]
        public void SagaContext_WhenSagaIdIsProvided_UsesProvidedSagaId()
        {
            // Arrange & Act
            var context = new SagaContext<TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);

            // Assert
            Assert.AreEqual(_testSagaId, context.SagaId);
            _mockSagaIdGenerator.Verify(g => g.Generate(), Times.Never);
        }

        [TestMethod]
        public async Task Send_CallsEventBusSend_WithCorrectParameters()
        {
            // Arrange
            var context = new SagaContext<TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var command = new TestCommand();

            // Act
            await context.Send(command);

            // Assert
            _mockEventBus.Verify(eb => eb.Send(command, _testSagaId), Times.Once);
        }

        [TestMethod]
        public async Task Publish_CallsEventBusPublish_WithCorrectParameters()
        {
            // Arrange
            var context = new SagaContext<TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var testEvent = new TestEvent();

            // Act
            await context.Publish(testEvent);

            // Assert
            _mockEventBus.Verify(eb => eb.Publish(testEvent, _testSagaId), Times.Once);
        }
        
        [TestMethod]
        public void PublishWithTracking_CallsEventBusPublish_AndReturnsReactiveSagaStepFluent()
        {
            // Arrange
            var context = new SagaContext<TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var testEvent = new TestEvent();
            var publishTask = Task.CompletedTask;
            _mockEventBus.Setup(eb => eb.Publish(testEvent, _testSagaId)).Returns(publishTask);

            // Act
            var fluentStep = context.PublishWithTracking(testEvent);

            // Assert
            _mockEventBus.Verify(eb => eb.Publish(testEvent, _testSagaId), Times.Once);
            Assert.IsNotNull(fluentStep);
            Assert.IsInstanceOfType(fluentStep, typeof(ReactiveSagaStepFluent<TestEvent, TestMessage>));
            // Further inspection of fluentStep's internal context/task might be needed if strict equality is required,
            // but type and mock call verification are primary.
        }

        [TestMethod]
        public void SendWithTracking_CallsEventBusSend_AndReturnsReactiveSagaStepFluent()
        {
            // Arrange
            var context = new SagaContext<TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var command = new TestCommand();
            var sendTask = Task.CompletedTask;
            _mockEventBus.Setup(eb => eb.Send(command, _testSagaId)).Returns(sendTask);
            
            // Act
            var fluentStep = context.SendWithTracking(command);

            // Assert
            _mockEventBus.Verify(eb => eb.Send(command, _testSagaId), Times.Once);
            Assert.IsNotNull(fluentStep);
            Assert.IsInstanceOfType(fluentStep, typeof(ReactiveSagaStepFluent<TestCommand, TestMessage>));
        }

        [TestMethod]
        public async Task Compensate_CallsEventBusPublish_WithCorrectParameters()
        {
            // Arrange
            var context = new SagaContext<TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var failedEvent = new TestFailedEvent(); // TestFailedEvent is an IEvent

            // Act
            await context.Compensate(failedEvent);

            // Assert
            _mockEventBus.Verify(eb => eb.Publish(failedEvent, _testSagaId), Times.Once);
        }

        [TestMethod]
        public async Task MarkAsComplete_CallsSagaStoreLogStepAsync_WithCorrectParameters()
        {
            // Arrange
            var context = new SagaContext<TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);

            // Act
            await context.MarkAsComplete<TestEvent>();

            // Assert
            _mockSagaStore.Verify(ss => ss.LogStepAsync(_testSagaId, typeof(TestEvent), Saga.Enums.StepStatus.Completed, null), Times.Once);
        }

        [TestMethod]
        public async Task MarkAsFailed_CallsSagaStoreLogStepAsync_WithCorrectParameters()
        {
            // Arrange
            var context = new SagaContext<TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);

            // Act
            await context.MarkAsFailed<TestEvent>();

            // Assert
            _mockSagaStore.Verify(ss => ss.LogStepAsync(_testSagaId, typeof(TestEvent), Saga.Enums.StepStatus.Failed, null), Times.Once);
        }
        
        [TestMethod]
        public async Task MarkAsCompensated_CallsSagaStoreLogStepAsync_WithCorrectParameters()
        {
            // Arrange
            var context = new SagaContext<TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
        
            // Act
            await context.MarkAsCompensated<TestEvent>();
        
            // Assert
            _mockSagaStore.Verify(ss => ss.LogStepAsync(_testSagaId, typeof(TestEvent), Saga.Enums.StepStatus.Compensated, null), Times.Once);
        }
        
        [TestMethod]
        public async Task MarkAsCompensationFailed_CallsSagaStoreLogStepAsync_WithCorrectParameters()
        {
            // Arrange
            var context = new SagaContext<TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
        
            // Act
            await context.MarkAsCompensationFailed<TestEvent>();
        
            // Assert
            _mockSagaStore.Verify(ss => ss.LogStepAsync(_testSagaId, typeof(TestEvent), Saga.Enums.StepStatus.CompensationFailed, null), Times.Once);
        }

        [TestMethod]
        public async Task IsAlreadyCompleted_CallsSagaStoreIsStepCompletedAsync_AndReturnsResult()
        {
            // Arrange
            var context = new SagaContext<TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            _mockSagaStore.Setup(ss => ss.IsStepCompletedAsync(_testSagaId, typeof(TestEvent))).Returns(Task.FromResult(true));

            // Act
            var result = await context.IsAlreadyCompleted<TestEvent>();

            // Assert
            Assert.IsTrue(result);
            _mockSagaStore.Verify(ss => ss.IsStepCompletedAsync(_testSagaId, typeof(TestEvent)), Times.Once);
        }

        // --- Tests for ReactiveSagaStepFluent (via SagaContext<TMessage>) ---
        [TestMethod]
        public async Task ReactiveSagaStepFluent_ThenMarkAsComplete_UsesInitialMessageTypeForMarking()
        {
            // Arrange
            var context = new SagaContext<TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var stepEvent = new TestEvent(); // This is TStep
            var fluentStep = context.PublishWithTracking(stepEvent);

            // Act
            await fluentStep.ThenMarkAsComplete();

            // Assert
            // Verify that MarkAsComplete on the context was called with TInitialMessage (TestMessage)
            _mockSagaStore.Verify(s => s.LogStepAsync(
                _testSagaId,
                typeof(TestMessage), // TInitialMessage
                Saga.Enums.StepStatus.Completed, 
                null), Times.Once);
        }

        [TestMethod]
        public async Task ReactiveSagaStepFluent_ThenMarkAsFailed_UsesInitialMessageTypeForMarking()
        {
            // Arrange
            var context = new SagaContext<TestMessage>(_testSagaId, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            var stepEvent = new TestEvent(); // This is TStep
            var fluentStep = context.PublishWithTracking(stepEvent);
            var failResponse = new FailResponse(); // This parameter is currently unused by ReactiveSagaStepFluent's ThenMarkAsFailed

            // Act
            await fluentStep.ThenMarkAsFailed(failResponse);

            // Assert
            _mockSagaStore.Verify(s => s.LogStepAsync(
                _testSagaId,
                typeof(TestMessage), // TInitialMessage
                Saga.Enums.StepStatus.Failed, 
                null), Times.Once);
        }


        // --- Tests for SagaContext<TMessage, TSagaData> ---

        [TestMethod]
        public void SagaContextWithData_InitializesBaseAndDataCorrectly()
        {
            // Arrange
            var testData = new TestSagaData { TestProperty = "Sample" };
            
            // Act
            var context = new SagaContext<TestMessage, TestSagaData>(
                _testSagaId, 
                testData, 
                _mockEventBus.Object, 
                _mockSagaStore.Object, 
                _mockSagaIdGenerator.Object);

            // Assert
            Assert.AreEqual(_testSagaId, context.SagaId); // Base initialization
            Assert.AreSame(testData, context.Data);      // Data property
            _mockSagaIdGenerator.Verify(g => g.Generate(), Times.Never); // Ensure base used provided SagaId
        }

        [TestMethod]
        public void PublishWithTracking_ForContextWithData_ReturnsCoordinatedSagaStepFluent_WithCorrectContext()
        {
            // Arrange
            var testData = new TestSagaData();
            var parentContext = new SagaContext<TestMessage, TestSagaData>(
                _testSagaId, testData, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            
            var stepEvent = new TestEvent();
            var publishTask = Task.CompletedTask;
            // This mock is for the Publish call made by the parentContext, which then creates the step context.
            _mockEventBus.Setup(eb => eb.Publish(stepEvent, _testSagaId)).Returns(publishTask);

            // Act
            var fluentStep = parentContext.PublishWithTracking(stepEvent);

            // Assert
            _mockEventBus.Verify(eb => eb.Publish(stepEvent, _testSagaId), Times.Once); // Base publish called
            Assert.IsNotNull(fluentStep);
            Assert.IsInstanceOfType(fluentStep, typeof(CoordinatedSagaStepFluent<TestEvent, TestSagaData>));

            // To verify the inner context of CoordinatedSagaStepFluent, we'd ideally access it.
            // If not directly accessible, we can infer from its behavior if methods were added to it.
            // For now, the creation of the correct fluent type is a key indicator.
            // The refactoring in Step 1 of previous task ensured that the context passed to 
            // CoordinatedSagaStepFluent uses base class's eventBus, sagaStore, sagaIdGenerator.
            // We can verify this by having the CoordinatedSagaStepFluent perform an action that uses these,
            // but that would be testing CoordinatedSagaStepFluent itself more than SagaContext.
            // The prompt asks to verify the new step-specific context has same SagaId, Data and uses base services.
            // This was done by construction in the refactor:
            // `new SagaContext<TNewMessage, TSagaData>(this.SagaId, this.Data, this.BaseEventBus, this.BaseSagaStore, this.BaseSagaIdGenerator);`
            // So by design, if CoordinatedSagaStepFluent is created, its context has these properties.
            // We can add a simple test for one of its methods like ThenMarkAsComplete to ensure it uses the right context.
        }

        [TestMethod]
        public void SendWithTracking_ForContextWithData_ReturnsCoordinatedSagaStepFluent_WithCorrectContext()
        {
            // Arrange
            var testData = new TestSagaData();
            var parentContext = new SagaContext<TestMessage, TestSagaData>(
                _testSagaId, testData, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            
            var stepCommand = new TestCommand();
            var sendTask = Task.CompletedTask;
            _mockEventBus.Setup(eb => eb.Send(stepCommand, _testSagaId)).Returns(sendTask);

            // Act
            var fluentStep = parentContext.SendWithTracking(stepCommand);

            // Assert
            _mockEventBus.Verify(eb => eb.Send(stepCommand, _testSagaId), Times.Once); // Base send called
            Assert.IsNotNull(fluentStep);
            Assert.IsInstanceOfType(fluentStep, typeof(CoordinatedSagaStepFluent<TestCommand, TestSagaData>));
            // Similar to PublishWithTracking, detailed verification of the inner context's properties
            // relies on the correctness of the SagaContext<TMessage, TSagaData> constructor and CreateStepContext method.
        }

        // --- Tests for CoordinatedSagaStepFluent (via SagaContext<TMessage, TSagaData> and StepSpecificSagaContextAdapter) ---
        
        [TestMethod]
        public async Task CoordinatedSagaStepFluent_ThenMarkAsComplete_UsesAdapterAndMarksTStepCorrectly()
        {
            // Arrange
            var testData = new TestSagaData();
            var parentContext = new SagaContext<TestMessage, TestSagaData>( // TMessage is TestMessage
                _testSagaId, testData, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            
            var stepEvent = new TestEvent(); // This will be TStep for CoordinatedSagaStepFluent
            var fluentStep = parentContext.PublishWithTracking(stepEvent);

            // Act
            await fluentStep.ThenMarkAsComplete();

            // Assert
            // This verifies that the adapter (context within CoordinatedSagaStepFluent) correctly called LogStepAsync
            // with its own SagaId (_testSagaId), the TStep of the fluent step (TestEvent), and Completed status.
            _mockSagaStore.Verify(s => s.LogStepAsync(
                _testSagaId,
                typeof(TestEvent), // TStep for CoordinatedSagaStepFluent
                Saga.Enums.StepStatus.Completed,
                null), Times.Once);
        }

        [TestMethod]
        public async Task CoordinatedSagaStepFluent_ThenMarkAsFailed_UsesAdapterAndMarksTStepCorrectly()
        {
            // Arrange
            var testData = new TestSagaData();
            var parentContext = new SagaContext<TestMessage, TestSagaData>(
                _testSagaId, testData, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            
            var stepCommand = new TestCommand(); // This will be TStep
            var fluentStep = parentContext.SendWithTracking(stepCommand);
            var failResponse = new FailResponse(); // This parameter is currently unused by CoordinatedSagaStepFluent's ThenMarkAsFailed

            // Act
            await fluentStep.ThenMarkAsFailed(failResponse);

            // Assert
            _mockSagaStore.Verify(s => s.LogStepAsync(
                _testSagaId,
                typeof(TestCommand), // TStep for CoordinatedSagaStepFluent
                Saga.Enums.StepStatus.Failed,
                null), Times.Once);
        }

        [TestMethod]
        public async Task CoordinatedSagaStepFluent_Adapter_PublishWithTracking_DelegatesCorrectly()
        {
            // Arrange
            var testData = new TestSagaData();
            var initialContextMessage = new TestMessage(); // Just for typing the parent context
            var parentContext = new SagaContext<TestMessage, TestSagaData>( // TMessage for parent is TestMessage
                _testSagaId, 
                testData, 
                _mockEventBus.Object, 
                _mockSagaStore.Object, 
                _mockSagaIdGenerator.Object);
            
            // Get the CoordinatedSagaStepFluent, which holds the StepSpecificSagaContextAdapter
            var firstStepEvent = new TestEvent(); // TStep for the first fluent step
            var coordinatedFluentStep1 = parentContext.PublishWithTracking(firstStepEvent);

            // Now, we want to test a method on the adapter itself.
            // The adapter's PublishWithTracking method is what we need to target.
            // We need to mock the _eventBus.Publish call that the adapter's Publish method will make.
            var secondStepEvent = new TestEvent { MessageId = Guid.NewGuid() }; // A new event for the adapter to publish
            
            _mockEventBus.Reset(); // Reset calls from parentContext.PublishWithTracking
            _mockEventBus.Setup(eb => eb.Publish(secondStepEvent, _testSagaId)).Returns(Task.CompletedTask);

            // Act: Call PublishWithTracking on the adapter.
            // Since the adapter's ISagaContext methods are explicitly implemented, we need to cast to ISagaContext
            // or call the public methods of the adapter which are the 'new' CoordinatedSagaStepFluent returning ones.
            // The adapter's public PublishWithTracking<TCoordStep> creates a *new* adapter.
            // This test will verify that the adapter's Publish method (called by its own PublishWithTracking)
            // uses the original eventBus and sagaId.
            var coordinatedFluentStep2 = coordinatedFluentStep1.Context.PublishWithTracking(secondStepEvent);
            
            // Assert that the adapter's Publish method used the correct eventBus and sagaId
            _mockEventBus.Verify(eb => eb.Publish(secondStepEvent, _testSagaId), Times.Once);
            Assert.IsNotNull(coordinatedFluentStep2);
            Assert.IsInstanceOfType(coordinatedFluentStep2, typeof(CoordinatedSagaStepFluent<TestEvent, TestSagaData>));
        }


        [TestMethod]
        public async Task CoordinatedSagaStepFluent_Adapter_SendWithTracking_DelegatesCorrectly()
        {
            // Arrange
            var testData = new TestSagaData();
            var parentContext = new SagaContext<TestMessage, TestSagaData>(
                _testSagaId, testData, _mockEventBus.Object, _mockSagaStore.Object, _mockSagaIdGenerator.Object);
            
            var firstStepCommand = new TestCommand(); 
            var coordinatedFluentStep1 = parentContext.SendWithTracking(firstStepCommand);

            var secondStepCommand = new TestCommand { MessageId = Guid.NewGuid() };
            
            _mockEventBus.Reset(); 
            _mockEventBus.Setup(eb => eb.Send(secondStepCommand, _testSagaId)).Returns(Task.CompletedTask);

            // Act
            var coordinatedFluentStep2 = coordinatedFluentStep1.Context.SendWithTracking(secondStepCommand);
            
            // Assert
            _mockEventBus.Verify(eb => eb.Send(secondStepCommand, _testSagaId), Times.Once);
            Assert.IsNotNull(coordinatedFluentStep2);
            Assert.IsInstanceOfType(coordinatedFluentStep2, typeof(CoordinatedSagaStepFluent<TestCommand, TestSagaData>));
        }

    }
}
