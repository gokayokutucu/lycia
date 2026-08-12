// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Saga.Abstractions.Persistence.Journal;

namespace Lycia.Extensions.Journal;

/// <summary>Result of attempting to bring one journal entry up to the reducer's supported schema version.</summary>
public sealed class JournalUpcastResult
{
    public bool Succeeded { get; set; }
    public SagaJournalEntry? Entry { get; set; }
    public string? FailureReason { get; set; }

    public static JournalUpcastResult Success(SagaJournalEntry entry) => new() { Succeeded = true, Entry = entry };
    public static JournalUpcastResult Failure(string reason) => new() { Succeeded = false, FailureReason = reason };
}

/// <summary>
/// Applies registered <see cref="IJournalEntryUpcaster"/>s in sequence until an entry reaches
/// <see cref="SagaJournalSchema.CurrentVersion"/>. Deterministic and side-effect-free — never mutates
/// the persisted historical record, only the in-memory value used for this reduction.
/// </summary>
public sealed class JournalEntryUpcastChain
{
    private readonly IReadOnlyDictionary<int, IJournalEntryUpcaster> _upcastersByFromVersion;

    public JournalEntryUpcastChain(IEnumerable<IJournalEntryUpcaster> upcasters)
    {
        _upcastersByFromVersion = (upcasters ?? throw new ArgumentNullException(nameof(upcasters)))
            .ToDictionary(u => u.FromSchemaVersion);
    }

    public JournalUpcastResult Upcast(SagaJournalEntry entry)
    {
        var current = entry;
        var guard = 0;
        while (current.JournalSchemaVersion < SagaJournalSchema.CurrentVersion)
        {
            if (guard++ > 64)
                return JournalUpcastResult.Failure(
                    $"Upcast chain for saga {entry.SagaId} version {entry.TargetVersion} did not converge (possible cyclic upcaster registration).");

            if (!_upcastersByFromVersion.TryGetValue(current.JournalSchemaVersion, out var upcaster))
            {
                return JournalUpcastResult.Failure(
                    $"No upcaster registered for journal schema version {current.JournalSchemaVersion} " +
                    $"(saga {entry.SagaId}, version {entry.TargetVersion}). Rebuild cannot proceed safely.");
            }

            current = upcaster.Upcast(current);
        }

        return JournalUpcastResult.Success(current);
    }
}
