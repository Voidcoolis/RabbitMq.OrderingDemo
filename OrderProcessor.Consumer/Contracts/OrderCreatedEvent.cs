using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Contracts
{
    public sealed record OrderCreatedEvent(
        Guid OrderId,
        string CustomerEmail,
        decimal TotalAmount,
        DateTime CreatedAtUtc
    );
}
