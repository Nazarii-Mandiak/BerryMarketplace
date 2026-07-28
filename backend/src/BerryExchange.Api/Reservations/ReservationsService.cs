using BerryExchange.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BerryExchange.Api.Reservations;

public class ReservationsService
{
    private readonly BerryExchangeDbContext _db;

    public ReservationsService(BerryExchangeDbContext db)
    {
        _db = db;
    }

    public async Task<ReserveResult> ReserveAsync(Guid listingId, Guid buyerId, CancellationToken ct)
    {
        // Ownership check lives here (not just in ReservationsEndpoints) so every caller -
        // the HTTP endpoint, the chat tool executor, and any future caller - inherits it.
        // A plain read is fine here (no atomicity requirement): it only ever rejects, it
        // never authorizes a write, so a race against a concurrent listing edit can't turn
        // this into an oversell - that guarantee still comes solely from the atomic UPDATE
        // below.
        var sellerId = await _db.Listings
            .Where(l => l.Id == listingId)
            .Select(l => (Guid?)l.SellerId)
            .FirstOrDefaultAsync(ct);

        if (sellerId is null)
        {
            return ReserveResult.NotFound;
        }
        if (sellerId == buyerId)
        {
            return ReserveResult.OwnListing;
        }

        // The decrement UPDATE and the Reservation insert must commit or roll back
        // together: without an explicit transaction they'd be two separate implicit
        // transactions, and a crash/exception between them would leave stock
        // permanently decremented with no reservation row to account for it. This
        // transaction only ever wraps writes that happen after the atomicity guard
        // below has already been evaluated by Postgres, so it does not reintroduce
        // any read-then-write race on QuantityAvailable.
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        // Single atomic conditional UPDATE: the decrement and the "is there stock" check
        // happen as one statement executed by Postgres, so two simultaneous requests on
        // the last pint cannot both see QuantityAvailable > 0 and both succeed. A
        // SELECT-then-UPDATE here would be racy (both requests could read quantity=1
        // before either writes), so this must never be split into a separate read.
        var rows = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Listings\" SET \"QuantityAvailable\" = \"QuantityAvailable\" - 1 WHERE \"Id\" = {listingId} AND \"QuantityAvailable\" > 0",
            ct);

        if (rows == 0)
        {
            // Nothing was written, so there's nothing to commit; let the transaction
            // dispose without committing (equivalent to a no-op rollback).
            return ReserveResult.SoldOut;
        }

        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            ListingId = listingId,
            BuyerId = buyerId,
            Quantity = 1,
            Status = ReservationStatus.Pending,
            ReservedAt = DateTimeOffset.UtcNow
        };
        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);

        return ReserveResult.Success(reservation);
    }

    public async Task<List<Reservation>> GetByBuyerAsync(Guid buyerId, CancellationToken ct)
    {
        return await _db.Reservations
            .Where(r => r.BuyerId == buyerId)
            .OrderByDescending(r => r.ReservedAt)
            .ToListAsync(ct);
    }
}

public enum ReserveOutcome
{
    Success,
    SoldOut,
    NotFound,
    OwnListing,
}

public class ReserveResult
{
    public ReserveOutcome Outcome { get; }
    public Reservation? Reservation { get; }
    public bool Succeeded => Outcome == ReserveOutcome.Success;

    private ReserveResult(ReserveOutcome outcome, Reservation? reservation)
    {
        Outcome = outcome;
        Reservation = reservation;
    }

    public static ReserveResult Success(Reservation r) => new(ReserveOutcome.Success, r);
    public static readonly ReserveResult SoldOut = new(ReserveOutcome.SoldOut, null);
    public static readonly ReserveResult NotFound = new(ReserveOutcome.NotFound, null);
    public static readonly ReserveResult OwnListing = new(ReserveOutcome.OwnListing, null);
}
