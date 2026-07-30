namespace BerryExchange.Api.Listings;

// Deliberately a separate table from Listing, with no navigation property, so listing
// queries (market list, search, event publishing) never pull image bytes into memory -
// only the two endpoints that actually need the photo touch this table.
public class ListingPhoto
{
    public Guid ListingId { get; set; }
    public byte[] Bytes { get; set; } = [];
}
