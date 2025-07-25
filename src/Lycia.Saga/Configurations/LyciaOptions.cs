using System;
using System.Collections.Generic;
using System.Text;

namespace Lycia.Saga.Configurations
{
    public class LyciaOptions
    {
#if NETSTANDARD2_0
        public string EventBusProvider { get; set; }
        public string EventStoreProvider { get; set; }
        public string ApplicationId { get; set; }
        public int CommonTtlSeconds { get; set; }
        public string EventStoreConnectionString { get; set; }
        public string EventBusConnectionString { get; set; }
        public int LogMaxRetryCount { get; set; }
#else
        public string EventBusProvider { get; init; }
        public string EventStoreProvider { get; init; }
        public string ApplicationId { get; init; }
        public int CommonTtlSeconds { get; init; }
        public string EventStoreConnectionString { get; init; }
        public string EventBusConnectionString { get; init; }
        public int LogMaxRetryCount { get; init; }
#endif
    }

}
