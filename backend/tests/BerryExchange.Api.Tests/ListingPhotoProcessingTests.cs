using BerryExchange.Api.Listings;
using SkiaSharp;
using Xunit;

namespace BerryExchange.Api.Tests;

public class ListingPhotoProcessingTests
{
    private static SKBitmap TwoPixelBitmap()
    {
        // 2 wide x 1 tall: left pixel red, right pixel blue - a marker distinct enough to
        // tell exactly where each edge ends up after a rotation.
        var bmp = new SKBitmap(2, 1);
        bmp.SetPixel(0, 0, SKColors.Red);
        bmp.SetPixel(1, 0, SKColors.Blue);
        return bmp;
    }

    [Fact]
    public void ApplyExifOrientation_TopLeft_returns_the_bitmap_unchanged()
    {
        using var source = TwoPixelBitmap();
        using var result = ListingPhotosEndpoints.ApplyExifOrientation(source, SKEncodedOrigin.TopLeft);

        Assert.Equal(2, result.Width);
        Assert.Equal(1, result.Height);
        Assert.Equal(SKColors.Red, result.GetPixel(0, 0));
        Assert.Equal(SKColors.Blue, result.GetPixel(1, 0));
    }

    [Fact]
    public void ApplyExifOrientation_RightTop_rotates_90_degrees_clockwise()
    {
        // EXIF 6 - by far the most common real-world case (phone held normally for a
        // portrait shot). Rotating a landscape rectangle 90° CW turns its left edge into
        // the top edge.
        using var source = TwoPixelBitmap();
        using var result = ListingPhotosEndpoints.ApplyExifOrientation(source, SKEncodedOrigin.RightTop);

        Assert.Equal(1, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(SKColors.Red, result.GetPixel(0, 0));
        Assert.Equal(SKColors.Blue, result.GetPixel(0, 1));
    }

    [Fact]
    public void ApplyExifOrientation_LeftBottom_rotates_90_degrees_counterclockwise()
    {
        // EXIF 8 - the mirror-image case of RightTop: rotating 90° CCW turns the left
        // edge into the bottom edge instead of the top.
        using var source = TwoPixelBitmap();
        using var result = ListingPhotosEndpoints.ApplyExifOrientation(source, SKEncodedOrigin.LeftBottom);

        Assert.Equal(1, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(SKColors.Blue, result.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, result.GetPixel(0, 1));
    }

    [Fact]
    public void ApplyExifOrientation_BottomRight_rotates_180_degrees()
    {
        using var source = TwoPixelBitmap();
        using var result = ListingPhotosEndpoints.ApplyExifOrientation(source, SKEncodedOrigin.BottomRight);

        Assert.Equal(2, result.Width);
        Assert.Equal(1, result.Height);
        Assert.Equal(SKColors.Blue, result.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, result.GetPixel(1, 0));
    }

    [Fact]
    public void ResizeToMax_downsizes_the_longest_edge_and_preserves_aspect_ratio()
    {
        using var source = new SKBitmap(3000, 2000);
        using var resized = ListingPhotosEndpoints.ResizeToMax(source, 1200);

        Assert.Equal(1200, resized.Width);
        Assert.Equal(800, resized.Height);
    }

    [Fact]
    public void ResizeToMax_leaves_an_already_small_image_unchanged()
    {
        using var source = new SKBitmap(400, 300);
        using var resized = ListingPhotosEndpoints.ResizeToMax(source, 1200);

        Assert.Equal(400, resized.Width);
        Assert.Equal(300, resized.Height);
    }

    [Fact]
    public void ProcessPhoto_returns_null_for_bytes_that_are_not_a_recognized_image()
    {
        using var stream = new MemoryStream([1, 2, 3, 4, 5]);

        var result = ListingPhotosEndpoints.ProcessPhoto(stream);

        Assert.Null(result);
    }

    [Fact]
    public void ProcessPhoto_reencodes_a_valid_image_as_webp_carrying_no_exif()
    {
        using var bitmap = new SKBitmap(50, 50);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Green);
        }
        using var original = SKImage.FromBitmap(bitmap);
        using var pngData = original.Encode(SKEncodedImageFormat.Png, 100);
        using var input = new MemoryStream(pngData.ToArray());

        var processed = ListingPhotosEndpoints.ProcessPhoto(input);

        Assert.NotNull(processed);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(processed!, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(processed, 8, 4));
        // Decoding to raw pixels and re-encoding from scratch structurally cannot carry
        // EXIF forward - SKBitmap has no EXIF of its own to copy, and SKImage.Encode
        // never attaches any unless explicitly given one. A phone photo's GPS
        // coordinates cannot survive this pipeline, regardless of what the input had.
        Assert.DoesNotContain("Exif", System.Text.Encoding.ASCII.GetString(processed));
    }
}
