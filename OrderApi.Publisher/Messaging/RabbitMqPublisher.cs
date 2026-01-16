using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace OrderApi.Publisher.Messaging;

/// <summary>
/// RabbitMQ publisher used by the API to emit domain events.
/// Declares a durable topology (exchange/queues) and publishes persistent JSON messages.
/// </summary>
public sealed class RabbitMqPublisher : IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public RabbitMqPublisher()
    {
        // RabbitMQ connection settings (local Docker container)
        var factory = new ConnectionFactory
        {
            HostName = "localhost",
            UserName = "guest",
            Password = "guest",
            VirtualHost = "/"
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        // Main exchange for publishing order events
        _channel.ExchangeDeclare("orders.exchange", ExchangeType.Direct, durable: true);
        _channel.ExchangeDeclare("orders.dlx", ExchangeType.Direct, durable: true);

        // Dead-letter queue(DLQ)
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

    /// <summary>
    /// Publishes a message to the main exchange with the provided routing key.
    /// Messages are sent as JSON with persistent delivery mode.
    /// </summary>
    public void Publish<T>(string routingKey, T message)
    {
        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        // Persistent message so it can survive broker restart (requires durable queues/exchanges)
        var props = _channel.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = "application/json";

        // Publish to the direct exchange. Routing key determines which queue(s) receive it.
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
