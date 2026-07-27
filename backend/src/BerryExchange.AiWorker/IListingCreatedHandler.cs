using BerryExchange.Contracts;

namespace BerryExchange.AiWorker;

public interface IListingCreatedHandler
{
    Task HandleAsync(ListingCreatedEvent evt, CancellationToken ct);
}
