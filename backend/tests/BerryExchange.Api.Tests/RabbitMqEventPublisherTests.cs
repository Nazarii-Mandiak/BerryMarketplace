using System.Text.Json;
using BerryExchange.Api.Infrastructure.Messaging;
using BerryExchange.Contracts;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace BerryExchange.Api.Tests;

public class RabbitMqEventPublisherTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder().Build();

    public Task InitializeAsync() => _rabbit.StartAsync();
    public Task DisposeAsync() => _rabbit.DisposeAsync().AsTask();

    [Fact]
    public async Task PublishAsync_delivers_json_event_to_topic_exchange()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = _rabbit.Hostname,
            ["RabbitMq:Port"] = _rabbit.GetMappedPublicPort(5672).ToString(),
            ["RabbitMq:Username"] = "rabbitmq",
            ["RabbitMq:Password"] = "rabbitmq",
        }).Build();

        await using var publisher = new RabbitMqEventPublisher(config);

        // Consumer-side setup: bind a fresh queue to the exchange the publisher declares.
        var factory = new ConnectionFactory
        {
            HostName = _rabbit.Hostname,
            Port = _rabbit.GetMappedPublicPort(5672),
            UserName = "rabbitmq",
            Password = "rabbitmq",
        };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(MessagingConventions.Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        var queue = (await channel.QueueDeclareAsync()).QueueName;
        await channel.QueueBindAsync(queue, MessagingConventions.Exchange, "listing.*");

        var evt = new ListingCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), "Blueberry", "Hilltop",
            5.25m, 4, null, DateTimeOffset.UtcNow);
        await publisher.PublishAsync(ListingCreatedEvent.RoutingKey, evt, CancellationToken.None);

        BasicGetResult? delivery = null;
        for (var i = 0; i < 50 && delivery is null; i++)
        {
            delivery = await channel.BasicGetAsync(queue, autoAck: true);
            if (delivery is null) await Task.Delay(100);
        }

        Assert.NotNull(delivery);
        var roundTripped = JsonSerializer.Deserialize<ListingCreatedEvent>(delivery!.Body.ToArray());
        Assert.Equal(evt.ListingId, roundTripped!.ListingId);
        Assert.Equal("Blueberry", roundTripped.BerryType);
    }
}
