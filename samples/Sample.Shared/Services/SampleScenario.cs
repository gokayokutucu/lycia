// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

namespace Sample.Shared.Services;

/// <summary>Development-only switches used to exercise sample success and compensation paths.</summary>
public static class SampleScenario
{
    public static bool FailPayment => IsEnabled("LYCIA_SAMPLE_FAIL_PAYMENT");
    public static bool FailShipping => IsEnabled("LYCIA_SAMPLE_FAIL_SHIPPING");

    private static bool IsEnabled(string name) =>
        string.Equals(Environment.GetEnvironmentVariable(name), "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);
}
