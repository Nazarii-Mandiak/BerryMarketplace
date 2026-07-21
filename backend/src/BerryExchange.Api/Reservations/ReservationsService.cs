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

        return ReserveResult.Success(reservation);
    }
}

public class ReserveResult
{
    public bool Succeeded { get; }
    public Reservation? Reservation { get; }

    private ReserveResult(bool succeeded, Reservation? reservation)
    {
        Succeeded = succeeded;
        Reservation = reservation;
    }

    public static ReserveResult Success(Reservation r) => new(true, r);
    public static readonly ReserveResult SoldOut = new(false, null);
}
