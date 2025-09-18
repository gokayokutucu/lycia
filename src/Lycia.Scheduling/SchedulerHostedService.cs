// TargetFramework: netstandard2.0

using Microsoft.Extensions.Hosting;

namespace Lycia.Scheduling
{
    /// <summary>Hosted wrapper that starts/stops the SchedulerLoop with the host lifecycle.</summary>
    public sealed class SchedulerHostedService(SchedulerLoop loop) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
            => loop.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken)
            => loop.StopAsync(cancellationToken);
    }
}