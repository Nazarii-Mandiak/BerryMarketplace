using BerryExchange.Api.Infrastructure;
using Pgvector;

namespace BerryExchange.Api.Ai;

public record ListingEnrichmentRequest(float[] Embedding, string? TastingNotes);

public static class InternalEnrichmentEndpoints
{
    public static void MapInternalEnrichmentEndpoints(this WebApplication app)
    {
        app.MapPost("/api/internal/listings/{id:guid}/enrichment",
            async (Guid id, ListingEnrichmentRequest request, HttpContext http,
                   BerryExchangeDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            // Service-to-service auth: shared key, never the user cookie. An unset
            // Internal:ApiKey disables the endpoint entirely (fail closed).
            var expectedKey = config["Internal:ApiKey"];
            if (string.IsNullOrEmpty(expectedKey) ||
                !string.Equals(http.Request.Headers["X-Internal-ApiKey"], expectedKey, StringComparison.Ordinal))
            {
                return Results.Unauthorized();
            }

            if (request.Embedding.Length != 384)
            {
                return Results.BadRequest(new { errors = new[] { "Embedding must have 384 dimensions." } });
            }

            var listing = await db.Listings.FindAsync([id], ct);
            if (listing is null) return Results.NotFound();

            listing.Embedding = new Vector(request.Embedding);
            listing.AiTastingNotes = request.TastingNotes is { Length: > 300 } notes ? notes[..300] : request.TastingNotes;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
