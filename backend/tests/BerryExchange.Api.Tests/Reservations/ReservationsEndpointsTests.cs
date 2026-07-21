using System.Net;
using System.Net.Http.Json;
using BerryExchange.Api.Accounts;
using BerryExchange.Api.Listings;
using Xunit;

namespace BerryExchange.Api.Tests.Reservations;

public class ReservationsEndpointsTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;

    public ReservationsEndpointsTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(ListingResponse Listing, HttpClient BuyerClient)> SeedListingAndBuyer(
        string sellerEmail, string buyerEmail, int quantity)
    {
        var sellerClient = _fixture.CreateClient();
        await sellerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: sellerEmail, Password: "Password123!", DisplayName: "Seller"));
        var createResponse = await sellerClient.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Gooseberries", FarmName: "Old Stone Orchard", PricePerPint: 8.5m, QuantityAvailable: quantity, Note: null));
        var listing = (await createResponse.Content.ReadFromJsonAsync<ListingResponse>())!;

        var buyerClient = _fixture.CreateClient();
        await buyerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: buyerEmail, Password: "Password123!", DisplayName: "Buyer"));

        return (listing, buyerClient);
    }

    [Fact]
    public async Task Reserving_decrements_quantity_and_returns_created()
    {
        var (listing, buyerClient) = await SeedListingAndBuyer(
            "res-seller-1@example.com", "res-buyer-1@example.com", quantity: 3);

        var response = await buyerClient.PostAsync($"/api/listings/{listing.Id}/reservations", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var updatedListing = await buyerClient.GetFromJsonAsync<ListingResponse>($"/api/listings/{listing.Id}");
        Assert.Equal(2, updatedListing!.QuantityAvailable);
    }

    [Fact]
    public async Task Reserving_a_sold_out_listing_returns_conflict()
    {
        var (listing, buyerClient) = await SeedListingAndBuyer(
            "res-seller-2@example.com", "res-buyer-2@example.com", quantity: 0);

        var response = await buyerClient.PostAsync($"/api/listings/{listing.Id}/reservations", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Reserving_your_own_listing_returns_bad_request()
    {
        var sellerClient = _fixture.CreateClient();
        await sellerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "res-seller-3@example.com", Password: "Password123!", DisplayName: "Seller"));
        var createResponse = await sellerClient.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Mulberries", FarmName: "Fontan Family Grove", PricePerPint: 9.1m, QuantityAvailable: 4, Note: null));
        var listing = (await createResponse.Content.ReadFromJsonAsync<ListingResponse>())!;

        var response = await sellerClient.PostAsync($"/api/listings/{listing.Id}/reservations", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
