using BerryExchange.Api.Infrastructure;
using BerryExchange.Api.Listings;
using Microsoft.Extensions.DependencyInjection;
using Pgvector;

namespace BerryExchange.Api.Tests;

public class ListingEmbeddingPersistenceTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public ListingEmbeddingPersistenceTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Embedding_and_tasting_notes_round_trip_through_postgres()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BerryExchangeDbContext>();

        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            SellerId = await SeedUserAsync(db),
            BerryType = "Gooseberry",
            FarmName = "Vector Farm",
            PricePerPint = 4m,
            QuantityAvailable = 2,
            CreatedAt = DateTimeOffset.UtcNow,
            Embedding = new Vector(Enumerable.Repeat(0.1f, 384).ToArray()),
            AiTastingNotes = "Bright and tart."
        };
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.Listings.FindAsync(listing.Id);
        Assert.NotNull(loaded!.Embedding);
        Assert.Equal(384, loaded.Embedding!.ToArray().Length);
        Assert.Equal("Bright and tart.", loaded.AiTastingNotes);
    }

    private static async Task<Guid> SeedUserAsync(BerryExchangeDbContext db)
    {
        var user = new BerryExchange.Api.Accounts.ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"seed-{Guid.NewGuid():N}@test.dev",
            Email = $"seed-{Guid.NewGuid():N}@test.dev",
            DisplayName = "Seed"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
