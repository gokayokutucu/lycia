using Lycia.Messaging;

namespace Lycia.Tests.Helpers;

public class DummySagaData : SagaData
{
    public string SomeField { get; set; } = "test";
}
    
public class DummyHandler
{
}

public class DummyStep : EventBase
{
}
