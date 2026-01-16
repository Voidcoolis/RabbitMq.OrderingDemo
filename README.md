# RabbitMQ Order Processing Demo (.NET)

This project demonstrates an event-driven messaging architecture using RabbitMQ with ASP.NET Core and a .NET Worker Service.

The solution contains a publisher API that sends order-related events to RabbitMQ and a background consumer that processes those events asynchronously. The project focuses on reliability, fault tolerance, and proper message handling using retry and dead-letter queues.

---

## Architecture Overview

The message flow is as follows:

1. A client sends a request to the Publisher API (`POST /orders`)
2. The API publishes an `OrderCreated` event to a RabbitMQ exchange
3. A Worker Service consumes messages from a queue and processes them
4. If processing fails, the message is routed to a retry queue with a delay
5. Messages that fail permanently are sent to a Dead Letter Queue (DLQ)

RabbitMQ topology used in the project:

- `orders.exchange` – main direct exchange for order events
- `orders.created.q` – main consumer queue
- `orders.retry` – retry queue using TTL-based delay
- `orders.dlx` – dead-letter exchange
- `orders.dlq` – dead-letter queue

---

## Technology Stack

- .NET 8 (ASP.NET Core Web API, Worker Service)
- C#
- RabbitMQ
- Docker (for running RabbitMQ locally)
- System.Text.Json
- Swagger / OpenAPI
- Microsoft.Extensions.Logging

---

## Solution Structure
RabbitMq.OrderingDemo
│
├── OrderApi.Publisher
│ ├── Messaging
│ │ └── RabbitMqPublisher.cs
│ ├── Contracts
│ │ └── OrderCreatedEvent.cs
│ └── Program.cs
│
├── OrderProcessor.Consumer
│ ├── Contracts
│ │ └── OrderCreatedEvent.cs
│ └── Worker.cs
│
└── README.md


---

## Prerequisites

- .NET 8 SDK (or .NET 6+)
- Docker Desktop
- Visual Studio 2022 (recommended)

---

## Running RabbitMQ

Run RabbitMQ locally with the management UI using Docker:

```bash
docker run -d --hostname rabbit-local --name rabbitmq \
  -p 5672:5672 \
  -p 15672:15672 \
  rabbitmq:3-management
```
## RabbitMQ Management UI:
- URL: http://localhost:15672
- Username: guest
- Password: guest

## Running the Application
1. Open the solution in Visual Studio
2. Configure multiple startup projects:
  - OrderApi.Publisher → Start
  - OrderProcessor.Consumer → Start
3. Run the solution using F5

The Publisher API will start and expose Swagger UI in the browser.

## Testing the Application
### Publishing messages via Swagger
- Open Swagger UI
- Execute POST /orders
- The API publishes an order event to RabbitMQ

### Observing the Consumer
- The Worker Service logs:
  - received messages
  - successful processing
  - retry attempts in case of failure

### Inspecting RabbitMQ
- Open RabbitMQ Management UI
- Monitor the following queues:
  - orders.created.q
  - orders.retry
  - orders.dlq

### Messages can also be published manually from the RabbitMQ UI using:
- Exchange: orders.exchange
- Routing key: orders.created
- Payload: JSON matching the message contract

  ### Message Contract
  Example OrderCreatedEvent payload:
  ```json
  {
  "orderId": "71e5298d-9eef-4078-a959-efd22a2cb210",
  "customerEmail": "customer@example.com",
  "totalAmount": 79.50,
  "createdAtUtc": "2026-01-16T17:45:00Z"
  }

JSON deserialization is configured to be case-insensitive, allowing both camelCase and PascalCase payloads.

### Reliability Features
- Durable exchanges and queues
- Manual message acknowledgements
- QoS / prefetch limits
- Retry queue with TTL-based delay
- Dead-letter queue (DLQ)
- Idempotent topology declaration
- Asynchronous background processing

##Purpose of the Project

This project was created as a portfolio and learning exercise to demonstrate practical usage of RabbitMQ in a .NET environment. It showcases common messaging patterns used in production systems, including retries, dead-letter queues, and decoupled services.
The implementation is intentionally simple but realistic.


### Possible Improvements
- Configurable retry limits
- Retry counter using message headers
- Externalized configuration via appsettings.json
- Structured logging (e.g., Serilog)
- Health checks and monitoring
