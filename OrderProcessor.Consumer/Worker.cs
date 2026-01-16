using Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace OrderProcessor.Consumer;

/// <summary>
/// Background worker that consumes OrderCreatedEvent messages from RabbitMQ.
/// Demonstrates: durable topology, manual ACK, QoS/prefetch, retry queue (TTL), and DLQ.
/// </summary>
public sealed class Worker : BackgroundService
{
    private IConnection? _connection;
    private IModel? _channel;
    private readonly ILogger<Worker> _logger;

    // Allow camelCase or PascalCase JSON from different publishers
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Worker(ILogger<Worker> logger) => _logger = logger;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Connect to RabbitMQ
        var factory = new ConnectionFactory
        {
            HostName = "localhost",
            UserName = "guest",
            Password = "guest",
            VirtualHost = "/",
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        // Declare exchanges (safe if already exist)
        _channel.ExchangeDeclare("orders.exchange", ExchangeType.Direct, durable: true);
        _channel.ExchangeDeclare("orders.dlx", ExchangeType.Direct, durable: true);

        // Dead-letter queue
        _channel.QueueDeclare("orders.dlq", true, false, false);
        _channel.QueueBind("orders.dlq", "orders.dlx", "orders.dead");

        // Retry queue (TTL-based delay)
        var retryArgs = new Dictionary<string, object>
        {
            ["x-message-ttl"] = 10000,
            ["x-dead-letter-exchange"] = "orders.exchange",
            ["x-dead-letter-routing-key"] = "orders.created"
        };
        _channel.QueueDeclare("orders.retry", true, false, false, retryArgs);

        // Main queue with DLX settings
        var mainArgs = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = "orders.dlx",
            ["x-dead-letter-routing-key"] = "orders.dead"
        };
        _channel.QueueDeclare("orders.created.q", true, false, false, mainArgs);
        _channel.QueueBind("orders.created.q", "orders.exchange", "orders.created");

        // Limit unacked messages
        _channel.BasicQos(0, 5, false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += HandleAsync;

        // Manual ACK mode
        _channel.BasicConsume("orders.created.q", false, consumer);

        _logger.LogInformation("Consumer listening on orders.created.q");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles a single RabbitMQ message.
    /// On success: ACK message.
    /// On failure: publish to retry queue (delayed retry), then ACK original message.
    /// </summary>
    private async Task HandleAsync(object sender, BasicDeliverEventArgs ea)
    {
        if (_channel is null) return;

        var json = Encoding.UTF8.GetString(ea.Body.ToArray());

        try
        {
            _logger.LogInformation("RAW JSON: {Json}", json);

            // Deserialize using case-insensitive options to support different JSON casing
            var evt = JsonSerializer.Deserialize<Contracts.OrderCreatedEvent>(json, JsonOptions)
                      ?? throw new InvalidOperationException("Invalid payload (null).");

            _logger.LogInformation("Deserialized type: {Type}", evt.GetType().FullName);

            // Fail fast on default values (usually indicates invalid payload/schema)
            if (evt.OrderId == Guid.Empty || evt.TotalAmount <= 0)
                throw new InvalidOperationException("Deserialized event contains default values.");

            _logger.LogInformation(
                "Processing OrderId={OrderId} Total={Total}",
                evt.OrderId,
                evt.TotalAmount
            );

            await Task.Delay(300);

            // ACK only after successful processing
            _channel.BasicAck(ea.DeliveryTag, false);
            _logger.LogInformation("Processed OK OrderId={OrderId}", evt.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed. RAW JSON: {Json}", json);

            // Send to retry queue (delayed retry)
            _channel.BasicPublish(
                exchange: "",
                routingKey: "orders.retry",
                basicProperties: ea.BasicProperties,
                body: ea.Body
            );

            // ACK original message
            _channel.BasicAck(ea.DeliveryTag, false);
        }
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
