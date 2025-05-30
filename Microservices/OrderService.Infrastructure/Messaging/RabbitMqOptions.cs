namespace OrderService.Infrastructure.Messaging
{
    public class RabbitMqOptions
    {
        public string Hostname { get; set; } = "localhost";
        public int Port { get; set; } = 5672; // Default AMQP port
        public string Username { get; set; } = "guest";
        public string Password { get; set; } = "guest";
        public string ExchangeName { get; set; } = "order_service_exchange"; // Default exchange
        public string VirtualHost { get; set; } = "/";
    }
}
