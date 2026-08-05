// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions.Scheduling;

namespace Lycia.Scheduling;

/// <summary>System UTC clock used in production.</summary>
public sealed class SystemSchedulingClock : ISchedulingClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Thread-safe manually advanced UTC clock for deterministic tests and in-memory hosts.</summary>
public sealed class ManualSchedulingClock : ISchedulingClock
{
    private readonly object _gate = new();
    private DateTimeOffset _utcNow;

    /// <summary>Creates a clock at the supplied UTC-normalized instant.</summary>
    public ManualSchedulingClock(DateTimeOffset initialUtc) => _utcNow = initialUtc.ToUniversalTime();

    /// <inheritdoc />
    public DateTimeOffset UtcNow
    {
        get { lock (_gate) return _utcNow; }
    }

    /// <summary>Advances the clock by a non-negative duration.</summary>
    public void Advance(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        lock (_gate) _utcNow = _utcNow.Add(duration);
    }

    /// <summary>Sets the clock to a UTC-normalized instant without allowing time to move backwards.</summary>
    public void Set(DateTimeOffset utcNow)
    {
        var normalized = utcNow.ToUniversalTime();
        lock (_gate)
        {
            if (normalized < _utcNow) throw new ArgumentOutOfRangeException(nameof(utcNow), "Scheduling clocks cannot move backwards.");
            _utcNow = normalized;
        }
    }
}
