using System.Net;
using System.Net.Http.Json;
using BerryExchange.Api.Accounts;
using BerryExchange.Api.Listings;
using BerryExchange.Api.Reservations;
using Xunit;

namespace BerryExchange.Api.Tests.Reservations;

public class ReservationsConcurrencyTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;

    public ReservationsConcurrencyTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Two_simultaneous_buyers_requesting_overlapping_fractional_weight_only_one_wins()
    {
        // 1 kg available; both buyers ask for 0.75 kg (1.5 kg total demand). The atomic
        // conditional UPDATE must still allow only one to succeed even though the guard is
        // now a fractional comparison (QuantityAvailableKg >= quantityKg), not the old
        // integer "> 0" check.
        var sellerClient = _fixture.CreateClient();
        await sellerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "concurrency-seller@example.com", Password: "Password123!", DisplayName: "Seller"));
        var createResponse = await sellerClient.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Strawberries", FarmName: "Sunrow Farm", PricePerKg: 6.4m, QuantityAvailableKg: 1m, Note: null));
        var listing = (await createResponse.Content.ReadFromJsonAsync<ListingResponse>())!;

        var buyerAClient = _fixture.CreateClient();
        await buyerAClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "concurrency-buyer-a@example.com", Password: "Password123!", DisplayName: "Buyer A"));

        var buyerBClient = _fixture.CreateClient();
        await buyerBClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "concurrency-buyer-b@example.com", Password: "Password123!", DisplayName: "Buyer B"));

        var reserveUrl = $"/api/listings/{listing.Id}/reservations";

        var taskA = buyerAClient.PostAsJsonAsync(reserveUrl, new ReserveRequest(0.75m));
        var taskB = buyerBClient.PostAsJsonAsync(reserveUrl, new ReserveRequest(0.75m));
        var results = await Task.WhenAll(taskA, taskB);

        var statusCodes = results.Select(r => r.StatusCode).OrderBy(c => c).ToList();
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, statusCodes);

        var finalListing = await sellerClient.GetFromJsonAsync<ListingResponse>($"/api/listings/{listing.Id}");
        Assert.Equal(0.25m, finalListing!.QuantityAvailableKg);
    }
}
