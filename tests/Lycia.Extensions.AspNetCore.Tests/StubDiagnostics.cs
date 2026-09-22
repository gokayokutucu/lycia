// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Saga.Abstractions.Persistence;

namespace Lycia.Extensions.AspNetCore.Tests;

/// <summary>
/// A test double for <see cref="ILyciaReliabilityDiagnostics"/> that returns a fixed snapshot. Used to
/// prove the endpoint is a faithful projection of whatever <see cref="ILyciaReliabilityDiagnostics"/>
/// reports, without needing AddLycia/UsePersistence or any real infrastructure to construct each
/// representative topology (Standard/LocalAtomic/Independent/Split Store).
/// </summary>
internal sealed class StubDiagnostics(LyciaReliabilitySnapshot snapshot) : ILyciaReliabilityDiagnostics
{
    public int CallCount { get; private set; }

    public LyciaReliabilitySnapshot GetSnapshot()
    {
        CallCount++;
        return snapshot;
    }
}
