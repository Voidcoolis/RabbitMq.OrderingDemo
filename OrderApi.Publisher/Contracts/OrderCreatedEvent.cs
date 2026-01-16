namespace Contracts
{
    public sealed record OrderCreatedEvent(
        Guid OrderId,
        string CustomerEmail,
        decimal TotalAmount,
        DateTime CreatedAtUtc
    );
}
