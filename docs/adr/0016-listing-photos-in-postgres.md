# 0016. Listing photos: processed bytes in Postgres, not object storage

Date: 2026-07-30
Status: Accepted

## Context

Listings only ever had drawn placeholder art (`BerryIcon`, an inline SVG keyed off berry
type) — there was no way for a grower to upload a real photo, and the backend had **no upload
infrastructure at all**: no `IFormFile` usage, no `UseStaticFiles`, no volume, no object-storage
SDK, no `wwwroot`. Adding photos meant building this from nothing, and the storage decision was
the first fork: a mounted volume with `UseStaticFiles`, an object-storage bucket (S3/R2/Azure
Blob), or bytes in the existing Postgres database.

## Decision

**Photo bytes live in Postgres**, in a new `ListingPhoto` table keyed by `ListingId` (also its FK,
`ON DELETE CASCADE`), deliberately separate from `Listing` itself with no navigation property — so
`GetAllAsync`, the pgvector semantic search, and the `ListingCreatedEvent` publisher never
accidentally pull image bytes into memory; only the two endpoints that actually serve or accept a
photo touch that table. `Listing.PhotoContentType` (nullable) stays on the main row precisely
because it's cheap and lets `ListingResponse.HasPhoto` be computed inline in `FromEntity(Listing)`
with no extra query and no call-site changes anywhere that already builds a `ListingResponse`.

This needs no volume, no bucket, no cloud credentials, and behaves identically under
docker-compose, k8s, and the Testcontainers-backed test suite — the same property that has driven
every other infrastructure choice in this codebase (ADR-0002, ADR-0005). The `ponytail:`-flagged
ceiling: fine to a few thousand images at the size this pipeline produces (see below); move to
object storage behind the same URL shape (`GET /api/listings/{id}/photo`) if that ever stops being
true.

**Uploads are decoded, EXIF-oriented, resized, and re-encoded — never stored as the client sent
them.** `POST /api/listings/{id}/photo` (seller-only, ownership checked against
`ClaimTypes.NameIdentifier`) rejects anything over 8 MB (enforced via
`IHttpMaxRequestBodySizeFeature`, set before `ReadFormAsync` reads the body — not after, so an
oversized upload is rejected mid-read rather than fully buffered first), decodes it, auto-rotates
using the EXIF orientation tag, downsizes to 1200 px on the longest edge, and re-encodes as WebP at
quality 80 before ever touching the database. This re-encode is the actual security/privacy
boundary, not just a size optimization: a payload smuggled inside a validly-decoded image cannot
survive being decoded to raw pixels and redrawn, and **all EXIF is dropped** — including the GPS
coordinates a phone photo of a farm typically carries, which would otherwise publish a grower's
location to every visitor of the market page. `GET /api/listings/{id}/photo` is anonymous and sets
`Cache-Control: public, max-age=300` — a short TTL rather than a content-hashed URL, since a photo
can be replaced in place at the same URL and a long-lived cache would keep serving a stale image
after a reupload. `DELETE /api/listings/{id}/photo` (seller-only) removes the row and nulls
`PhotoContentType`, reverting the card to its drawn-icon fallback.

**Image library: SkiaSharp, not ImageSharp.** The plan going into this phase specified
`SixLabors.ImageSharp`, with a documented fallback to SkiaSharp if licensing was a problem. It was:
ImageSharp 4.0.0 (the version NuGet resolves today) fails the **build itself** — not a runtime EULA
prompt, a hard MSBuild target (`SixLaborsLicenseKey`/`SixLaborsLicenseFile` required) with no free
tier — so it was a blocker, not a caveat, and the switch happened in this same phase rather than as
a follow-up. SkiaSharp (MIT) needed two additions the plan didn't anticipate: an explicit
`SkiaSharp.NativeAssets.Linux` package reference (the base `SkiaSharp` package only pulls
Windows/macOS native binaries; Linux ships as a separate package for licensing/packaging reasons),
and `libfontconfig1` installed in the API's Docker runtime stage — `libSkiaSharp.so` links against
fontconfig even for pure image decode/resize/encode with no text rendering involved, and is missing
by default on the `mcr.microsoft.com/dotnet/aspnet:10.0` base image.

SkiaSharp has no built-in "auto-orient" the way ImageSharp does, so `ApplyExifOrientation` is
hand-written: a `translate → rotate → translate` canvas transform around the image center. It
handles the four orientations real phone cameras actually produce (`TopLeft`/no-op,
`BottomRight`/180°, `RightTop`/90° CW, `LeftBottom`/90° CCW) and leaves the four mirrored EXIF
origins unrotated — those come from scanner software, not camera sensors, and were judged not worth
the risk of a wrong guess on origins this codebase will likely never actually see in practice. This
is marked with a `ponytail:` comment naming the gap. Given how easy 2D rotation math is to get
subtly wrong (a sign flip silently mirrors instead of rotates), `ApplyExifOrientation`,
`ResizeToMax`, and `ProcessPhoto` are `internal` with `InternalsVisibleTo` extended to the test
project specifically so `ListingPhotoProcessingTests` can assert exact pixel positions after each
rotation using a hand-built two-pixel marker bitmap, rather than trusting the derivation unverified.

## Consequences

No new infrastructure to run, deploy, or pay for — the trade-off is that Postgres now stores binary
blobs alongside relational data, which doesn't scale indefinitely and doesn't get a CDN in front of
it for free. The 5-minute cache and the WebP-at-1200px pipeline (a 4 MB phone photo typically lands
around 80–150 KB) keep this reasonable well past the point a portfolio/demo deployment would ever
reach.

The re-encode pipeline means **the server always trusts its own output, never the client's
original bytes** — this is a stronger guarantee than magic-byte sniffing (which only checks a
signature, not that the entire file is well-formed) and was chosen over it for exactly that reason.

`SkiaSharp.NativeAssets.Linux` and the Dockerfile's `libfontconfig1` install are both easy to miss
if this pattern is copied elsewhere — SkiaSharp's own NuGet restore succeeds without them (the
managed assembly loads fine), and the failure (`DllNotFoundException` / a decode that works locally
on macOS/Windows but crashes in the container) only surfaces at runtime, inside Docker, on the very
first photo upload. Both are called out explicitly here so that isn't rediscovered the hard way.

Alternatives considered:

- **Mounted volume + `UseStaticFiles`** — rejected; needs a persistent volume in both
  docker-compose and k8s, and breaks across multiple API replicas unless that volume is shared
  (RWX), which neither environment provides today.
- **S3/R2/Azure Blob Storage** — rejected for now; most production-correct long-term, but the
  most moving parts (SDK, credentials, a bucket to provision, presigned-URL plumbing) for a
  feature whose actual data volume is nowhere near what would justify it yet. The `GET` endpoint's
  URL shape is deliberately storage-agnostic so this remains a same-shaped follow-up, not a
  breaking API change.
- **Magic-byte sniffing instead of a full decode/re-encode** — rejected; weaker (validates a
  signature, not the whole file) for roughly the same amount of code, given ImageSharp/SkiaSharp
  were already going to be a dependency for the resize step regardless.
- **ImageSharp (as originally planned)** — rejected once its current version turned out to
  require a paid license to build at all; SkiaSharp was the documented fallback and is what
  shipped.
