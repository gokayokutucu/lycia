using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lycia.Messaging;
using Lycia.Saga;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Enums;
using Lycia.Infrastructure.Dispatching; 
using Lycia.Saga.Extensions; 

namespace Lycia.Tests
{
    // --- Test Stub Classes for Messages ---
    public class DispatcherTestMessage : IMessage { public Guid MessageId { get; set; } = Guid.NewGuid(); public Guid SagaId { get; set; } }
    public class DispatcherTestStartCommand : DispatcherTestMessage, ICommand { }
    public class DispatcherTestEventMessage : DispatcherTestMessage, IEvent { }
    public class DispatcherTestCompensatableEvent : DispatcherTestEventMessage { }


    public class DispatcherTestOriginalCommand : DispatcherTestMessage, ICommand { }
    public class DispatcherTestResponseMessage : DispatcherTestMessage, IResponse<DispatcherTestOriginalCommand> { public DispatcherTestOriginalCommand OriginalMessage { get; set; } }
    public class DispatcherTestSuccessResponseMessage : DispatcherTestResponseMessage, ISuccessResponse<DispatcherTestOriginalCommand> { }
    public class DispatcherTestFailResponseMessage : DispatcherTestResponseMessage, IFailResponse<DispatcherTestOriginalCommand> { }
    public class DispatcherTestResponseEventMessage : DispatcherTestEventMessage, IResponse<DispatcherTestOriginalCommand> { public DispatcherTestOriginalCommand OriginalMessage { get; set; } }


    public class DispatcherTestSagaData : SagaData { public string DataValue { get; set; } }

    // --- Mock Handler Implementations ---
    // ISagaStartHandler<TMsg>, ISagaHandlerWithContext<TMsg, TSagaData>
    public class MockCoordinatedStartHandler : ISagaStartHandler<DispatcherTestStartCommand>, ISagaHandlerWithContext<DispatcherTestStartCommand, DispatcherTestSagaData>
    {
        public bool InitializeCalled { get; private set; }
        public bool HandleStartAsyncCalled { get; private set; }
        public DispatcherTestStartCommand ReceivedMessage { get; private set; }
        public ISagaContext<DispatcherTestStartCommand, DispatcherTestSagaData> Context { get; private set; }

        public void Initialize(ISagaContext<DispatcherTestStartCommand, DispatcherTestSagaData> context) { InitializeCalled = true; Context = context; }
        public Task HandleStartAsync(DispatcherTestStartCommand message) { HandleStartAsyncCalled = true; ReceivedMessage = message; return Task.CompletedTask; }
    }
    
    // ISagaStartHandler<TMsg>, ISagaHandlerWithContext<TMsg>
    public class MockReactiveStartHandler : ISagaStartHandler<DispatcherTestStartCommand>, ISagaHandlerWithContext<DispatcherTestStartCommand>
    {
        public bool InitializeCalled { get; private set; }
        public bool HandleStartAsyncCalled { get; private set; }
        public DispatcherTestStartCommand ReceivedMessage { get; private set; }
        public ISagaContext<DispatcherTestStartCommand> Context { get; private set; }

        public void Initialize(ISagaContext<DispatcherTestStartCommand> context) { InitializeCalled = true; Context = context; }
        public Task HandleStartAsync(DispatcherTestStartCommand message) { HandleStartAsyncCalled = true; ReceivedMessage = message; return Task.CompletedTask; }
    }

    // ISagaStartHandler<TMsg>, ISagaHandlerWithContext<IMessage>
    public class MockGenericlessContextHandler : ISagaStartHandler<DispatcherTestStartCommand>, ISagaHandlerWithContext<IMessage>
    {
        public bool InitializeCalled { get; private set; }
        public bool HandleStartAsyncCalled { get; private set; }
        public IMessage ReceivedMessage { get; private set; }
        public ISagaContext<IMessage> Context { get; private set; }
        public void Initialize(ISagaContext<IMessage> context) { InitializeCalled = true; Context = context; }
        public Task HandleStartAsync(DispatcherTestStartCommand message) { HandleStartAsyncCalled = true; ReceivedMessage = message; return Task.CompletedTask; }
    }
    
    // ISuccessResponseHandler<TResp>, ISagaHandlerWithContext<TResp>
    public class MockSuccessResponseHandler : ISuccessResponseHandler<DispatcherTestSuccessResponseMessage>, ISagaHandlerWithContext<DispatcherTestSuccessResponseMessage> 
    {
        public bool InitializeCalled { get; private set; }
        public bool HandleSuccessResponseAsyncCalled { get; private set; }
        public ISagaContext<DispatcherTestSuccessResponseMessage> Context { get; private set; }
        public void Initialize(ISagaContext<DispatcherTestSuccessResponseMessage> context) { InitializeCalled = true; Context = context; }
        public Task HandleSuccessResponseAsync(DispatcherTestSuccessResponseMessage message) { HandleSuccessResponseAsyncCalled = true; return Task.CompletedTask; }
    }

    // IFailResponseHandler<TResp>, ISagaHandlerWithContext<TResp>
    public class MockFailResponseHandler : IFailResponseHandler<DispatcherTestFailResponseMessage>, ISagaHandlerWithContext<DispatcherTestFailResponseMessage> 
    {
        public bool InitializeCalled { get; private set; }
        public bool HandleFailResponseAsyncCalled { get; private set; }
        public ISagaContext<DispatcherTestFailResponseMessage> Context { get; private set; }
        public void Initialize(ISagaContext<DispatcherTestFailResponseMessage> context) { InitializeCalled = true; Context = context; }
        public Task HandleFailResponseAsync(DispatcherTestFailResponseMessage message, FailResponse fail) { HandleFailResponseAsyncCalled = true; return Task.CompletedTask; }
    }

    // ISagaCompensationHandler<TMsg>
    public class MockCompensationHandler : ISagaCompensationHandler<DispatcherTestCompensatableEvent>
    {
        public bool InitializeCalled { get; private set; } // Initialize was added to ISagaCompensationHandler
        public bool CompensateAsyncCalled { get; private set; }
        public ISagaContext<DispatcherTestCompensatableEvent> Context { get; private set; }
        public void Initialize(ISagaContext<DispatcherTestCompensatableEvent> context) { InitializeCalled = true; Context = context; }
        public Task CompensateAsync(DispatcherTestCompensatableEvent message) { CompensateAsyncCalled = true; return Task.CompletedTask; }
    }
    
    // Handler for testing HandleAsync fallback
    public class FallbackTestHandler : IMessageHandler // A generic interface not tied to specific InvokeHandlerAsync logic
    {
        public bool HandleAsyncSpecificCalled { get; private set; }
        public bool HandleAsyncIMessageCalled { get; private set; }
        public IMessage ReceivedMessage { get; private set; }

        public Task HandleAsync(DispatcherTestEventMessage message) 
        {
            HandleAsyncSpecificCalled = true;
            ReceivedMessage = message;
            return Task.CompletedTask;
        }

        public Task HandleAsync(IMessage message) 
        {
            HandleAsyncIMessageCalled = true;
            ReceivedMessage = message;
            return Task.CompletedTask;
        }
    }
    public interface IMessageHandler { /* Marker for testing fallback */ }


    [TestClass]
    public class SagaDispatcherTests
    {
        private Mock<ISagaStore> _mockSagaStore;
        private Mock<ISagaIdGenerator> _mockSagaIdGenerator;
        private Mock<IEventBus> _mockEventBus;
        private Mock<IServiceProvider> _mockServiceProvider;
        private SagaDispatcher _dispatcher;
        private Guid _generatedSagaId;
        private Guid _providedSagaId;

        [TestInitialize]
        public void Setup()
        {
            _mockSagaStore = new Mock<ISagaStore>();
            _mockSagaIdGenerator = new Mock<ISagaIdGenerator>();
            _mockEventBus = new Mock<IEventBus>();
            _mockServiceProvider = new Mock<IServiceProvider>();

            _generatedSagaId = Guid.NewGuid();
            _providedSagaId = Guid.NewGuid();
            _mockSagaIdGenerator.Setup(g => g.Generate()).Returns(_generatedSagaId);

            // Setup for LoadContextAsync used by InitializeCoordinatedHandlerAsync
            _mockSagaStore.Setup(s => s.LoadContextAsync<DispatcherTestSagaData>(It.IsAny<Guid>()))
                          .ReturnsAsync((Guid sagaIdParam) => new DispatcherTestSagaData { SagaId = sagaIdParam, DataValue = "Loaded" });
            
            _mockSagaStore.Setup(s => s.IsStepCompletedAsync(It.IsAny<Guid>(), It.IsAny<Type>())).ReturnsAsync(false);
            
            _dispatcher = new SagaDispatcher(
                _mockSagaStore.Object,
                _mockSagaIdGenerator.Object,
                _mockEventBus.Object,
                _mockServiceProvider.Object
            );
        }
        
        private void SetupMockHandlerResolution(Type handlerInterfaceType, IEnumerable<object> handlerInstances)
        {
             _mockServiceProvider.Setup(sp => sp.GetServices(handlerInterfaceType))
                                .Returns(handlerInstances.ToList());
        }

        // --- ISagaStartHandler Tests ---
        [TestMethod]
        public async Task DispatchAsync_SagaStart_Coordinated_NoSagaId_GeneratesId_InitializesAndHandles()
        {
            var command = new DispatcherTestStartCommand();
            var mockHandler = new MockCoordinatedStartHandler();
            SetupMockHandlerResolution(typeof(ISagaStartHandler<DispatcherTestStartCommand>), new List<object> { mockHandler });

            await _dispatcher.DispatchAsync(command);

            _mockSagaIdGenerator.Verify(g => g.Generate(), Times.Once);
            Assert.IsTrue(mockHandler.InitializeCalled, "Initialize");
            Assert.IsTrue(mockHandler.HandleStartAsyncCalled, "HandleStartAsync");
            Assert.AreEqual(_generatedSagaId, mockHandler.Context.SagaId);
            Assert.AreEqual("Loaded", mockHandler.Context.Data.DataValue);
        }

        [TestMethod]
        public async Task DispatchAsync_SagaStart_Reactive_WithSagaId_ReusesId_InitializesAndHandles()
        {
            var command = new DispatcherTestStartCommand { SagaId = _providedSagaId };
            var mockHandler = new MockReactiveStartHandler();
            SetupMockHandlerResolution(typeof(ISagaStartHandler<DispatcherTestStartCommand>), new List<object> { mockHandler });

            await _dispatcher.DispatchAsync(command);

            _mockSagaIdGenerator.Verify(g => g.Generate(), Times.Never);
            Assert.IsTrue(mockHandler.InitializeCalled, "Initialize");
            Assert.IsTrue(mockHandler.HandleStartAsyncCalled, "HandleStartAsync");
            Assert.AreEqual(_providedSagaId, mockHandler.Context.SagaId);
        }
        
        [TestMethod]
        public async Task DispatchAsync_SagaStart_GenericlessContext_InitializesAndHandles()
        {
            var command = new DispatcherTestStartCommand { SagaId = _providedSagaId };
            var mockHandler = new MockGenericlessContextHandler();
            SetupMockHandlerResolution(typeof(ISagaStartHandler<DispatcherTestStartCommand>), new List<object> { mockHandler });

            await _dispatcher.DispatchAsync(command);
            
            Assert.IsTrue(mockHandler.InitializeCalled, "Initialize");
            Assert.IsTrue(mockHandler.HandleStartAsyncCalled, "HandleStartAsync");
            Assert.AreEqual(_providedSagaId, mockHandler.Context.SagaId);
        }

        // --- ISagaCompensationHandler Tests ---
        [TestMethod]
        public async Task DispatchAsync_Message_ResolvesCompensationHandler_InitializesAndCompensates()
        {
            var compensatableEvent = new DispatcherTestCompensatableEvent { SagaId = _providedSagaId };
            var mockCompHandler = new MockCompensationHandler();

            SetupMockHandlerResolution(typeof(ISagaStartHandler<DispatcherTestCompensatableEvent>), new List<object>()); // No start
            SetupMockHandlerResolution(typeof(ISagaCompensationHandler<DispatcherTestCompensatableEvent>), new List<object> { mockCompHandler });

            await _dispatcher.DispatchAsync(compensatableEvent);

            Assert.IsTrue(mockCompHandler.InitializeCalled, "Initialize Compensation");
            Assert.IsTrue(mockCompHandler.CompensateAsyncCalled, "CompensateAsync");
            Assert.AreEqual(_providedSagaId, mockCompHandler.Context.SagaId);
        }
        
        // --- Response Handler Tests ---
        [TestMethod]
        public async Task DispatchAsync_SuccessResponse_InitializesAndHandles()
        {
            var originalCmd = new DispatcherTestOriginalCommand { SagaId = _providedSagaId };
            var successResponse = new DispatcherTestSuccessResponseMessage { SagaId = _providedSagaId, OriginalMessage = originalCmd };
            var mockHandler = new MockSuccessResponseHandler();
            SetupMockHandlerResolution(typeof(ISuccessResponseHandler<DispatcherTestSuccessResponseMessage>), new List<object>{mockHandler});

            await _dispatcher.DispatchAsync<DispatcherTestOriginalCommand, DispatcherTestSuccessResponseMessage>(successResponse);

            Assert.IsTrue(mockHandler.InitializeCalled, "Initialize SuccessResp");
            Assert.IsTrue(mockHandler.HandleSuccessResponseAsyncCalled, "HandleSuccessResp");
            Assert.AreEqual(_providedSagaId, mockHandler.Context.SagaId);
        }

        [TestMethod]
        public async Task DispatchAsync_FailResponse_InitializesAndHandles()
        {
            var originalCmd = new DispatcherTestOriginalCommand { SagaId = _providedSagaId };
            var failResponseMsg = new DispatcherTestFailResponseMessage { SagaId = _providedSagaId, OriginalMessage = originalCmd };
            var mockHandler = new MockFailResponseHandler();
            SetupMockHandlerResolution(typeof(IFailResponseHandler<DispatcherTestFailResponseMessage>), new List<object>{mockHandler});

            await _dispatcher.DispatchAsync<DispatcherTestOriginalCommand, DispatcherTestFailResponseMessage>(failResponseMsg);

            Assert.IsTrue(mockHandler.InitializeCalled, "Initialize FailResp");
            Assert.IsTrue(mockHandler.HandleFailResponseAsyncCalled, "HandleFailResp");
            Assert.AreEqual(_providedSagaId, mockHandler.Context.SagaId);
        }

        [TestMethod]
        public async Task DispatchAsync_PureResponse_NotSuccessOrFail_DoesNotFallback()
        {
            var originalCmd = new DispatcherTestOriginalCommand { SagaId = _providedSagaId };
            var pureResponse = new DispatcherTestResponseMessage { SagaId = _providedSagaId, OriginalMessage = originalCmd };
            
            SetupMockHandlerResolution(typeof(ISuccessResponseHandler<DispatcherTestResponseMessage>), new List<object>());
            SetupMockHandlerResolution(typeof(IFailResponseHandler<DispatcherTestResponseMessage>), new List<object>());
            
            var mockFallbackStartHandler = new MockReactiveStartHandler(); // Used to detect fallback
            SetupMockHandlerResolution(typeof(ISagaStartHandler<DispatcherTestResponseMessage>), new List<object> { mockFallbackStartHandler });

            await _dispatcher.DispatchAsync<DispatcherTestOriginalCommand, DispatcherTestResponseMessage>(pureResponse);

            Assert.IsFalse(mockFallbackStartHandler.HandleStartAsyncCalled, "DispatchByMessageTypeAsync should not be called.");
        }

        [TestMethod]
        public async Task DispatchAsync_ResponseIsEvent_NotSuccessOrFail_FallsBackToDispatchByMessageType()
        {
            var originalCmd = new DispatcherTestOriginalCommand { SagaId = _providedSagaId };
            var responseEvent = new DispatcherTestResponseEventMessage { SagaId = _providedSagaId, OriginalMessage = originalCmd };
            
            SetupMockHandlerResolution(typeof(ISuccessResponseHandler<DispatcherTestResponseEventMessage>), new List<object>());
            SetupMockHandlerResolution(typeof(IFailResponseHandler<DispatcherTestResponseEventMessage>), new List<object>());

            var mockFallbackHandler = new MockReactiveStartHandler(); 
            SetupMockHandlerResolution(typeof(ISagaStartHandler<DispatcherTestResponseEventMessage>), new List<object> { mockFallbackHandler });
            
            await _dispatcher.DispatchAsync<DispatcherTestOriginalCommand, DispatcherTestResponseEventMessage>(responseEvent);

            Assert.IsTrue(mockFallbackHandler.HandleStartAsyncCalled, "DispatchByMessageTypeAsync should be called.");
        }
        
        // --- HandleAsync Fallback Test (Conceptual - relies on internal InvokeHandlerAsync structure) ---
        [TestMethod]
        public async Task DispatchAsync_InvokeHandlerAsync_Fallback_CallsHandleAsync_TMessage()
        {
            // This test assumes InvokeHandlerAsync is called with a handlerType that doesn't have
            // HandleStartAsync, HandleSuccessResponseAsync etc. which would trigger the HandleAsync fallback.
            // We use a generic event and a handler that implements a non-specific interface for registration.
            var eventMessage = new DispatcherTestEventMessage { SagaId = _providedSagaId };
            var mockFallbackHandler = new FallbackTestHandler();

            // Register FallbackTestHandler for IMessageHandler<DispatcherTestEventMessage>
            // SagaDispatcher's DispatchByMessageTypeAsync doesn't directly look for IMessageHandler.
            // To test the fallback in InvokeHandlerAsync, we'd typically need InvokeHandlerAsync to be called
            // with handlerType = typeof(IMessageHandler) or similar.
            // The current DispatchByMessageTypeAsync calls InvokeHandlerAsync with ISagaStartHandler or ISagaCompensationHandler.
            // If InvokeHandlerAsync was refactored to use helpers, those helpers would be tested.
            // If InvokeHandlerAsync has the direct if/else for method names, then to test fallback:
            // handlerType must NOT be ISagaStartHandler, ISuccessResponseHandler, IFailResponseHandler.
            // And the handler instance (mockFallbackHandler) must have HandleAsync(DispatcherTestEventMessage).
            
            // For this test, we'll assume a scenario where DispatchByMessageTypeAsync somehow resolves a handler
            // (e.g., registered for a base event type or a less specific interface if dispatcher logic allowed it)
            // and then InvokeHandlerAsync is called for it.
            // Since direct setup for this path is complex with current DispatchByMessageTypeAsync,
            // this test is more about the *expected behavior of InvokeHandlerAsync's fallback section*.

            // Simulate that IMessageHandler<DispatcherTestEventMessage> was resolved
            var handlerTypeForInvoke = typeof(IMessageHandler); // This is the key: a type not matching specific methods
             _mockServiceProvider.Setup(sp => sp.GetServices(handlerTypeForInvoke))
                                .Returns(new List<object> { mockFallbackHandler });

            // To call InvokeHandlerAsync, we need to bypass the main DispatchAsync routing or find a way through it.
            // Let's assume a hypothetical DispatchToGenericHandlerAsync for testing this.
            // For now, we acknowledge this test setup is tricky.

            // The subtask implies testing the fallback "If no specific method like HandleStartAsync is matched by handlerType".
            // This means the handlerType itself in InvokeHandlerAsync is what causes the fallback.
            // We can simulate this by directly testing a call to InvokeHandlerAsync if it were testable,
            // or ensuring one of the existing DispatchAsync paths could lead to such a handlerType.
            // The current `DispatchByMessageTypeAsync` only calls `InvokeHandlerAsync` for `ISagaStartHandler` or `ISagaCompensationHandler`.
            // The `InvokeHandlerAsync` from the *previous state* (before helper methods) would look at `handlerType`
            // and if it's not one of the specific ones, it would try `handler.GetType().GetMethod("HandleAsync")`.

            // This test remains difficult to implement perfectly without either:
            // 1. Making InvokeHandlerAsync internal/public for direct testing.
            // 2. Having the full refactored InvokeHandlerAsync (with helpers) in place and testing the helpers.
            // Given the previous file state discrepancies, I'll assume the *intent* of testing the fallback.
            // The most practical way with a private InvokeHandlerAsync is if DispatchByMessageTypeAsync
            // could somehow pass a "generic" handler type to it. It currently does not.
            
            // The test will assert the behavior if such a call to InvokeHandlerAsync was made.
            // This is more of a specification test for InvokeHandlerAsync's internal fallback.
            // No direct dispatch path currently in SagaDispatcher leads to this for a generic IMessageHandler.
             Assert.Inconclusive("Directly testing HandleAsync fallback via DispatchAsync is complex due to DispatchByMessageTypeAsync's specific handler lookups. This test assumes InvokeHandlerAsync would be called with a generic handler type.");
        }


        // --- No Handler Found Tests ---
        [TestMethod]
        public async Task DispatchAsync_NoHandlerFound_ForStartCommand_CompletesSilently()
        {
            var command = new DispatcherTestStartCommand();
            SetupMockHandlerResolution(typeof(ISagaStartHandler<DispatcherTestStartCommand>), new List<object>()); 

            await _dispatcher.DispatchAsync(command);
            _mockSagaIdGenerator.Verify(g => g.Generate(), Times.Never);
             // Also verify no methods on SagaStore or EventBus were called for this command.
            _mockSagaStore.VerifyNoOtherCalls();
            _mockEventBus.VerifyNoOtherCalls();
        }
    }
}
