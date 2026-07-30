using BerryExchange.Api.Infrastructure;
using BerryExchange.Api.Infrastructure.Messaging;
using BerryExchange.AiCore;
using BerryExchange.Contracts;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace BerryExchange.Api.Listings;

public class ListingsService
{
    private readonly BerryExchangeDbContext _db;
    private readonly IEventPublisher _events;
    private readonly ILogger<ListingsService> _logger;
    private readonly ITextEmbedder _embedder;

    public ListingsService(BerryExchangeDbContext db, IEventPublisher events, ILogger<ListingsService> logger, ITextEmbedder embedder)
    {
        _db = db;
        _events = events;
        _logger = logger;
        _embedder = embedder;
    }

    public async Task<List<Listing>> GetAllAsync(CancellationToken ct)
    {
        return await _db.Listings.OrderByDescending(l => l.CreatedAt).ToListAsync(ct);
    }

    public async Task<Listing?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await _db.Listings.FirstOrDefaultAsync(l => l.Id == id, ct);
    }

    public async Task<List<Listing>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        // IgnoreQueryFilters: this backs /api/reservations/mine, which must still show a
        // buyer's past reservation details even after the seller has deleted the listing -
        // the only caller of this method, so the exception lives here rather than at each
        // call site.
        return await _db.Listings.IgnoreQueryFilters().Where(l => ids.Contains(l.Id)).ToListAsync(ct);
    }

    public async Task<Listing> CreateAsync(Guid sellerId, CreateListingRequest request, CancellationToken ct)
    {
        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            SellerId = sellerId,
            BerryType = request.BerryType,
            FarmName = request.FarmName,
            PricePerKg = request.PricePerKg,
            QuantityAvailableKg = request.QuantityAvailableKg,
            Note = request.Note,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Listings.Add(listing);
        await _db.SaveChangesAsync(ct);

        // Publish-after-commit, best effort (see ADR-0009): a broker outage must never
        // fail the user's request. The transactional-outbox pattern is the documented
        // evolution path if delivery guarantees are ever needed.
        try
        {
            await _events.PublishAsync(ListingCreatedEvent.RoutingKey, new ListingCreatedEvent(
                listing.Id, listing.SellerId, listing.BerryType, listing.FarmName,
                listing.PricePerKg, listing.QuantityAvailableKg, listing.Note, listing.CreatedAt), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish ListingCreatedEvent for listing {ListingId}", listing.Id);
        }

        return listing;
    }

    public async Task<Listing> UpdateAsync(Listing listing, UpdateListingRequest request, CancellationToken ct)
    {
        listing.BerryType = request.BerryType;
        listing.FarmName = request.FarmName;
        listing.PricePerKg = request.PricePerKg;
        listing.QuantityAvailableKg = request.QuantityAvailableKg;
        listing.Note = request.Note;

        // Editing the fields the embedding/tasting note were derived from makes them stale
        // immediately - clear them rather than leave a wrong-but-plausible note showing
        // until the async worker catches up (a moment with no note reads better than a
        // moment with the wrong one).
        listing.Embedding = null;
        listing.AiTastingNotes = null;
        await _db.SaveChangesAsync(ct);

        // Republish the existing ListingCreatedEvent rather than inventing a
        // listing.updated contract: the AI worker's handler already just recomputes the
        // embedding/tasting note and PUTs enrichment back (InternalEnrichmentEndpoints is a
        // plain overwrite), so it's idempotent and this reuses the whole pipeline for free.
        try
        {
            await _events.PublishAsync(ListingCreatedEvent.RoutingKey, new ListingCreatedEvent(
                listing.Id, listing.SellerId, listing.BerryType, listing.FarmName,
                listing.PricePerKg, listing.QuantityAvailableKg, listing.Note, listing.CreatedAt), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish ListingCreatedEvent for listing {ListingId} update", listing.Id);
        }

        return listing;
    }

    public async Task SoftDeleteAsync(Listing listing, CancellationToken ct)
    {
        listing.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<(string Mode, List<Listing> Results)> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var anyEmbedded = await _db.Listings.AnyAsync(l => l.Embedding != null, ct);
        if (anyEmbedded)
        {
            var queryVector = new Vector(_embedder.Embed(query));
            var results = await _db.Listings
                .Where(l => l.Embedding != null)
                .OrderBy(l => l.Embedding!.CosineDistance(queryVector))
                .Take(limit)
                .ToListAsync(ct);
            return ("semantic", results);
        }

        var pattern = $"%{query}%";
        var keyword = await _db.Listings
            .Where(l => EF.Functions.ILike(l.BerryType, pattern)
                     || EF.Functions.ILike(l.FarmName, pattern)
                     || (l.Note != null && EF.Functions.ILike(l.Note, pattern)))
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
        return ("keyword", keyword);
    }
}
