using System.Net;
using System.Net.Http.Json;
using BerryExchange.Api.Accounts;
using BerryExchange.Api.Listings;
using BerryExchange.Api.Reservations;
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

    [Fact]
    public async Task Mine_with_no_reservations_returns_empty_list()
    {
        var buyerClient = _fixture.CreateClient();
        await buyerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "res-mine-empty@example.com", Password: "Password123!", DisplayName: "Buyer"));

        var response = await buyerClient.GetAsync("/api/reservations/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reservations = await response.Content.ReadFromJsonAsync<List<ReservationWithListingResponse>>();
        Assert.Empty(reservations!);
    }

    [Fact]
    public async Task Mine_returns_reservation_with_embedded_listing_details_and_only_the_callers_own()
    {
        var (listing, buyerClient) = await SeedListingAndBuyer(
            "res-mine-seller@example.com", "res-mine-buyer@example.com", quantity: 3);
        await buyerClient.PostAsync($"/api/listings/{listing.Id}/reservations", null);

        var otherBuyerClient = _fixture.CreateClient();
        await otherBuyerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "res-mine-other-buyer@example.com", Password: "Password123!", DisplayName: "Other Buyer"));

        var response = await buyerClient.GetAsync("/api/reservations/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reservations = await response.Content.ReadFromJsonAsync<List<ReservationWithListingResponse>>();
        var reservation = Assert.Single(reservations!);
        Assert.Equal(listing.Id, reservation.ListingId);
        Assert.Equal("Gooseberries", reservation.BerryType);
        Assert.Equal("Old Stone Orchard", reservation.FarmName);
        Assert.Equal(8.5m, reservation.PricePerPint);
        Assert.Equal("Pending", reservation.Status);

        var otherResponse = await otherBuyerClient.GetAsync("/api/reservations/mine");
        var otherReservations = await otherResponse.Content.ReadFromJsonAsync<List<ReservationWithListingResponse>>();
        Assert.Empty(otherReservations!);
    }

    [Fact]
    public async Task Mine_without_authentication_returns_unauthorized()
    {
        var anonymousClient = _fixture.CreateClient();

        var response = await anonymousClient.GetAsync("/api/reservations/mine");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
