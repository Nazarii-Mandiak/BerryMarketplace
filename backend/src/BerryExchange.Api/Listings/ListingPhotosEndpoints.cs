using BerryExchange.Api.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace BerryExchange.Api.Listings;

public static class ListingPhotosEndpoints
{
    private const long MaxUploadBytes = 8 * 1024 * 1024;
    private const int MaxDimension = 1200;
    private const string PhotoContentType = "image/webp";

    public static void MapListingPhotosEndpoints(this WebApplication app)
    {
        app.MapPost("/api/listings/{id:guid}/photo", async (
            Guid id, HttpContext http, BerryExchangeDbContext db, CancellationToken ct) =>
        {
            var listing = await db.Listings.FindAsync([id], ct);
            if (listing is null)
            {
                return Results.NotFound();
            }

            var callerId = Guid.Parse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            if (listing.SellerId != callerId)
            {
                return Results.Json(new { error = "You cannot modify another seller's listing." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            // Set before reading the body, not after, so an oversized upload is rejected by
            // Kestrel while reading rather than after it has already been buffered.
            var maxSizeFeature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (maxSizeFeature is { IsReadOnly: false })
            {
                maxSizeFeature.MaxRequestBodySize = MaxUploadBytes;
            }

            IFormFile? file;
            try
            {
                var form = await http.Request.ReadFormAsync(ct);
                file = form.Files.GetFile("photo");
            }
            catch (BadHttpRequestException)
            {
                return Results.BadRequest(new { errors = new[] { "Photo must be 8 MB or smaller." } });
            }

            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { errors = new[] { "A photo file is required." } });
            }

            byte[]? processed;
            using (var stream = file.OpenReadStream())
            {
                processed = ProcessPhoto(stream);
            }
            if (processed is null)
            {
                return Results.BadRequest(new { errors = new[] { "File is not a recognized image." } });
            }

            var photo = await db.ListingPhotos.FindAsync([id], ct);
            if (photo is null)
            {
                db.ListingPhotos.Add(new ListingPhoto { ListingId = id, Bytes = processed });
            }
            else
            {
                photo.Bytes = processed;
            }
            listing.PhotoContentType = PhotoContentType;
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        }).RequireAuthorization();

        app.MapGet("/api/listings/{id:guid}/photo", async (
            Guid id, HttpContext http, BerryExchangeDbContext db, CancellationToken ct) =>
        {
            var photo = await db.ListingPhotos.AsNoTracking().FirstOrDefaultAsync(p => p.ListingId == id, ct);
            if (photo is null)
            {
                return Results.NotFound();
            }

            // Short TTL, not a content-hashed URL: a photo can be replaced in place at the
            // same URL, so a long-lived cache would keep serving a stale image after a
            // reupload. Revisit with ETags or hashed URLs if this ever takes real traffic.
            http.Response.Headers.CacheControl = "public, max-age=300";
            return Results.File(photo.Bytes, PhotoContentType);
        });

        app.MapDelete("/api/listings/{id:guid}/photo", async (
            Guid id, HttpContext http, BerryExchangeDbContext db, CancellationToken ct) =>
        {
            var listing = await db.Listings.FindAsync([id], ct);
            if (listing is null)
            {
                return Results.NotFound();
            }

            var callerId = Guid.Parse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            if (listing.SellerId != callerId)
            {
                return Results.Json(new { error = "You cannot modify another seller's listing." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var photo = await db.ListingPhotos.FindAsync([id], ct);
            if (photo is not null)
            {
                db.ListingPhotos.Remove(photo);
            }
            listing.PhotoContentType = null;
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        }).RequireAuthorization();
    }

    // Decodes, EXIF-auto-orients, downsizes to MaxDimension on the longest edge, and
    // re-encodes as WebP. Returns null if the stream isn't a decodable image. Re-encoding
    // (rather than storing the client's original bytes) is the actual security/privacy
    // boundary: all EXIF - including a phone photo's GPS coordinates - is dropped by the
    // re-encode, and a payload smuggled inside a validly-decoded image can't survive being
    // decoded and redrawn.
    internal static byte[]? ProcessPhoto(Stream input)
    {
        using var codec = SKCodec.Create(input);
        if (codec is null)
        {
            return null;
        }

        using var source = SKBitmap.Decode(codec);
        if (source is null)
        {
            return null;
        }

        using var oriented = ApplyExifOrientation(source, codec.EncodedOrigin);
        using var resized = ResizeToMax(oriented, MaxDimension);
        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, 80);
        return encoded.ToArray();
    }

    internal static SKBitmap ApplyExifOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        // Real phone cameras only ever emit TopLeft/BottomRight/RightTop/LeftBottom (EXIF
        // 1/3/6/8) - the four mirrored origins exist for scanner software, not camera
        // sensors.
        // ponytail: mirrored origins (TopRight/BottomLeft/LeftTop/RightBottom) are left
        // unrotated rather than guessed at; add full 8-case handling if a mirrored upload
        // is ever actually observed.
        var rotationDegrees = origin switch
        {
            SKEncodedOrigin.BottomRight => 180,
            SKEncodedOrigin.RightTop => 90,
            SKEncodedOrigin.LeftBottom => 270,
            _ => 0,
        };

        if (rotationDegrees == 0)
        {
            return source.Copy();
        }

        var swapDimensions = rotationDegrees is 90 or 270;
        var width = swapDimensions ? source.Height : source.Width;
        var height = swapDimensions ? source.Width : source.Height;

        var rotated = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(rotated))
        {
            canvas.Translate(width / 2f, height / 2f);
            canvas.RotateDegrees(rotationDegrees);
            canvas.Translate(-source.Width / 2f, -source.Height / 2f);
            // Nearest-neighbor: rotation is always an exact 90° multiple, so pixel centers
            // land exactly on other pixel centers - no blending should occur.
            canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        }
        return rotated;
    }

    internal static SKBitmap ResizeToMax(SKBitmap source, int maxDimension)
    {
        var longestEdge = Math.Max(source.Width, source.Height);
        if (longestEdge <= maxDimension)
        {
            return source.Copy();
        }

        var scale = maxDimension / (double)longestEdge;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        return source.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
    }
}
