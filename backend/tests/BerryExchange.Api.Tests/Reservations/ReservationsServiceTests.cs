using BerryExchange.Api.Accounts;
using BerryExchange.Api.Infrastructure;
using BerryExchange.Api.Listings;
using BerryExchange.Api.Reservations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BerryExchange.Api.Tests.Reservations;

public class ReservationsServiceTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public ReservationsServiceTests(ApiTestFixture fixture) => _fixture = fixture;

    // Regression test for the chat-agent authorization bypass: ChatToolExecutor calls
    // ReservationsService.ReserveAsync directly, skipping ReservationsEndpoints entirely,
    // so the "can't reserve your own listing" guard must live in the service itself.
    [Fact]
    public async Task ReserveAsync_rejects_reserving_your_own_listing_even_when_called_directly_not_through_the_endpoint()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BerryExchangeDbContext>();
        var reservationsService = scope.ServiceProvider.GetRequiredService<ReservationsService>();

        var seller = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = $"s-{Guid.NewGuid():N}@t.dev",
            Email = $"s-{Guid.NewGuid():N}@t.dev", DisplayName = "Seller",
        };
        db.Users.Add(seller);
        var listing = new Listing
        {
            Id = Guid.NewGuid(), SellerId = seller.Id, BerryType = "Blueberry", FarmName = "Direct Farm",
            PricePerPint = 5m, QuantityAvailable = 3, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        var result = await reservationsService.ReserveAsync(listing.Id, seller.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ReserveOutcome.OwnListing, result.Outcome);

        db.ChangeTracker.Clear();
        Assert.Equal(3, (await db.Listings.FindAsync(listing.Id))!.QuantityAvailable);
    }

    [Fact]
    public async Task ReserveAsync_returns_NotFound_outcome_for_a_nonexistent_listing()
    {
        using var scope = _fixture.Services.CreateScope();
        var reservationsService = scope.ServiceProvider.GetRequiredService<ReservationsService>();

        var result = await reservationsService.ReserveAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ReserveOutcome.NotFound, result.Outcome);
    }
}
