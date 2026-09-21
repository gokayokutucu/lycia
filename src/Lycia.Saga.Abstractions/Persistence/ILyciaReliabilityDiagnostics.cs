// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
namespace Lycia.Saga.Abstractions.Persistence;

/// <summary>
/// Composes a safe <see cref="LyciaReliabilitySnapshot"/> from the persistence stores actually
/// registered for the current application, without introducing a second source of truth alongside
/// <see cref="IPersistenceTopology"/> or the Inbox/Outbox/journal registrations themselves.
/// </summary>
public interface ILyciaReliabilityDiagnostics
{
    /// <summary>Gets the current safe, secret-free reliability/topology snapshot.</summary>
    LyciaReliabilitySnapshot GetSnapshot();
}
