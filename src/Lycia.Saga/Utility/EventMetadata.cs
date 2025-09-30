// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Reflection;

namespace Lycia.Saga.Utility;

public static class EventMetadata
{
    public static string ApplicationId { get; set; } = Assembly.GetEntryAssembly()?.GetName().Name ?? "Unknown";
}