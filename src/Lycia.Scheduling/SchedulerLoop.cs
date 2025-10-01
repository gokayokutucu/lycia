// TargetFramework: netstandard2.0

using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Serializers;
using Lycia.Scheduling.Abstractions;

namespace Lycia.Scheduling
{
    public sealed class SchedulerLoop(
        IScheduleStorage storage,
        IEventBus eventBus,
        IMessageSerializer serializer,
        TimeSpan? pollInterval = null,
        int? batchSize = null)
        : IDisposable
    {
        private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        private readonly int _batchSize = batchSize ?? 100;
        private Timer? _timer;
        private int _running;

        public Task StartAsync(CancellationToken ct = default)
        {
            _timer = new Timer(OnTick, null, _pollInterval, _pollInterval);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            return Task.CompletedTask;
        }

        private async void OnTick(object? state)
        {
            if (Interlocked.Exchange(ref _running, 1) == 1) return;
            try
            {
                var now = DateTimeOffset.UtcNow;
                var due = await storage.DequeueDueAsync(now, _batchSize).ConfigureAwait(false);
                foreach (var entry in due)
                {
                    var headers = ConvertHeaders(entry.Headers);
                    var schemaId = GetHeaderString(headers, "lycia-schema-id");
                    var schemaVer = GetHeaderString(headers, "lycia-schema-ver");
                    var ctxPair = serializer.CreateContextFor(entry.MessageType, schemaId, schemaVer);
                    var message = serializer.Deserialize(entry.Payload, headers, ctxPair.Ctx);

                    await PublishOrSendAsync(eventBus, message, entry.MessageType, headers, CancellationToken.None).ConfigureAwait(false);
                    await storage.MarkSucceededAsync(entry.Id).ConfigureAwait(false);
                }
            }
            catch
            {
                // Intentionally swallow to keep the loop alive; surface via logging in a decorator if needed
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }

        private static Task PublishOrSendAsync(IEventBus bus, object message, Type messageType, Dictionary<string, object?>? headers, CancellationToken ct)
        {
            var isCommand = headers != null && headers.TryGetValue("lycia-type", out var lyciaType)
                            && lyciaType != null
                            && string.Equals(lyciaType.ToString(), "command", StringComparison.OrdinalIgnoreCase);

            var busType = typeof(IEventBus);
            if (isCommand)
            {
                var send = busType.GetMethod("Send");
                var gm = send?.MakeGenericMethod(messageType);
                return (Task)gm?.Invoke(bus, [message, null, null, ct])!;
            }
            else
            {
                var publish = busType.GetMethod("Publish");
                var gm = publish?.MakeGenericMethod(messageType);
                return (Task)gm?.Invoke(bus, [message, null, null, ct])!;
            }
        }

        private static Dictionary<string, object?> ConvertHeaders(Dictionary<string, object>? src)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (src == null) return dict;
            foreach (var kv in src)
                dict[kv.Key] = kv.Value;
            return dict;
        }

        private static string? GetHeaderString(IReadOnlyDictionary<string, object?>? headers, string key)
        {
            if (headers != null && headers.TryGetValue(key, out var value) && value != null)
                return value.ToString();
            return null;
        }

        public void Dispose()
        {
            var t = _timer;
            t?.Dispose();
        }
    }
}