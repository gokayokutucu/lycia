namespace Lycia.Dapr.EventBus.Sqs;

    public class AmazonSqsEventSubscriberOptions
    {
        public string AccessKeyId { get; set; }
        public string SecretAccessKey { get; set; }
        public string Url { get; set; }
        public int MaxNumberOfMessages { get; set; }
        public int WaitTimeSeconds { get; set; }
    }