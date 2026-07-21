using System.Net;
using System.Net.Http.Json;
using BerryExchange.Api.Accounts;
using BerryExchange.Api.Listings;
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
    public async Task Two_simultaneous_buyers_only_one_wins_the_last_pint()
    {
        var sellerClient = _fixture.CreateClient();
        await sellerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "concurrency-seller@example.com", Password: "Password123!", DisplayName: "Seller"));
        var createResponse = await sellerClient.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Strawberries", FarmName: "Sunrow Farm", PricePerPint: 6.4m, QuantityAvailable: 1, Note: null));
        var listing = (await createResponse.Content.ReadFromJsonAsync<ListingResponse>())!;

        var buyerAClient = _fixture.CreateClient();
        await buyerAClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "concurrency-buyer-a@example.com", Password: "Password123!", DisplayName: "Buyer A"));

        var buyerBClient = _fixture.CreateClient();
        await buyerBClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "concurrency-buyer-b@example.com", Password: "Password123!", DisplayName: "Buyer B"));

        var reserveUrl = $"/api/listings/{listing.Id}/reservations";

        var taskA = buyerAClient.PostAsync(reserveUrl, null);
        var taskB = buyerBClient.PostAsync(reserveUrl, null);
        var results = await Task.WhenAll(taskA, taskB);

        var statusCodes = results.Select(r => r.StatusCode).OrderBy(c => c).ToList();
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, statusCodes);

        var finalListing = await sellerClient.GetFromJsonAsync<ListingResponse>($"/api/listings/{listing.Id}");
        Assert.Equal(0, finalListing!.QuantityAvailable);
    }
}
