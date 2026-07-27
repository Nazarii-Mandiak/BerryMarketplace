using BerryExchange.Contracts;

namespace BerryExchange.AiWorker;

// Phase-2 placeholder behavior: proves the pipeline end to end.
// Task 12 replaces this registration with the enrichment handler.
public sealed class LoggingListingCreatedHandler : IListingCreatedHandler
{
    private readonly ILogger<LoggingListingCreatedHandler> _logger;
    public LoggingListingCreatedHandler(ILogger<LoggingListingCreatedHandler> logger) => _logger = logger;

    public Task HandleAsync(ListingCreatedEvent evt, CancellationToken ct)
    {
        _logger.LogInformation("Received listing.created: {BerryType} from {FarmName} ({ListingId})",
            evt.BerryType, evt.FarmName, evt.ListingId);
        return Task.CompletedTask;
    }
}
