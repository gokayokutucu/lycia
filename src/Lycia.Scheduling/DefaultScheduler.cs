// TargetFramework: netstandard2.0

using Lycia.Scheduling.Abstractions;

namespace Lycia.Scheduling
{
    public sealed class DefaultScheduler(IScheduleStorage storage) : IScheduler
    {
        public Task<Guid> ScheduleAsync(ScheduleRequest request, CancellationToken ct = default(CancellationToken))
            => storage.EnqueueAsync(request, ct);

        public Task CancelAsync(Guid scheduleId, CancellationToken ct = default(CancellationToken))
            => storage.MarkCancelledAsync(scheduleId, ct);
    }
}