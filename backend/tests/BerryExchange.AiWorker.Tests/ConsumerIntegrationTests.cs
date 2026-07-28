using System.Text.Json;
using BerryExchange.AiWorker;
using BerryExchange.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace BerryExchange.AiWorker.Tests;

public sealed class RecordingHandler : IListingCreatedHandler
{
    public TaskCompletionSource<ListingCreatedEvent> Received { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task HandleAsync(ListingCreatedEvent evt, CancellationToken ct)
    {
        Received.TrySetResult(evt);
        return Task.CompletedTask;
    }
}

public class ConsumerIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management").Build();

    public Task InitializeAsync() => _rabbit.StartAsync();
    public Task DisposeAsync() => _rabbit.DisposeAsync().AsTask();

    [Fact]
    public async Task Consumer_receives_published_listing_created_event()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = _rabbit.Hostname,
            ["RabbitMq:Port"] = _rabbit.GetMappedPublicPort(5672).ToString(),
            ["RabbitMq:Username"] = "rabbitmq",
            ["RabbitMq:Password"] = "rabbitmq",
        }).Build();

        var handler = new RecordingHandler();
        var consumer = new RabbitMqConsumerService(config, handler, NullLogger<RabbitMqConsumerService>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await consumer.StartAsync(cts.Token);

        // Publish directly to the exchange the consumer declared.
        var factory = new ConnectionFactory
        {
            HostName = _rabbit.Hostname,
            Port = _rabbit.GetMappedPublicPort(5672),
            UserName = "rabbitmq",
            Password = "rabbitmq",
        };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var evt = new ListingCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), "Raspberry", "Bramble Row",
            7m, 3, "tart", DateTimeOffset.UtcNow);
        await channel.BasicPublishAsync(MessagingConventions.Exchange, ListingCreatedEvent.RoutingKey,
            mandatory: false,
            basicProperties: new BasicProperties { ContentType = "application/json" },
            body: JsonSerializer.SerializeToUtf8Bytes(evt));

        var received = await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(evt.ListingId, received.ListingId);

        await consumer.StopAsync(CancellationToken.None);
    }
}
