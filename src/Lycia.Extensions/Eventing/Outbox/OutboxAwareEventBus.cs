using Lycia.Saga.Abstractions;
using Microsoft.Extensions.Options;

namespace Lycia.Extensions.Eventing.Outbox;
//
// public class OutboxAwareEventBus : IEventBus
// {
//     private readonly IEventBus _inner;
//     private readonly IOutboxStore _outbox;
//     private readonly IRetryPolicy _retry;
//     private readonly OutboxOptions _options;
//
//     public OutboxAwareEventBus(IEventBus inner, IOutboxStore outbox, IRetryPolicy retry, IOptions<OutboxOptions> options)
//     {
//         _inner = inner;
//         _outbox = outbox;
//         _retry = retry;
//         _options = options.Value;
//     }
//
//     public async Task PublishAsync<T>(T message, IDictionary<string, string>? headers = null, CancellationToken ct = default)
//     {
//         if (_options.Enabled)
//         {
//             await _retry.ExecuteAsync(() => _outbox.SaveAsync(new OutboxMessage(message, headers), ct));
//         }
//         else
//         {
//             await _retry.ExecuteAsync(() => _inner.PublishAsync(message, headers, ct));
//         }
//     }
//
//     public async Task SendAsync<T>(T message, IDictionary<string, string>? headers = null, CancellationToken ct = default)
//     {
//         if (_options.Enabled)
//         {
//             await _retry.ExecuteAsync(() => _outbox.SaveAsync(new OutboxMessage(message, headers), ct));
//         }
//         else
//         {
//             await _retry.ExecuteAsync(() => _inner.SendAsync(message, headers, ct));
//         }
//     }
// }