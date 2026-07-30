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
            BerryType: "Blueberries", FarmName: "Blue Hollow Orchard", PricePerKg: 5.2m, QuantityAvailableKg: 10, Note: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_create_then_list_contains_it()
    {
        var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "listings-seller@example.com", Password: "Password123!", DisplayName: "Listings Seller"));

        var createResponse = await client.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Raspberries", FarmName: "Thistlewood Farm", PricePerKg: 7.8m, QuantityAvailableKg: 9, Note: "Delicate."));
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
            BerryType: overlongBerryType, FarmName: "Blue Hollow Orchard", PricePerKg: 5.2m, QuantityAvailableKg: 10, Note: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_with_non_positive_price_returns_bad_request()
    {
        var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "listings-badprice@example.com", Password: "Password123!", DisplayName: "Bad Price Seller"));

        var response = await client.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Strawberries", FarmName: "Blue Hollow Orchard", PricePerKg: 0m, QuantityAvailableKg: 10, Note: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_with_price_exceeding_the_numeric_10_2_column_bound_returns_bad_request()
    {
        var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "listings-hugeprice@example.com", Password: "Password123!", DisplayName: "Huge Price Seller"));

        // The DB column is numeric(10,2), max ~99,999,999.99. Before this fix, a price at or
        // above 100,000,000 sailed past validation and threw an unhandled Npgsql overflow
        // exception (500) at SaveChangesAsync instead of a clean 400.
        var response = await client.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Strawberries", FarmName: "Blue Hollow Orchard", PricePerKg: 100_000_000m, QuantityAvailableKg: 10, Note: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_with_a_fractional_price_and_quantity_succeeds()
    {
        var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "listings-fractional@example.com", Password: "Password123!", DisplayName: "Fractional Seller"));

        var response = await client.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Strawberries", FarmName: "Blue Hollow Orchard", PricePerKg: 5.25m, QuantityAvailableKg: 12.5m, Note: null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ListingResponse>();
        Assert.Equal(5.25m, created!.PricePerKg);
        Assert.Equal(12.5m, created.QuantityAvailableKg);
    }

    [Fact]
    public async Task Create_with_a_price_that_has_more_than_2_decimal_places_returns_bad_request()
    {
        var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "listings-price-precision@example.com", Password: "Password123!", DisplayName: "Precision Seller"));

        // numeric(10,2) would otherwise silently round 5.256 to 5.26 instead of rejecting it.
        var response = await client.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Strawberries", FarmName: "Blue Hollow Orchard", PricePerKg: 5.256m, QuantityAvailableKg: 10, Note: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_with_a_quantity_that_has_more_than_2_decimal_places_returns_bad_request()
    {
        var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "listings-qty-precision@example.com", Password: "Password123!", DisplayName: "Qty Precision Seller"));

        var response = await client.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Strawberries", FarmName: "Blue Hollow Orchard", PricePerKg: 5.25m, QuantityAvailableKg: 12.505m, Note: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_with_berry_type_that_only_fits_after_trimming_succeeds_with_the_trimmed_value()
    {
        var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "listings-padded@example.com", Password: "Password123!", DisplayName: "Padded Seller"));

        // 2 leading spaces + 40 'A' characters = 42 raw characters, but trims to exactly 40.
        // Before the trim-consistency fix, validation checked the *trimmed* length (40, valid)
        // while ListingsService persisted the *untrimmed* original (42 chars), overflowing the
        // character varying(40) column and crashing with an unhandled DbUpdateException (500).
        // Now that BerryType/FarmName are trimmed once and that same value is used both for the
        // length check and for what gets stored, this is legitimate input (40 chars fits exactly)
        // and must succeed - not be rejected - storing the trimmed value.
        var paddedBerryType = "  " + new string('A', 40);
        var response = await client.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: paddedBerryType, FarmName: "Blue Hollow Orchard", PricePerKg: 5.2m, QuantityAvailableKg: 10, Note: null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ListingResponse>();
        Assert.Equal(new string('A', 40), created!.BerryType);
    }

    [Fact]
    public async Task Create_with_note_that_only_fits_after_trimming_succeeds_with_the_trimmed_value()
    {
        var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "listings-padded-note@example.com", Password: "Password123!", DisplayName: "Padded Note Seller"));

        // 2 leading spaces + 80 'N' characters = 82 raw characters, but trims to exactly 80.
        // Note must follow the same trim-once-and-reuse pattern as BerryType/FarmName: this is
        // legitimate input (80 chars fits the character varying(80) column exactly after
        // trimming) and must succeed - not be incorrectly rejected as too long based on the
        // raw, untrimmed length.
        var paddedNote = "  " + new string('N', 80);
        var response = await client.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Strawberries", FarmName: "Blue Hollow Orchard", PricePerKg: 5.2m, QuantityAvailableKg: 10, Note: paddedNote));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ListingResponse>();
        Assert.Equal(new string('N', 80), created!.Note);
    }
}
