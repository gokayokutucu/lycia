// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Contexts;

namespace Lycia.Saga.Contexts;

public sealed class MessageSerializationContext : IMessageSerializationContext
{
    public string ApplicationId { get; set; } = "";
    public string? ExplicitTypeName { get; set; }
}