# 0017. Listing edit and delete: soft delete via a global query filter

Date: 2026-07-30
Status: Accepted

## Context

Listings had no update or delete path at all — the API only ever exposed `POST /api/listings`.
A farmer who mistyped a price, wanted to change a photo, or sold out and wanted the crate gone had
no way to do any of that; the listing just sat there until it manually reached zero stock (and even
then, still showed as "Sold out" indefinitely rather than disappearing).

Delete specifically has a constraint the create/read endpoints don't: `Reservations` holds a
foreign key to `Listings`. A hard `DELETE FROM "Listings"` would either cascade away every buyer's
purchase history for that listing, or fail outright on the foreign key — neither is acceptable for
a marketplace where "what did I buy and from whom" needs to stay answerable after the seller moves
on.

## Decision

**Delete is soft.** `Listing` gains a nullable `DeletedAt`; `DELETE /api/listings/{id}` (seller-only,
ownership checked the same way `POST`/photo endpoints are) sets it to `UtcNow` rather than removing
the row. A single `entity.HasQueryFilter(l => l.DeletedAt == null)` on `Listing` in
`BerryExchangeDbContext` then makes every LINQ query against `Listings` — the market list, search,
get-by-id, `FindAsync` (used by the photo endpoints too) — automatically exclude deleted rows, with
no per-query edits anywhere.

That filter has one deliberate hole and one implicit exception, both load-bearing:

- **`ReservationsService.ReserveAsync`'s atomic stock decrement is raw SQL**
  (`ExecuteSqlInterpolatedAsync`), which does not go through EF's query filters at all. This is
  safe only because the ownership check immediately above it *is* a LINQ query
  (`_db.Listings.Where(l => l.Id == listingId).Select(...)`) and therefore *does* respect the
  filter — a soft-deleted listing looks like it doesn't exist at that check, so the method returns
  `NotFound` before the raw SQL ever runs.
  `ListingEditAndDeleteTests.Reserving_a_deleted_listing_returns_not_found` pins this rather than
  leaving it as an unverified side effect of code shape.
- **`ListingsService.GetByIdsAsync`** — the one query that backs `GET /api/reservations/mine` —
  explicitly calls `.IgnoreQueryFilters()`. Without it, a buyer's reservation for a listing the
  seller later deleted would still be in the `Reservations` table, but the dictionary lookup
  joining it to listing details (`listingsById[r.ListingId]`) would throw, because the filtered
  query silently drops the now-invisible listing. This is the one place in the codebase the filter
  needs to be *defeated* on purpose, and it's commented as such at the call site, not left to be
  rediscovered as a bug report.

**Edit reuses the enrichment pipeline instead of inventing a new event.** `PUT /api/listings/{id}`
validates through the same rules as create — `ValidateCreateRequest` was extracted into
`ValidateListingFields(berryType, farmName, pricePerKg, quantityAvailableKg, note)`, taking plain
values rather than either DTO type, so both `CreateListingRequest` and the new
`UpdateListingRequest` (identical shape, distinct name — same fields don't imply the same
semantics) share one validator instead of drifting into two. `ListingsService.UpdateAsync` nulls
`Embedding` and `AiTastingNotes` before saving (they were derived from the now-stale
berry/farm/note text) and republishes the existing `ListingCreatedEvent` rather than a new
`listing.updated` contract — the AI worker's `EnrichingListingCreatedHandler` already just
recomputes an embedding and tasting note and `PUT`s them back through
`InternalEnrichmentEndpoints`, which is a plain overwrite-in-place, so replaying the same event is
idempotent and reuses the whole async pipeline for free instead of building a second one.

## Consequences

No feature anywhere needs to remember to filter out deleted listings — the one line on `Listing`'s
`OnModelCreating` config covers every current and future LINQ query against it. The cost is the
opposite failure mode: any code path that *should* see deleted listings has to opt out explicitly
and correctly, and that's easy to get wrong the first time a new such path is added (as
`GetByIdsAsync` demonstrates) — a future maintainer adding a second query that needs deleted rows
(an admin listing-audit view, say) needs to know this pattern exists and remember to use
`IgnoreQueryFilters()` rather than being warned automatically.

Soft-deleted rows are never purged — there is no retention job. They accumulate indefinitely,
which is fine at any volume this app will plausibly reach, but is a deliberately deferred concern,
not a solved one.

`ListingPhoto` was deliberately given no navigation property back to `Listing` (ADR-0016), so
deleting a listing does not cascade-delete its photo row — the bytes simply become unreachable
through the now-filtered `Listing`, which is harmless and avoids adding delete-time work to an
otherwise fast operation.

Alternatives considered:

- **Hard delete with `ON DELETE CASCADE` on `Reservations`** — rejected outright; a buyer's
  reservation history is real data a marketplace should not destroy just because the seller
  removed the listing.
- **Hard delete with a denormalized snapshot on `Reservation`** (copy berry type/farm/price onto
  the reservation row at purchase time, so it survives the listing's deletion) — a reasonable
  alternative, but a larger change (new columns, a write-time copy step) for a problem the query
  filter already solves with one line; revisit if `Reservation` ever needs to reflect the listing
  as it was *at the time of purchase* rather than as it currently stands (which the filter approach
  cannot do — `Mine` still shows the listing's live berry type/farm name, not a purchase-time
  snapshot).
- **A `listing.updated` RabbitMQ event distinct from `listing.created`** — rejected; the AI
  worker's handling of the event is identical either way (recompute and overwrite), so a second
  contract would be two things to keep in sync for no behavioral difference.
