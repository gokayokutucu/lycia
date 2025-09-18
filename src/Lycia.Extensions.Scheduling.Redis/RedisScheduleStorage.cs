// TargetFramework: netstandard2.0

using Lycia.Scheduling;
using Lycia.Scheduling.Abstractions;
using Newtonsoft.Json;
using StackExchange.Redis;
namespace Lycia.Extensions.Scheduling.Redis
{
    public sealed class RedisScheduleStorage(IConnectionMultiplexer mux, string applicationId) : IScheduleStorage
    {
        private readonly IDatabase _db = mux.GetDatabase();
        private readonly string _appKey = "lycia:schedule:" + applicationId;

        public async Task<Guid> EnqueueAsync(ScheduleRequest r, CancellationToken ct = default)
        {
            var id = r.MessageId;
            var entryKey = EntryKey(id);
            var entry = new ScheduledEntry
            {
                Id = id,
                DueTime = r.DueTime,
                Payload = r.Payload,
                MessageType = r.MessageType,
                Headers = new Dictionary<string, object>(r.Headers),
                CorrelationId = r.CorrelationId,
                MessageId = r.MessageId
            };
            // For debugging: serializing SerializableEntry for Redis storage
            var json = JsonConvert.SerializeObject(new SerializableEntry(entry));
            await _db.StringSetAsync(entryKey, json).ConfigureAwait(false);
            var score = ToEpochMs(r.DueTime);
            await _db.SortedSetAddAsync(_appKey, id.ToString(), score).ConfigureAwait(false);
            return id;
        }

        public async Task<IReadOnlyList<ScheduledEntry>> DequeueDueAsync(DateTimeOffset nowUtc, int max,
            CancellationToken ct = default)
        {
            var nowScore = ToEpochMs(nowUtc);
            var ids = await _db.SortedSetRangeByScoreAsync(_appKey, Double.NegativeInfinity, nowScore, Exclude.None, Order.Ascending, 0, max).ConfigureAwait(false);
            var result = new List<ScheduledEntry?>(ids.Length);
            foreach (var id in ids)
            {
                if (id.IsNullOrEmpty) continue;
                if (!Guid.TryParse(id, out var idGuid)) continue;
                var entryKey = EntryKey(idGuid);
                var json = await _db.StringGetAsync(entryKey).ConfigureAwait(false);
                if (json.IsNullOrEmpty)
                {
                    await _db.SortedSetRemoveAsync(_appKey, id).ConfigureAwait(false);
                    continue;
                }
                var ser = JsonConvert.DeserializeObject<SerializableEntry>(json);
                result.Add(ser?.ToEntry());
                await _db.SortedSetRemoveAsync(_appKey, id).ConfigureAwait(false);
            }
            
            return result.Where(x => x != null).OfType<ScheduledEntry>().ToList();
        }

        public Task MarkSucceededAsync(Guid scheduleId, CancellationToken ct = default)
            => _db.KeyDeleteAsync(EntryKey(scheduleId));

        public async Task MarkCancelledAsync(Guid scheduleId, CancellationToken ct = default)
        {
            await _db.SortedSetRemoveAsync(_appKey, scheduleId.ToString()).ConfigureAwait(false);
            await _db.KeyDeleteAsync(EntryKey(scheduleId)).ConfigureAwait(false);
        }

        private static double ToEpochMs(DateTimeOffset dt) => (dt.ToUnixTimeMilliseconds());
        private string EntryKey(Guid id) => _appKey + ":entry:" + id;

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class SerializableEntry
        {
            [JsonProperty] private Guid Id { get; set; }
            [JsonProperty] private long DueTimeMs { get; set; }
            [JsonProperty] private byte[] Payload { get; set; }
            [JsonProperty] private string MessageTypeAssemblyQualifiedName { get; set; }
            [JsonProperty] private Dictionary<string, object> Headers { get; set; }
            [JsonProperty] private Guid CorrelationId { get; set; }
            [JsonProperty] private Guid MessageId { get; set; }

            public SerializableEntry() { }

            public SerializableEntry(ScheduledEntry e)
            {
                Id = e.Id;
                DueTimeMs = e.DueTime.ToUnixTimeMilliseconds();
                Payload = e.Payload;
                MessageTypeAssemblyQualifiedName = e.MessageType.AssemblyQualifiedName;
                Headers = new Dictionary<string, object>(e.Headers);
                CorrelationId = e.CorrelationId;
                MessageId = e.MessageId;
            }

            public ScheduledEntry ToEntry()
            {
                return new ScheduledEntry
                {
                    Id = Id,
                    DueTime = DateTimeOffset.FromUnixTimeMilliseconds(DueTimeMs),
                    Payload = Payload,
                    MessageType = Type.GetType(MessageTypeAssemblyQualifiedName, throwOnError: true),
                    Headers = Headers,
                    CorrelationId = CorrelationId,
                    MessageId = MessageId
                };
            }
        }
    }
}