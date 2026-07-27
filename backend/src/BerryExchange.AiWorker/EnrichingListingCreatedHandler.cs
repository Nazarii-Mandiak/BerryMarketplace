using BerryExchange.AiCore;
using BerryExchange.Contracts;

namespace BerryExchange.AiWorker;

public sealed class EnrichingListingCreatedHandler : IListingCreatedHandler
{
    private readonly ITextEmbedder _embedder;
    private readonly EnrichmentApiClient _api;
    private readonly ILogger<EnrichingListingCreatedHandler> _logger;

    public EnrichingListingCreatedHandler(ITextEmbedder embedder, EnrichmentApiClient api,
        ILogger<EnrichingListingCreatedHandler> logger)
    {
        _embedder = embedder;
        _api = api;
        _logger = logger;
    }

    public async Task HandleAsync(ListingCreatedEvent evt, CancellationToken ct)
    {
        var text = $"{evt.BerryType} from {evt.FarmName}. {evt.Note}".Trim();
        var embedding = _embedder.Embed(text);
        await _api.SendAsync(evt.ListingId, embedding, tastingNotes: null, ct); // notes: Task 18
        _logger.LogInformation("Enriched listing {ListingId}", evt.ListingId);
    }
}
