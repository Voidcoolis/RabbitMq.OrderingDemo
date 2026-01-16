using Contracts;
using OrderApi.Publisher.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/orders", (RabbitMqPublisher mq) =>
{
    var evt = new OrderCreatedEvent(
        OrderId: Guid.NewGuid(),
        CustomerEmail: "customer@example.com",
        TotalAmount: 49.99m,
        CreatedAtUtc: DateTime.UtcNow
    );

    mq.Publish("orders.created", evt);
    return Results.Ok(evt);
});

app.Run();
