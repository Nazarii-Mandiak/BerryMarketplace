using System.Net;
using System.Net.Http.Json;
using BerryExchange.Api.Accounts;
using BerryExchange.Api.Listings;
using SkiaSharp;
using Xunit;

namespace BerryExchange.Api.Tests.Listings;

public class ListingPhotosEndpointsTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;

    public ListingPhotosEndpointsTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static byte[] MakePngBytes(int width = 400, int height = 300, SKColor? color = null)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color ?? SKColors.Green);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private async Task<(ListingResponse Listing, HttpClient SellerClient)> SeedListing(string sellerEmail)
    {
        var sellerClient = _fixture.CreateClient();
        await sellerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: sellerEmail, Password: "Password123!", DisplayName: "Seller"));
        var createResponse = await sellerClient.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Raspberries", FarmName: "Photo Farm", PricePerKg: 6m, QuantityAvailableKg: 5m, Note: null));
        var listing = (await createResponse.Content.ReadFromJsonAsync<ListingResponse>())!;
        return (listing, sellerClient);
    }

    private static MultipartFormDataContent MultipartPhoto(byte[] bytes, string filename = "photo.png")
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        content.Add(fileContent, "photo", filename);
        return content;
    }

    [Fact]
    public async Task Uploading_a_photo_makes_hasPhoto_true_and_get_returns_it_as_webp()
    {
        var (listing, sellerClient) = await SeedListing("photo-upload@example.com");

        var uploadResponse = await sellerClient.PostAsync(
            $"/api/listings/{listing.Id}/photo", MultipartPhoto(MakePngBytes()));
        Assert.Equal(HttpStatusCode.NoContent, uploadResponse.StatusCode);

        var updatedListing = await sellerClient.GetFromJsonAsync<ListingResponse>($"/api/listings/{listing.Id}");
        Assert.True(updatedListing!.HasPhoto);

        var getResponse = await sellerClient.GetAsync($"/api/listings/{listing.Id}/photo");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal("image/webp", getResponse.Content.Headers.ContentType!.MediaType);
        var bytes = await getResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public async Task Reuploading_a_photo_replaces_the_stored_bytes()
    {
        var (listing, sellerClient) = await SeedListing("photo-reupload@example.com");
        await sellerClient.PostAsync($"/api/listings/{listing.Id}/photo",
            MultipartPhoto(MakePngBytes(color: SKColors.Red)));

        var second = await sellerClient.PostAsync($"/api/listings/{listing.Id}/photo",
            MultipartPhoto(MakePngBytes(width: 40, height: 30, color: SKColors.Blue)));
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        var getResponse = await sellerClient.GetAsync($"/api/listings/{listing.Id}/photo");
        var bytes = await getResponse.Content.ReadAsByteArrayAsync();
        using var decoded = SKBitmap.Decode(bytes);
        // Lossy WebP at quality 80 doesn't preserve exact byte values, so check the
        // dominant channel rather than an exact match - this is still unambiguously blue,
        // not the red that was there before the reupload.
        var pixel = decoded.GetPixel(0, 0);
        Assert.True(pixel.Blue > pixel.Red && pixel.Blue > pixel.Green,
            $"expected a blue-dominant pixel after reupload, got R={pixel.Red} G={pixel.Green} B={pixel.Blue}");
    }

    [Fact]
    public async Task Uploading_a_photo_to_another_sellers_listing_returns_forbidden()
    {
        var (listing, _) = await SeedListing("photo-owner@example.com");
        var otherClient = _fixture.CreateClient();
        await otherClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "photo-intruder@example.com", Password: "Password123!", DisplayName: "Intruder"));

        var response = await otherClient.PostAsync($"/api/listings/{listing.Id}/photo", MultipartPhoto(MakePngBytes()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_bytes_that_are_not_a_recognized_image_returns_bad_request()
    {
        var (listing, sellerClient) = await SeedListing("photo-notimage@example.com");

        var response = await sellerClient.PostAsync($"/api/listings/{listing.Id}/photo",
            MultipartPhoto("this is a text file, not a png"u8.ToArray(), "notes.txt"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_a_file_over_8mb_returns_bad_request()
    {
        var (listing, sellerClient) = await SeedListing("photo-oversized@example.com");
        var oversized = new byte[9 * 1024 * 1024];

        var response = await sellerClient.PostAsync($"/api/listings/{listing.Id}/photo",
            MultipartPhoto(oversized, "huge.png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Getting_a_photo_for_a_listing_without_one_returns_not_found()
    {
        var (listing, _) = await SeedListing("photo-none@example.com");

        var response = await _fixture.CreateClient().GetAsync($"/api/listings/{listing.Id}/photo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_photo_reverts_hasPhoto_to_false_and_the_get_404s()
    {
        var (listing, sellerClient) = await SeedListing("photo-delete@example.com");
        await sellerClient.PostAsync($"/api/listings/{listing.Id}/photo", MultipartPhoto(MakePngBytes()));

        var deleteResponse = await sellerClient.DeleteAsync($"/api/listings/{listing.Id}/photo");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var updatedListing = await sellerClient.GetFromJsonAsync<ListingResponse>($"/api/listings/{listing.Id}");
        Assert.False(updatedListing!.HasPhoto);
        var getResponse = await sellerClient.GetAsync($"/api/listings/{listing.Id}/photo");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Deleting_another_sellers_photo_returns_forbidden()
    {
        var (listing, sellerClient) = await SeedListing("photo-delete-owner@example.com");
        await sellerClient.PostAsync($"/api/listings/{listing.Id}/photo", MultipartPhoto(MakePngBytes()));

        var otherClient = _fixture.CreateClient();
        await otherClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "photo-delete-intruder@example.com", Password: "Password123!", DisplayName: "Intruder"));

        var response = await otherClient.DeleteAsync($"/api/listings/{listing.Id}/photo");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
