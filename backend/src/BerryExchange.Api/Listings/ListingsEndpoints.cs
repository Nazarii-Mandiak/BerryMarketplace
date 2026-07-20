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

        if (request.QuantityAvailable < 0)
        {
            errors.Add("QuantityAvailable must be 0 or greater.");
        }

        return errors;
    }
}
