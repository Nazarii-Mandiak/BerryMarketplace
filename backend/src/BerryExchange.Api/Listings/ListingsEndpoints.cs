namespace BerryExchange.Api.Listings;

public static class ListingsEndpoints
{
    public static void MapListingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/listings");

        group.MapGet("/", async (ListingsService service, CancellationToken ct) =>
        {
            var listings = await service.GetAllAsync(ct);
            return Results.Ok(listings.Select(ListingResponse.FromEntity));
        });

        group.MapGet("/search", async (string? q, int? limit, ListingsService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Results.BadRequest(new { errors = new[] { "q is required." } });
            }
            var (mode, results) = await service.SearchAsync(q.Trim(), Math.Clamp(limit ?? 10, 1, 50), ct);
            return Results.Ok(new { mode, results = results.Select(ListingResponse.FromEntity) });
        });

        group.MapGet("/{id:guid}", async (Guid id, ListingsService service, CancellationToken ct) =>
        {
            var listing = await service.GetByIdAsync(id, ct);
            return listing is null ? Results.NotFound() : Results.Ok(ListingResponse.FromEntity(listing));
        });

        group.MapPost("/", async (CreateListingRequest request, HttpContext http, ListingsService service, CancellationToken ct) =>
        {
            // Trim once, up front, and use this same trimmed value both for length
            // validation and for what gets persisted. Validating a trimmed length while
            // saving the untrimmed original would let padded-whitespace input slip past
            // the check and still overflow the HasMaxLength(40)/(80) columns at
            // SaveChangesAsync. Note has no "is required" check, so it stays null when
            // not provided rather than falling back to string.Empty like BerryType/FarmName.
            var normalized = request with
            {
                BerryType = request.BerryType?.Trim() ?? string.Empty,
                FarmName = request.FarmName?.Trim() ?? string.Empty,
                Note = request.Note?.Trim()
            };

            var errors = ValidateCreateRequest(normalized);
            if (errors.Count > 0)
            {
                return Results.BadRequest(new { errors });
            }

            var sellerId = Guid.Parse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var listing = await service.CreateAsync(sellerId, normalized, ct);
            return Results.Created($"/api/listings/{listing.Id}", ListingResponse.FromEntity(listing));
        }).RequireAuthorization();
    }

    private static List<string> ValidateCreateRequest(CreateListingRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.BerryType))
        {
            errors.Add("BerryType is required.");
        }
        else if (request.BerryType.Length > 40)
        {
            errors.Add("BerryType must be 40 characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(request.FarmName))
        {
            errors.Add("FarmName is required.");
        }
        else if (request.FarmName.Length > 40)
        {
            errors.Add("FarmName must be 40 characters or fewer.");
        }

        if (request.Note is not null && request.Note.Length > 80)
        {
            errors.Add("Note must be 80 characters or fewer.");
        }

        if (request.PricePerPint <= 0)
        {
            errors.Add("PricePerPint must be greater than 0.");
        }
        else if (request.PricePerPint >= 100_000_000)
        {
            // The DB column is numeric(10,2): max ~99,999,999.99. Anything at or above
            // 100,000,000 overflows it and would otherwise throw an unhandled Npgsql
            // exception (500) at SaveChangesAsync instead of a clean 400 here.
            errors.Add("PricePerPint must be less than 100,000,000.");
        }

        if (request.QuantityAvailable < 0)
        {
            errors.Add("QuantityAvailable must be 0 or greater.");
        }

        return errors;
    }
}
