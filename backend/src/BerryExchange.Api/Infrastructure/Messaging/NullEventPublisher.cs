namespace BerryExchange.Api.Infrastructure.Messaging;

// Used when RabbitMq:Host is not configured (tests, bare local dev):
// the marketplace works fully without a broker; enrichment just doesn't happen.
public sealed class NullEventPublisher : IEventPublisher
{
    public Task PublishAsync<T>(string routingKey, T @event, CancellationToken ct) => Task.CompletedTask;
}
