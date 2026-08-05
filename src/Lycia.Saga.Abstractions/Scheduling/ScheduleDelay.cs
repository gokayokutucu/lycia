// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

namespace Lycia.Saga.Abstractions.Scheduling;

/// <summary>Recommended fixed-duration buckets for transport-independent message scheduling.</summary>
/// <remarks>Month values are fixed 30-day durations and <see cref="OneYear"/> is 365 days. Use ScheduleAt for calendar arithmetic.</remarks>
public enum ScheduleDelay
{
    /// <summary>Five seconds.</summary>
    FiveSeconds,
    /// <summary>Thirty seconds.</summary>
    ThirtySeconds,
    /// <summary>One minute.</summary>
    OneMinute,
    /// <summary>Five minutes.</summary>
    FiveMinutes,
    /// <summary>Fifteen minutes.</summary>
    FifteenMinutes,
    /// <summary>Thirty minutes.</summary>
    ThirtyMinutes,
    /// <summary>One hour.</summary>
    OneHour,
    /// <summary>Six hours.</summary>
    SixHours,
    /// <summary>Twelve hours.</summary>
    TwelveHours,
    /// <summary>One day.</summary>
    OneDay,
    /// <summary>One week.</summary>
    OneWeek,
    /// <summary>Thirty days.</summary>
    OneMonth,
    /// <summary>Sixty days.</summary>
    TwoMonths,
    /// <summary>Ninety days.</summary>
    ThreeMonths,
    /// <summary>One hundred twenty days.</summary>
    FourMonths,
    /// <summary>One hundred fifty days.</summary>
    FiveMonths,
    /// <summary>One hundred eighty days.</summary>
    SixMonths,
    /// <summary>Two hundred ten days.</summary>
    SevenMonths,
    /// <summary>Two hundred forty days.</summary>
    EightMonths,
    /// <summary>Two hundred seventy days.</summary>
    NineMonths,
    /// <summary>Three hundred days.</summary>
    TenMonths,
    /// <summary>Three hundred thirty days.</summary>
    ElevenMonths,
    /// <summary>Three hundred sixty-five days.</summary>
    OneYear
}

/// <summary>Resolves predefined scheduling buckets to fixed durations and canonical topology suffixes.</summary>
public static class ScheduleDelayResolver
{
    /// <summary>Returns the fixed duration represented by <paramref name="delay"/>.</summary>
    public static TimeSpan GetDuration(ScheduleDelay delay)
    {
        switch (delay)
        {
            case ScheduleDelay.FiveSeconds: return TimeSpan.FromSeconds(5);
            case ScheduleDelay.ThirtySeconds: return TimeSpan.FromSeconds(30);
            case ScheduleDelay.OneMinute: return TimeSpan.FromMinutes(1);
            case ScheduleDelay.FiveMinutes: return TimeSpan.FromMinutes(5);
            case ScheduleDelay.FifteenMinutes: return TimeSpan.FromMinutes(15);
            case ScheduleDelay.ThirtyMinutes: return TimeSpan.FromMinutes(30);
            case ScheduleDelay.OneHour: return TimeSpan.FromHours(1);
            case ScheduleDelay.SixHours: return TimeSpan.FromHours(6);
            case ScheduleDelay.TwelveHours: return TimeSpan.FromHours(12);
            case ScheduleDelay.OneDay: return TimeSpan.FromDays(1);
            case ScheduleDelay.OneWeek: return TimeSpan.FromDays(7);
            case ScheduleDelay.OneMonth: return TimeSpan.FromDays(30);
            case ScheduleDelay.TwoMonths: return TimeSpan.FromDays(60);
            case ScheduleDelay.ThreeMonths: return TimeSpan.FromDays(90);
            case ScheduleDelay.FourMonths: return TimeSpan.FromDays(120);
            case ScheduleDelay.FiveMonths: return TimeSpan.FromDays(150);
            case ScheduleDelay.SixMonths: return TimeSpan.FromDays(180);
            case ScheduleDelay.SevenMonths: return TimeSpan.FromDays(210);
            case ScheduleDelay.EightMonths: return TimeSpan.FromDays(240);
            case ScheduleDelay.NineMonths: return TimeSpan.FromDays(270);
            case ScheduleDelay.TenMonths: return TimeSpan.FromDays(300);
            case ScheduleDelay.ElevenMonths: return TimeSpan.FromDays(330);
            case ScheduleDelay.OneYear: return TimeSpan.FromDays(365);
            default: throw new ArgumentOutOfRangeException(nameof(delay), delay, "Unknown schedule delay bucket.");
        }
    }

    /// <summary>Returns the stable transport-topology suffix represented by <paramref name="delay"/>.</summary>
    public static string GetSuffix(ScheduleDelay delay)
    {
        switch (delay)
        {
            case ScheduleDelay.FiveSeconds: return "5s";
            case ScheduleDelay.ThirtySeconds: return "30s";
            case ScheduleDelay.OneMinute: return "1m";
            case ScheduleDelay.FiveMinutes: return "5m";
            case ScheduleDelay.FifteenMinutes: return "15m";
            case ScheduleDelay.ThirtyMinutes: return "30m";
            case ScheduleDelay.OneHour: return "1h";
            case ScheduleDelay.SixHours: return "6h";
            case ScheduleDelay.TwelveHours: return "12h";
            case ScheduleDelay.OneDay: return "1d";
            case ScheduleDelay.OneWeek: return "1w";
            case ScheduleDelay.OneMonth: return "1mo";
            case ScheduleDelay.TwoMonths: return "2mo";
            case ScheduleDelay.ThreeMonths: return "3mo";
            case ScheduleDelay.FourMonths: return "4mo";
            case ScheduleDelay.FiveMonths: return "5mo";
            case ScheduleDelay.SixMonths: return "6mo";
            case ScheduleDelay.SevenMonths: return "7mo";
            case ScheduleDelay.EightMonths: return "8mo";
            case ScheduleDelay.NineMonths: return "9mo";
            case ScheduleDelay.TenMonths: return "10mo";
            case ScheduleDelay.ElevenMonths: return "11mo";
            case ScheduleDelay.OneYear: return "1y";
            default: throw new ArgumentOutOfRangeException(nameof(delay), delay, "Unknown schedule delay bucket.");
        }
    }
}
