namespace Lycia.Scheduling.Abstractions;

public interface IScheduleStorage
{
    Task<Guid> EnqueueAsync(ScheduleRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ScheduledEntry>> DequeueDueAsync(DateTimeOffset nowUtc, int max, CancellationToken ct = default);
    Task MarkSucceededAsync(Guid scheduleId, CancellationToken ct = default);
    Task MarkCancelledAsync(Guid scheduleId, CancellationToken ct = default);
}