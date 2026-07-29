using System.Net;
using System.Net.Http.Json;
using BerryExchange.Api.Accounts;
using BerryExchange.Api.Listings;
using BerryExchange.Api.Reservations;
using Xunit;

namespace BerryExchange.Api.Tests;

public class EndToEndAcceptanceTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;

    public EndToEndAcceptanceTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Full_flow_register_list_reserve_twice_then_sold_out()
    {
        var sellerClient = _fixture.CreateClient();
        await sellerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "e2e-seller@example.com", Password: "Password123!", DisplayName: "E2E Seller"));

        var createResponse = await sellerClient.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Blackberries", FarmName: "Bramble & Co", PricePerKg: 6.9m, QuantityAvailableKg: 2m, Note: "Deep, wine-dark."));
        var listing = (await createResponse.Content.ReadFromJsonAsync<ListingResponse>())!;

        var buyerClient = _fixture.CreateClient();
        await buyerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "e2e-buyer@example.com", Password: "Password123!", DisplayName: "E2E Buyer"));

        var reserveUrl = $"/api/listings/{listing.Id}/reservations";

        var first = await buyerClient.PostAsJsonAsync(reserveUrl, new ReserveRequest(1m));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await buyerClient.PostAsJsonAsync(reserveUrl, new ReserveRequest(1m));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var third = await buyerClient.PostAsJsonAsync(reserveUrl, new ReserveRequest(1m));
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);

        var finalListing = await sellerClient.GetFromJsonAsync<ListingResponse>($"/api/listings/{listing.Id}");
        Assert.Equal(0m, finalListing!.QuantityAvailableKg);
    }
}
