using Xunit; // Changed
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
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Tests
{
    // --- Test Stub Classes for Messages (prefixed to avoid collisions) ---
    public class Dispatcher_TestMessage : IMessage 
    { 
        public Guid MessageId { get; set; } = Guid.NewGuid(); 
        public Guid SagaId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string ApplicationId { get; set; } = "DispatcherTestsApp";
    }
    public class Dispatcher_TestStartCommand : Dispatcher_TestMessage, ICommand { }
    public class Dispatcher_TestEventMessage : Dispatcher_TestMessage, IEvent { }
    public class Dispatcher_TestCompensatableEvent : Dispatcher_TestEventMessage { }

    // Renamed Dispatcher_TestPreviousCommand to Dispatcher_TestOriginalCommand for consistency
    public class Dispatcher_TestOriginalCommand : Dispatcher_TestMessage, ICommand { }

    // Simplified and corrected response stubs
    public class Dispatcher_TestResponseMessage : Dispatcher_TestMessage, IResponse<Dispatcher_TestOriginalCommand>, ISuccessResponse<IMessage>, IFailResponse<IMessage>
    { 
        public Dispatcher_TestOriginalCommand OriginalMessage { get; set; }
        public Guid CorrelationId { get; set; } = Guid.NewGuid();
    }

    public class Dispatcher_TestSuccessResponseMessage : Dispatcher_TestResponseMessage, ISuccessResponse<Dispatcher_TestOriginalCommand>, ISuccessResponse<IMessage>
    { }
    
    public class Dispatcher_TestFailResponseMessage : Dispatcher_TestResponseMessage, IFailResponse<Dispatcher_TestOriginalCommand>, IFailResponse<IMessage>
    { }
    
    public class Dispatcher_TestResponseEventMessage : Dispatcher_TestEventMessage, IResponse<Dispatcher_TestOriginalCommand>, ISuccessResponse<IMessage>, IFailResponse<IMessage>
    { 
        public Dispatcher_TestOriginalCommand OriginalMessage { get; set; }
        public Guid CorrelationId { get; set; } = Guid.NewGuid(); 
    }

    public class Dispatcher_TestSagaData : SagaData
    {
        
    }

    // --- Mock Handler Implementations (prefixed) ---
    public class Dispatcher_MockCoordinatedStartHandler : ISagaStartHandler<Dispatcher_TestStartCommand>, ISagaHandlerWithContext<Dispatcher_TestStartCommand, Dispatcher_TestSagaData>
    {
        public bool InitializeCalled { get; private set; }
        public bool HandleStartAsyncCalled { get; private set; }
        public Dispatcher_TestStartCommand ReceivedMessage { get; private set; }
        public ISagaContext<Dispatcher_TestStartCommand, Dispatcher_TestSagaData> Context { get; private set; }

        public void Initialize(ISagaContext<Dispatcher_TestStartCommand, Dispatcher_TestSagaData> context) { InitializeCalled = true; Context = context; }
        public Task HandleStartAsync(Dispatcher_TestStartCommand message) { HandleStartAsyncCalled = true; ReceivedMessage = message; if (Context == null && InitializeCalled == false) throw new InvalidOperationException("Context not initialized"); return Task.CompletedTask; }
    }
    
    public class Dispatcher_MockReactiveStartHandler : ISagaStartHandler<Dispatcher_TestStartCommand>, ISagaHandlerWithContext<Dispatcher_TestStartCommand>
    {
        public bool InitializeCalled { get; private set; }
        public bool HandleStartAsyncCalled { get; private set; }
        public Dispatcher_TestStartCommand ReceivedMessage { get; private set; }
        public ISagaContext<Dispatcher_TestStartCommand> Context { get; private set; }

        public void Initialize(ISagaContext<Dispatcher_TestStartCommand> context) { InitializeCalled = true; Context = context; }
        public Task HandleStartAsync(Dispatcher_TestStartCommand message) { HandleStartAsyncCalled = true; ReceivedMessage = message; if (Context == null && InitializeCalled == false) throw new InvalidOperationException("Context not initialized"); return Task.CompletedTask; }
    }

    public class Dispatcher_MockGenericlessContextHandler : ISagaStartHandler<Dispatcher_TestStartCommand>, ISagaHandlerWithContext<IMessage>
    {
        public bool InitializeCalled { get; private set; }
        public bool HandleStartAsyncCalled { get; private set; }
        public IMessage ReceivedMessage { get; private set; }
        public ISagaContext<IMessage> Context { get; private set; }
        public void Initialize(ISagaContext<IMessage> context) { InitializeCalled = true; Context = context; }
        public Task HandleStartAsync(Dispatcher_TestStartCommand message) { HandleStartAsyncCalled = true; ReceivedMessage = message; if (Context == null && InitializeCalled == false) throw new InvalidOperationException("Context not initialized"); return Task.CompletedTask; }
    }
    
    public class Dispatcher_MockSuccessResponseHandler : ISuccessResponseHandler<Dispatcher_TestSuccessResponseMessage>, ISagaHandlerWithContext<Dispatcher_TestSuccessResponseMessage> 
    {
        public bool InitializeCalled { get; private set; }
        public bool HandleSuccessResponseAsyncCalled { get; private set; }
        public ISagaContext<Dispatcher_TestSuccessResponseMessage> Context { get; private set; }
        public void Initialize(ISagaContext<Dispatcher_TestSuccessResponseMessage> context) { InitializeCalled = true; Context = context; }
        public Task HandleSuccessResponseAsync(Dispatcher_TestSuccessResponseMessage message) { HandleSuccessResponseAsyncCalled = true; if (Context == null && InitializeCalled == false) throw new InvalidOperationException("Context not initialized"); return Task.CompletedTask; }
    }

    public class Dispatcher_MockFailResponseHandler : IFailResponseHandler<Dispatcher_TestFailResponseMessage>, ISagaHandlerWithContext<Dispatcher_TestFailResponseMessage> 
    {
        public bool InitializeCalled { get; private set; }
        public bool HandleFailResponseAsyncCalled { get; private set; }
        public ISagaContext<Dispatcher_TestFailResponseMessage> Context { get; private set; }
        public void Initialize(ISagaContext<Dispatcher_TestFailResponseMessage> context) { InitializeCalled = true; Context = context; }
        public Task HandleFailResponseAsync(Dispatcher_TestFailResponseMessage message, FailResponse fail) { HandleFailResponseAsyncCalled = true; if (Context == null && InitializeCalled == false) throw new InvalidOperationException("Context not initialized"); return Task.CompletedTask; }
    }

    public class Dispatcher_MockCompensationHandler : ISagaCompensationHandler<Dispatcher_TestCompensatableEvent>
    {
        public bool InitializeCalled { get; private set; }
        public bool CompensateAsyncCalled { get; private set; }
        public ISagaContext<Dispatcher_TestCompensatableEvent> Context { get; private set; }
        public void Initialize(ISagaContext<Dispatcher_TestCompensatableEvent> context) { InitializeCalled = true; Context = context; }
        public Task CompensateAsync(Dispatcher_TestCompensatableEvent message) { CompensateAsyncCalled = true; if (Context == null && InitializeCalled == false) throw new InvalidOperationException("Context not initialized for compensation handler."); return Task.CompletedTask; }
    }
    
    // Using a more specific interface that SagaDispatcher might resolve for a fallback test.
    // E.g. if a message is dispatched that has no ISagaStartHandler but has this.
    public interface Dispatcher_Tests_IGenericEventHandler<in TEvent> : Dispatcher_Tests_IMessageHandler where TEvent : IEvent { } // Corrected base interface

    public class Dispatcher_FallbackTestHandler : Dispatcher_Tests_IGenericEventHandler<Dispatcher_TestEventMessage>
    {
        public bool HandleAsyncSpecificCalled { get; private set; }
        public bool HandleAsyncIMessageCalled { get; private set; }
        public IMessage ReceivedMessage { get; private set; }

        public Task HandleAsync(Dispatcher_TestEventMessage message) 
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
    public interface Dispatcher_Tests_IMessageHandler { /* Marker for testing fallback */ }

    // [TestClass] removed
    public class SagaDispatcherTests
    {
        private readonly Mock<ISagaStore> _mockSagaStore;
        private readonly Mock<ISagaIdGenerator> _mockSagaIdGenerator;
        private readonly Mock<IEventBus> _mockEventBus;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly SagaDispatcher _dispatcher;
        private readonly Guid _generatedSagaId;
        private readonly Guid _providedSagaId;

        // Constructor for xUnit setup (formerly [TestInitialize])
        public SagaDispatcherTests()
        {
            _mockSagaStore = new Mock<ISagaStore>();
            _mockSagaIdGenerator = new Mock<ISagaIdGenerator>();
            _mockEventBus = new Mock<IEventBus>();
            _mockServiceProvider = new Mock<IServiceProvider>();

            _generatedSagaId = Guid.NewGuid();
            _providedSagaId = Guid.NewGuid();
            _mockSagaIdGenerator.Setup(g => g.Generate()).Returns(_generatedSagaId);

            _mockSagaStore.Setup(s => s.LoadContextAsync<Dispatcher_TestMessage, Dispatcher_TestSagaData>(It.IsAny<Guid>()))
                          .Returns((Guid sagaIdParam) => new Dispatcher_TestSagaData
                          {
                              Extras = new Dictionary<string, object>()
                              {
                                    { "Test", "Loaded" }
                              }
                          });
            
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
        [Fact] // Changed
        public async Task DispatchAsync_SagaStart_Coordinated_NoSagaId_GeneratesId_InitializesAndHandles()
        {
            var command = new Dispatcher_TestStartCommand();
            var mockHandler = new Dispatcher_MockCoordinatedStartHandler();
            SetupMockHandlerResolution(typeof(ISagaStartHandler<Dispatcher_TestStartCommand>), new List<object> { mockHandler });

            await _dispatcher.DispatchAsync(command);

            _mockSagaIdGenerator.Verify(g => g.Generate(), Times.Once);
            Assert.True(mockHandler.InitializeCalled, "Initialize"); // Changed
            Assert.True(mockHandler.HandleStartAsyncCalled, "HandleStartAsync"); // Changed
            Assert.Equal(_generatedSagaId, mockHandler.Context.SagaId); // Changed
            Assert.Equal("Loaded", mockHandler.Context.Data.Get<string>("Test")); // Changed
        }

        [Fact] // Changed
        public async Task DispatchAsync_SagaStart_Reactive_WithSagaId_ReusesId_InitializesAndHandles()
        {
            var command = new Dispatcher_TestStartCommand { SagaId = _providedSagaId };
            var mockHandler = new Dispatcher_MockReactiveStartHandler();
            SetupMockHandlerResolution(typeof(ISagaStartHandler<Dispatcher_TestStartCommand>), new List<object> { mockHandler });

            await _dispatcher.DispatchAsync(command);

            _mockSagaIdGenerator.Verify(g => g.Generate(), Times.Never);
            Assert.True(mockHandler.InitializeCalled, "Initialize"); // Changed
            Assert.True(mockHandler.HandleStartAsyncCalled, "HandleStartAsync"); // Changed
            Assert.Equal(_providedSagaId, mockHandler.Context.SagaId); // Changed
        }
        
        [Fact] // Changed
        public async Task DispatchAsync_SagaStart_GenericlessContext_InitializesAndHandles()
        {
            var command = new Dispatcher_TestStartCommand { SagaId = _providedSagaId };
            var mockHandler = new Dispatcher_MockGenericlessContextHandler();
            SetupMockHandlerResolution(typeof(ISagaStartHandler<Dispatcher_TestStartCommand>), new List<object> { mockHandler });

            await _dispatcher.DispatchAsync(command);
            
            Assert.True(mockHandler.InitializeCalled, "Initialize"); // Changed
            Assert.True(mockHandler.HandleStartAsyncCalled, "HandleStartAsync"); // Changed
            Assert.Equal(_providedSagaId, mockHandler.Context.SagaId); // Changed
        }

        // --- ISagaCompensationHandler Tests ---
        [Fact] // Changed
        public async Task DispatchAsync_Message_ResolvesCompensationHandler_InitializesAndCompensates()
        {
            var compensatableEvent = new Dispatcher_TestCompensatableEvent { SagaId = _providedSagaId };
            var mockCompHandler = new Dispatcher_MockCompensationHandler();

            SetupMockHandlerResolution(typeof(ISagaStartHandler<Dispatcher_TestCompensatableEvent>), new List<object>()); 
            SetupMockHandlerResolution(typeof(ISagaCompensationHandler<Dispatcher_TestCompensatableEvent>), new List<object> { mockCompHandler });

            await _dispatcher.DispatchAsync(compensatableEvent);

            Assert.True(mockCompHandler.InitializeCalled, "Initialize Compensation"); // Changed
            Assert.True(mockCompHandler.CompensateAsyncCalled, "CompensateAsync"); // Changed
            Assert.Equal(_providedSagaId, mockCompHandler.Context.SagaId); // Changed
        }
        
        // --- Response Handler Tests ---
        [Fact] // Changed
        public async Task DispatchAsync_SuccessResponse_InitializesAndHandles()
        {
            var originalCmd = new Dispatcher_TestOriginalCommand { SagaId = _providedSagaId }; 
            var successResponse = new Dispatcher_TestSuccessResponseMessage { SagaId = _providedSagaId, OriginalMessage = originalCmd, CorrelationId = Guid.NewGuid() };
            var mockHandlerInstance = new Dispatcher_MockSuccessResponseHandler();
            SetupMockHandlerResolution(typeof(ISuccessResponseHandler<Dispatcher_TestSuccessResponseMessage>), new List<object>{mockHandlerInstance});

            // Reverted TMessage in DispatchAsync call to Dispatcher_TestOriginalCommand
            await _dispatcher.DispatchAsync<Dispatcher_TestOriginalCommand, Dispatcher_TestSuccessResponseMessage>(successResponse);

            Assert.True(mockHandlerInstance.InitializeCalled, "Initialize SuccessResp"); 
            Assert.True(mockHandlerInstance.HandleSuccessResponseAsyncCalled, "HandleSuccessResp"); 
            Assert.Equal(_providedSagaId, mockHandlerInstance.Context.SagaId); 
        }

        [Fact] // Changed
        public async Task DispatchAsync_FailResponse_InitializesAndHandles()
        {
            var originalCmd = new Dispatcher_TestOriginalCommand { SagaId = _providedSagaId }; 
            var failResponseToDispatch = new Dispatcher_TestFailResponseMessage { SagaId = _providedSagaId, OriginalMessage = originalCmd, CorrelationId = Guid.NewGuid() };
            var mockHandlerInstance = new Dispatcher_MockFailResponseHandler();
            SetupMockHandlerResolution(typeof(IFailResponseHandler<Dispatcher_TestFailResponseMessage>), new List<object>{mockHandlerInstance});

            // Reverted TMessage in DispatchAsync call
            await _dispatcher.DispatchAsync<Dispatcher_TestOriginalCommand, Dispatcher_TestFailResponseMessage>(failResponseToDispatch);

            Assert.True(mockHandlerInstance.InitializeCalled, "Initialize FailResp"); 
            Assert.True(mockHandlerInstance.HandleFailResponseAsyncCalled, "HandleFailResp"); 
            Assert.Equal(_providedSagaId, mockHandlerInstance.Context.SagaId); 
        }

        [Fact] // Changed
        public async Task DispatchAsync_PureResponse_NotSuccessOrFail_DoesNotFallback()
        {
            var originalCmd = new Dispatcher_TestOriginalCommand { SagaId = _providedSagaId }; 
            var pureResponse = new Dispatcher_TestResponseMessage { SagaId = _providedSagaId, OriginalMessage = originalCmd, CorrelationId = Guid.NewGuid() };
            
            SetupMockHandlerResolution(typeof(ISuccessResponseHandler<Dispatcher_TestResponseMessage>), new List<object>());
            SetupMockHandlerResolution(typeof(IFailResponseHandler<Dispatcher_TestResponseMessage>), new List<object>());
            
            var mockFallbackStartHandler = new Dispatcher_MockReactiveStartHandler(); 
            SetupMockHandlerResolution(typeof(ISagaStartHandler<Dispatcher_TestResponseMessage>), new List<object> { mockFallbackStartHandler });

            // Reverted TMessage in DispatchAsync call
            await _dispatcher.DispatchAsync<Dispatcher_TestOriginalCommand, Dispatcher_TestResponseMessage>(pureResponse);

            Assert.False(mockFallbackStartHandler.HandleStartAsyncCalled, "DispatchByMessageTypeAsync should not be called."); 
        }

        [Fact] // Changed
        public async Task DispatchAsync_ResponseIsEvent_NotSuccessOrFail_FallsBackToDispatchByMessageType()
        {
            var originalCmd = new Dispatcher_TestOriginalCommand { SagaId = _providedSagaId }; 
            var responseEvent = new Dispatcher_TestResponseEventMessage { SagaId = _providedSagaId, OriginalMessage = originalCmd, CorrelationId = Guid.NewGuid() };
            
            SetupMockHandlerResolution(typeof(ISuccessResponseHandler<Dispatcher_TestResponseEventMessage>), new List<object>());
            SetupMockHandlerResolution(typeof(IFailResponseHandler<Dispatcher_TestResponseEventMessage>), new List<object>());

            var mockFallbackHandler = new Dispatcher_MockReactiveStartHandler(); 
            SetupMockHandlerResolution(typeof(ISagaStartHandler<Dispatcher_TestResponseEventMessage>), new List<object> { mockFallbackHandler });
            
            await _dispatcher.DispatchAsync<Dispatcher_TestOriginalCommand, Dispatcher_TestResponseEventMessage>(responseEvent);

            Assert.True(mockFallbackHandler.HandleStartAsyncCalled, "DispatchByMessageTypeAsync should be called."); 
        }
        
        [Fact] // Changed
        public async Task DispatchAsync_NoHandlerFound_ForStartCommand_CompletesSilently()
        {
            var command = new Dispatcher_TestStartCommand();
            SetupMockHandlerResolution(typeof(ISagaStartHandler<Dispatcher_TestStartCommand>), new List<object>()); 

            await _dispatcher.DispatchAsync(command);
            _mockSagaIdGenerator.Verify(g => g.Generate(), Times.Never);
            _mockSagaStore.VerifyNoOtherCalls();
            _mockEventBus.VerifyNoOtherCalls();
        }
        
        [Fact] // Changed
        public async Task DispatchAsync_EventMessage_FallbackToHandleAsyncSpecific()
        {
            var eventMessage = new Dispatcher_TestEventMessage { SagaId = _providedSagaId };
            var specificFallbackHandler = new Dispatcher_SpecificFallbackTestHandler();
             SetupMockHandlerResolution(typeof(ISagaStartHandler<Dispatcher_TestEventMessage>), new List<object> { specificFallbackHandler });

            await _dispatcher.DispatchAsync(eventMessage);

            Assert.True(specificFallbackHandler.HandleAsyncSpecificCalled, "HandleAsync(TMessage) should be called."); // Changed
            Assert.False(specificFallbackHandler.HandleAsyncIMessageCalled, "HandleAsync(IMessage) should not be called if specific is found."); // Changed
            Assert.Equal(eventMessage, specificFallbackHandler.ReceivedMessage); // Changed
        }

        [Fact] // Changed
        public async Task DispatchAsync_EventMessage_FallbackToHandleAsyncIMessage()
        {
            var eventMessage = new Dispatcher_TestEventMessage { SagaId = _providedSagaId };
            var iMessageFallbackHandler = new Dispatcher_IMessageFallbackTestHandler();
            SetupMockHandlerResolution(typeof(ISagaStartHandler<Dispatcher_TestEventMessage>), new List<object> { iMessageFallbackHandler });

            await _dispatcher.DispatchAsync(eventMessage);

            Assert.False(iMessageFallbackHandler.HandleAsyncSpecificCalled, "HandleAsync(TMessage) should not be called."); // Changed
            Assert.True(iMessageFallbackHandler.HandleAsyncIMessageCalled, "HandleAsync(IMessage) should be called."); // Changed
            Assert.Equal(eventMessage, iMessageFallbackHandler.ReceivedMessage); // Changed
        }
    }

    // Handler for testing specific TMessage fallback in HandleAsync
    public class Dispatcher_SpecificFallbackTestHandler : ISagaStartHandler<Dispatcher_TestEventMessage>
    {
        public bool HandleStartAsyncCalled { get; private set; } // Added for interface compliance
        public bool HandleAsyncSpecificCalled { get; private set; }
        public bool HandleAsyncIMessageCalled { get; private set; } // Should not be called
        public IMessage ReceivedMessage { get; private set; }

        public Task HandleStartAsync(Dispatcher_TestEventMessage message) 
        {
            HandleStartAsyncCalled = true; 
            // This method is called if ISagaStartHandler is directly invoked.
            // For fallback testing, ensure test setup correctly leads to HandleAsync.
            return Task.CompletedTask; 
        }
        public Task HandleAsync(Dispatcher_TestEventMessage message) { HandleAsyncSpecificCalled = true; ReceivedMessage = message; return Task.CompletedTask; }
        public Task HandleAsync(IMessage message) { HandleAsyncIMessageCalled = true; ReceivedMessage = message; return Task.CompletedTask; }
    }

    // Handler for testing IMessage fallback in HandleAsync
    public class Dispatcher_IMessageFallbackTestHandler : ISagaStartHandler<Dispatcher_TestEventMessage>
    {
        public bool HandleStartAsyncCalled { get; private set; } // Added for interface compliance
        // NO HandleAsync(Dispatcher_TestEventMessage message) method to force IMessage fallback
        public bool HandleAsyncSpecificCalled { get; private set; } // Should not be called
        public bool HandleAsyncIMessageCalled { get; private set; }
        public IMessage ReceivedMessage { get; private set; }
        
        public Task HandleStartAsync(Dispatcher_TestEventMessage message) 
        {
            HandleStartAsyncCalled = true;
            return Task.CompletedTask;
        }
        public Task HandleAsync(IMessage message) { HandleAsyncIMessageCalled = true; ReceivedMessage = message; return Task.CompletedTask; }
    }
}
