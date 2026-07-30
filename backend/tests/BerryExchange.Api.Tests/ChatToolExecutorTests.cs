using System.Text.Json;
using BerryExchange.Api.Chat.Agent;
using BerryExchange.Api.Infrastructure;
using BerryExchange.Api.Listings;
using BerryExchange.Api.Reservations;
using Microsoft.Extensions.DependencyInjection;

namespace BerryExchange.Api.Tests;

public class ChatToolExecutorTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public ChatToolExecutorTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Unconfirmed_reservation_is_refused_and_confirmed_reservation_decrements_stock()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BerryExchangeDbContext>();
        var executor = new ChatToolExecutor(
            scope.ServiceProvider.GetRequiredService<ListingsService>(),
            scope.ServiceProvider.GetRequiredService<ReservationsService>());

        var seller = new BerryExchange.Api.Accounts.ApplicationUser
        { Id = Guid.NewGuid(), UserName = $"s-{Guid.NewGuid():N}@t.dev", Email = $"s-{Guid.NewGuid():N}@t.dev", DisplayName = "S" };
        var buyer = new BerryExchange.Api.Accounts.ApplicationUser
        { Id = Guid.NewGuid(), UserName = $"b-{Guid.NewGuid():N}@t.dev", Email = $"b-{Guid.NewGuid():N}@t.dev", DisplayName = "B" };
        db.Users.AddRange(seller, buyer);
        var listing = new Listing
        {
            Id = Guid.NewGuid(), SellerId = seller.Id, BerryType = "Mulberry", FarmName = "Silk Farm",
            PricePerKg = 4m, QuantityAvailableKg = 2, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        var unconfirmed = await executor.ExecuteAsync(buyer.Id,
            new AgentToolCall("t1", "create_reservation",
                JsonSerializer.Serialize(new { listing_id = listing.Id, quantity_kg = 1m, user_confirmed = false })),
            CancellationToken.None);
        Assert.True(unconfirmed.IsError);

        var confirmed = await executor.ExecuteAsync(buyer.Id,
            new AgentToolCall("t2", "create_reservation",
                JsonSerializer.Serialize(new { listing_id = listing.Id, quantity_kg = 1m, user_confirmed = true })),
            CancellationToken.None);
        Assert.False(confirmed.IsError);

        db.ChangeTracker.Clear();
        Assert.Equal(1, (await db.Listings.FindAsync(listing.Id))!.QuantityAvailableKg);

        var stock = await executor.ExecuteAsync(buyer.Id,
            new AgentToolCall("t3", "check_stock",
                JsonSerializer.Serialize(new { listing_id = listing.Id })), CancellationToken.None);
        Assert.Contains("1", stock.Content);
    }
}
