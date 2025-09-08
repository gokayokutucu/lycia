// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
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
