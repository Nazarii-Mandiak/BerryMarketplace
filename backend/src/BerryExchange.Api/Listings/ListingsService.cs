using BerryExchange.Api.Infrastructure;
using BerryExchange.Api.Infrastructure.Messaging;
using BerryExchange.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BerryExchange.Api.Listings;

public class ListingsService
{
    private readonly BerryExchangeDbContext _db;
    private readonly IEventPublisher _events;
    private readonly ILogger<ListingsService> _logger;

    public ListingsService(BerryExchangeDbContext db, IEventPublisher events, ILogger<ListingsService> logger)
    {
        _db = db;
        _events = events;
        _logger = logger;
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
        return await _db.Listings.Where(l => ids.Contains(l.Id)).ToListAsync(ct);
    }

    public async Task<Listing> CreateAsync(Guid sellerId, CreateListingRequest request, CancellationToken ct)
    {
        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            SellerId = sellerId,
            BerryType = request.BerryType,
            FarmName = request.FarmName,
            PricePerPint = request.PricePerPint,
            QuantityAvailable = request.QuantityAvailable,
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
                listing.PricePerPint, listing.QuantityAvailable, listing.Note, listing.CreatedAt), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish ListingCreatedEvent for listing {ListingId}", listing.Id);
        }

        return listing;
    }
}
