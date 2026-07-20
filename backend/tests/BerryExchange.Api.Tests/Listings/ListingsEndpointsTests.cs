using System.Net;
using System.Net.Http.Json;
using BerryExchange.Api.Accounts;
using BerryExchange.Api.Listings;
using Xunit;

namespace BerryExchange.Api.Tests.Listings;

public class ListingsEndpointsTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;

    public ListingsEndpointsTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_without_auth_returns_unauthorized()
    {
        var client = _fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Blueberries", FarmName: "Blue Hollow Orchard", PricePerPint: 5.2m, QuantityAvailable: 10, Note: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_create_then_list_contains_it()
    {
        var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "listings-seller@example.com", Password: "Password123!", DisplayName: "Listings Seller"));

        var createResponse = await client.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Raspberries", FarmName: "Thistlewood Farm", PricePerPint: 7.8m, QuantityAvailable: 9, Note: "Delicate."));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ListingResponse>();

        var listResponse = await client.GetAsync("/api/listings");
        var listings = await listResponse.Content.ReadFromJsonAsync<List<ListingResponse>>();

        Assert.Contains(listings!, l => l.Id == created!.Id);
    }

    [Fact]
    public async Task Get_by_id_for_unknown_listing_returns_not_found()
    {
        var client = _fixture.CreateClient();

        var response = await client.GetAsync($"/api/listings/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_with_overlong_berry_type_returns_bad_request()
    {
        var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "listings-overlong@example.com", Password: "Password123!", DisplayName: "Overlong Seller"));

        var overlongBerryType = new string('B', 41);
        var response = await client.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: overlongBerryType, FarmName: "Blue Hollow Orchard", PricePerPint: 5.2m, QuantityAvailable: 10, Note: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_with_non_positive_price_returns_bad_request()
    {
        var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "listings-badprice@example.com", Password: "Password123!", DisplayName: "Bad Price Seller"));

        var response = await client.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Strawberries", FarmName: "Blue Hollow Orchard", PricePerPint: 0m, QuantityAvailable: 10, Note: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
