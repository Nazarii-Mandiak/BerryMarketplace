namespace BerryExchange.Api.Infrastructure.Messaging;

public interface IEventPublisher
{
    Task PublishAsync<T>(string routingKey, T @event, CancellationToken ct);
}
