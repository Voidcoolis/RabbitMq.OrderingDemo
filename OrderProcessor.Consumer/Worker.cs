using Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace OrderProcessor.Consumer;

public sealed class Worker : BackgroundService
{
    private IConnection? _connection;
    private IModel? _channel;
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger) => _logger = logger;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

        // ✅ Ensure topology exists (safe to run multiple times)
        _channel.ExchangeDeclare("orders.exchange", ExchangeType.Direct, durable: true);
        _channel.ExchangeDeclare("orders.dlx", ExchangeType.Direct, durable: true);

        // DLQ
        _channel.QueueDeclare("orders.dlq", durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind("orders.dlq", "orders.dlx", routingKey: "orders.dead");

        // Retry queue (TTL -> back to main exchange)
        var retryArgs = new Dictionary<string, object>
        {
            ["x-message-ttl"] = 10000, // 10 seconds
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

        // QoS
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 5, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += HandleAsync;

        _channel.BasicConsume(
            queue: "orders.created.q",
            autoAck: false,
            consumer: consumer
        );

        _logger.LogInformation("Consumer listening on orders.created.q");
        return Task.CompletedTask;
    }

    private async Task HandleAsync(object sender, BasicDeliverEventArgs ea)
    {
        if (_channel is null) return;

        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            _logger.LogInformation("RAW JSON: {Json}", json);

            var evt = JsonSerializer.Deserialize<OrderCreatedEvent>(json)
                      ?? throw new InvalidOperationException("Invalid message payload.");

            _logger.LogInformation("Processing OrderId={OrderId} Total={Total}", evt.OrderId, evt.TotalAmount);

            // Simulate occasional failure to demonstrate retry
            if (evt.TotalAmount > 40m && Random.Shared.Next(0, 3) == 0)
                throw new Exception("Simulated processing failure");

            await Task.Delay(300);

            _channel.BasicAck(ea.DeliveryTag, multiple: false);
            _logger.LogInformation("Processed OK OrderId={OrderId}", evt.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed. Sending to retry queue.");

            // Put into retry queue; TTL will route it back to main
            _channel.BasicPublish(
                exchange: "",
                routingKey: "orders.retry",
                basicProperties: ea.BasicProperties,
                body: ea.Body
            );

            _channel.BasicAck(ea.DeliveryTag, multiple: false);
        }
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
