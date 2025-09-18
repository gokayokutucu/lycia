namespace Lycia.Scheduling.Abstractions;

public interface IScheduler
{
    Task<Guid> ScheduleAsync(ScheduleRequest request, CancellationToken ct = default);
    Task CancelAsync(Guid scheduleId, CancellationToken ct = default);
}