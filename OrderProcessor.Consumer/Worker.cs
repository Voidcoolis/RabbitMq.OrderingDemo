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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

        _channel.ExchangeDeclare("orders.exchange", ExchangeType.Direct, durable: true);
        _channel.ExchangeDeclare("orders.dlx", ExchangeType.Direct, durable: true);

        _channel.QueueDeclare("orders.dlq", true, false, false);
        _channel.QueueBind("orders.dlq", "orders.dlx", "orders.dead");

        var retryArgs = new Dictionary<string, object>
        {
            ["x-message-ttl"] = 10000,
            ["x-dead-letter-exchange"] = "orders.exchange",
            ["x-dead-letter-routing-key"] = "orders.created"
        };
        _channel.QueueDeclare("orders.retry", true, false, false, retryArgs);

        var mainArgs = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = "orders.dlx",
            ["x-dead-letter-routing-key"] = "orders.dead"
        };
        _channel.QueueDeclare("orders.created.q", true, false, false, mainArgs);
        _channel.QueueBind("orders.created.q", "orders.exchange", "orders.created");

        _channel.BasicQos(0, 5, false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += HandleAsync;

        _channel.BasicConsume("orders.created.q", false, consumer);

        _logger.LogInformation("Consumer listening on orders.created.q");
        return Task.CompletedTask;
    }

    private async Task HandleAsync(object sender, BasicDeliverEventArgs ea)
    {
        if (_channel is null) return;

        var json = Encoding.UTF8.GetString(ea.Body.ToArray());

        try
        {
            _logger.LogInformation("RAW JSON: {Json}", json);

            var evt = JsonSerializer.Deserialize<Contracts.OrderCreatedEvent>(json, JsonOptions)
                      ?? throw new InvalidOperationException("Invalid payload (null).");

            _logger.LogInformation("Deserialized type: {Type}", evt.GetType().FullName);

            if (evt.OrderId == Guid.Empty || evt.TotalAmount <= 0)
                throw new InvalidOperationException("Deserialized event contains default values.");

            _logger.LogInformation(
                "Processing OrderId={OrderId} Total={Total}",
                evt.OrderId,
                evt.TotalAmount
            );

            await Task.Delay(300);

            _channel.BasicAck(ea.DeliveryTag, false);
            _logger.LogInformation("Processed OK OrderId={OrderId}", evt.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed. RAW JSON: {Json}", json);

            _channel.BasicPublish(
                exchange: "",
                routingKey: "orders.retry",
                basicProperties: ea.BasicProperties,
                body: ea.Body
            );

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
