using System.Net;
using System.Net.Http.Json;
using BerryExchange.Api.Accounts;
using BerryExchange.Api.Infrastructure.Messaging;
using BerryExchange.Api.Listings;
using BerryExchange.Api.Reservations;
using BerryExchange.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BerryExchange.Api.Tests.Listings;

public class ListingEditAndDeleteTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;

    public ListingEditAndDeleteTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(ListingResponse Listing, HttpClient SellerClient)> SeedListing(string sellerEmail)
    {
        var sellerClient = _fixture.CreateClient();
        await sellerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: sellerEmail, Password: "Password123!", DisplayName: "Seller"));
        var createResponse = await sellerClient.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Tayberries", FarmName: "Original Farm", PricePerKg: 7m, QuantityAvailableKg: 4m, Note: null));
        var listing = (await createResponse.Content.ReadFromJsonAsync<ListingResponse>())!;
        return (listing, sellerClient);
    }

    [Fact]
    public async Task Updating_changes_the_listings_fields()
    {
        var (listing, sellerClient) = await SeedListing("edit-update@example.com");

        var response = await sellerClient.PutAsJsonAsync($"/api/listings/{listing.Id}", new UpdateListingRequest(
            BerryType: "Loganberries", FarmName: "Renamed Farm", PricePerKg: 9.5m, QuantityAvailableKg: 2m, Note: "Updated note"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ListingResponse>();
        Assert.Equal("Loganberries", updated!.BerryType);
        Assert.Equal("Renamed Farm", updated.FarmName);
        Assert.Equal(9.5m, updated.PricePerKg);
        Assert.Equal(2m, updated.QuantityAvailableKg);
        Assert.Equal("Updated note", updated.Note);
    }

    [Fact]
    public async Task Updating_another_sellers_listing_returns_forbidden()
    {
        var (listing, _) = await SeedListing("edit-owner@example.com");
        var otherClient = _fixture.CreateClient();
        await otherClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "edit-intruder@example.com", Password: "Password123!", DisplayName: "Intruder"));

        var response = await otherClient.PutAsJsonAsync($"/api/listings/{listing.Id}", new UpdateListingRequest(
            BerryType: "Hijacked", FarmName: "Nope", PricePerKg: 1m, QuantityAvailableKg: 1m, Note: null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Updating_an_unknown_listing_returns_not_found()
    {
        var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "edit-unknown@example.com", Password: "Password123!", DisplayName: "Seller"));

        var response = await client.PutAsJsonAsync($"/api/listings/{Guid.NewGuid()}", new UpdateListingRequest(
            BerryType: "Ghost", FarmName: "Nowhere", PricePerKg: 1m, QuantityAvailableKg: 1m, Note: null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Updating_with_a_non_positive_price_returns_bad_request()
    {
        var (listing, sellerClient) = await SeedListing("edit-badprice@example.com");

        var response = await sellerClient.PutAsJsonAsync($"/api/listings/{listing.Id}", new UpdateListingRequest(
            BerryType: "Tayberries", FarmName: "Original Farm", PricePerKg: 0m, QuantityAvailableKg: 4m, Note: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Updating_republishes_ListingCreatedEvent_with_the_new_values()
    {
        var recorder = new RecordingEventPublisher();
        var client = _fixture.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IEventPublisher>();
            services.AddSingleton<IEventPublisher>(recorder);
        })).CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "edit-republish@example.com", Password: "Password123!", DisplayName: "Seller"));
        var createResponse = await client.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Tayberries", FarmName: "Original Farm", PricePerKg: 7m, QuantityAvailableKg: 4m, Note: null));
        var listing = (await createResponse.Content.ReadFromJsonAsync<ListingResponse>())!;
        recorder.Published.Clear();

        var response = await client.PutAsJsonAsync($"/api/listings/{listing.Id}", new UpdateListingRequest(
            BerryType: "Loganberries", FarmName: "Renamed Farm", PricePerKg: 9.5m, QuantityAvailableKg: 2m, Note: null));
        response.EnsureSuccessStatusCode();

        var (routingKey, evt) = Assert.Single(recorder.Published);
        Assert.Equal(ListingCreatedEvent.RoutingKey, routingKey);
        var typed = Assert.IsType<ListingCreatedEvent>(evt);
        Assert.Equal("Loganberries", typed.BerryType);
        Assert.Equal(9.5m, typed.PricePerKg);
    }

    [Fact]
    public async Task Deleting_removes_the_listing_from_the_market_list_search_and_get_by_id()
    {
        var (listing, sellerClient) = await SeedListing("delete-visibility@example.com");

        var deleteResponse = await sellerClient.DeleteAsync($"/api/listings/{listing.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await sellerClient.GetAsync($"/api/listings/{listing.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var allListings = await sellerClient.GetFromJsonAsync<List<ListingResponse>>("/api/listings");
        Assert.DoesNotContain(allListings!, l => l.Id == listing.Id);

        var searchResponse = await sellerClient.GetAsync("/api/listings/search?q=Tayberries");
        var searchBody = await searchResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(listing.Id.ToString(), searchBody);
    }

    [Fact]
    public async Task Deleting_another_sellers_listing_returns_forbidden()
    {
        var (listing, sellerClient) = await SeedListing("delete-owner@example.com");
        var otherClient = _fixture.CreateClient();
        await otherClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "delete-intruder@example.com", Password: "Password123!", DisplayName: "Intruder"));

        var response = await otherClient.DeleteAsync($"/api/listings/{listing.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Confirm it's still there - a rejected delete must not have any effect.
        var getResponse = await sellerClient.GetAsync($"/api/listings/{listing.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_unknown_listing_returns_not_found()
    {
        var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "delete-unknown@example.com", Password: "Password123!", DisplayName: "Seller"));

        var response = await client.DeleteAsync($"/api/listings/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reserving_a_deleted_listing_returns_not_found()
    {
        var (listing, sellerClient) = await SeedListing("delete-reserve@example.com");
        var buyerClient = _fixture.CreateClient();
        await buyerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "delete-reserve-buyer@example.com", Password: "Password123!", DisplayName: "Buyer"));
        await sellerClient.DeleteAsync($"/api/listings/{listing.Id}");

        var response = await buyerClient.PostAsJsonAsync(
            $"/api/listings/{listing.Id}/reservations", new ReserveRequest(1m));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Mine_still_shows_a_reservation_for_a_listing_that_was_later_deleted()
    {
        var (listing, sellerClient) = await SeedListing("delete-mine@example.com");
        var buyerClient = _fixture.CreateClient();
        await buyerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "delete-mine-buyer@example.com", Password: "Password123!", DisplayName: "Buyer"));
        await buyerClient.PostAsJsonAsync($"/api/listings/{listing.Id}/reservations", new ReserveRequest(1m));

        await sellerClient.DeleteAsync($"/api/listings/{listing.Id}");

        var response = await buyerClient.GetAsync("/api/reservations/mine");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reservations = await response.Content.ReadFromJsonAsync<List<ReservationWithListingResponse>>();
        var reservation = Assert.Single(reservations!);
        Assert.Equal(listing.Id, reservation.ListingId);
        Assert.Equal("Tayberries", reservation.BerryType);
    }
}
