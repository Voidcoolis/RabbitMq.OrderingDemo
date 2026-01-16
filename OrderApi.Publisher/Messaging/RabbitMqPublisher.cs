using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace OrderApi.Publisher.Messaging;

public sealed class RabbitMqPublisher : IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public RabbitMqPublisher()
    {
        var factory = new ConnectionFactory
        {
            HostName = "localhost",
            UserName = "guest",
            Password = "guest",
            VirtualHost = "/"
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        // Exchanges
        _channel.ExchangeDeclare("orders.exchange", ExchangeType.Direct, durable: true);
        _channel.ExchangeDeclare("orders.dlx", ExchangeType.Direct, durable: true);

        // DLQ
        _channel.QueueDeclare("orders.dlq", durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind("orders.dlq", "orders.dlx", routingKey: "orders.dead");

        // Retry queue (TTL -> back to main exchange)
        var retryArgs = new Dictionary<string, object>
        {
            ["x-message-ttl"] = 10000, // 10s delay
            ["x-dead-letter-exchange"] = "orders.exchange",
            ["x-dead-letter-routing-key"] = "orders.created"
        };
        _channel.QueueDeclare("orders.retry", durable: true, exclusive: false, autoDelete: false, arguments: retryArgs);

        // Main queue dead-letters to DLX
        var mainArgs = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = "orders.dlx",
            ["x-dead-letter-routing-key"] = "orders.dead"
        };
        _channel.QueueDeclare("orders.created.q", durable: true, exclusive: false, autoDelete: false, arguments: mainArgs);
        _channel.QueueBind("orders.created.q", "orders.exchange", routingKey: "orders.created");
    }

    public void Publish<T>(string routingKey, T message)
    {
        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        var props = _channel.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = "application/json";

        _channel.BasicPublish(
            exchange: "orders.exchange",
            routingKey: routingKey,
            basicProperties: props,
            body: body
        );
    }

    public void Dispose()
    {
        _channel.Dispose();
        _connection.Dispose();
    }
}
