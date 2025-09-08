// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Extensions.Serialization;

public sealed class MessageSerializationContext
{
    public string ApplicationId { get; set; } = "";
    public string? ExplicitTypeName { get; set; }
}