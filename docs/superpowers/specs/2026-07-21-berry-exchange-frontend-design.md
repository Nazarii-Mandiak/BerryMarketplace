# Berry Exchange — Frontend Design

Date: 2026-07-21
Status: Approved

This spec implements the frontend described at a high level in `docs/superpowers/specs/2026-07-20-berry-exchange-architecture-design.md` and ADR-0003, and adds one backend endpoint the original design didn't fully specify.

## Purpose and scope

Replace the client-only `index.html` prototype with a real React + TypeScript SPA backed by the ASP.NET Core API in `backend/`. Covers all four routes from the architecture spec's frontend-components diagram: `/market`, `/sell`, `/reservations`, `/login` (plus `/register`). This is the full MVP frontend in one pass, not a partial slice.

## Backend addition: GET /api/reservations/mine

The architecture spec's frontend-components diagram calls for a "my reservations" view, but the Reservations module as built only exposes `POST /api/listings/{listingId}/reservations`. There is no way to list a buyer's own reservations.

- **New endpoint**: `GET /api/reservations/mine`, authenticated (`RequireAuthorization()`), added to `ReservationsEndpoints.MapReservationsEndpoints`.
- **Behavior**: looks up all reservations where `BuyerId` matches the current user (from `ClaimTypes.NameIdentifier`, same pattern as the POST endpoint and `/api/accounts/me`), joined against `Listing` for display fields.
- **Response shape** — a new `ReservationWithListingResponse`:
  ```csharp
  public record ReservationWithListingResponse(
      Guid Id, Guid ListingId, int Quantity, string Status, DateTimeOffset ReservedAt,
      string BerryType, string FarmName, decimal PricePerPint);
  ```
  Embedding the listing summary avoids the frontend making N+1 calls to `/api/listings/{id}` per reservation row.
- **Query**: `ReservationsService` gains `GetByBuyerAsync(buyerId, ct)`, returning that buyer's `Reservation` entities ordered by `ReservedAt` descending — a query against its own module's table only. `ListingsService` gains `GetByIdsAsync(IEnumerable<Guid> ids, ct)`, a batch lookup against its own module's table, mirroring the existing single-id `GetByIdAsync`. The endpoint handler (in `ReservationsEndpoints`, which already composes both services for the POST route's "can't reserve your own listing" check) calls both, then joins the results in memory into `ReservationWithListingResponse`.
- **Module boundary note**: ADR-0001 requires modules to communicate only through service interfaces, never by reaching into another module's EF entities directly. The existing POST endpoint already follows this pattern (composing `ListingsService` + `ReservationsService` at the endpoint layer); this endpoint does the same rather than having `ReservationsService` query the `Listings` table directly.
- **Tests**: add cases to `ReservationsEndpointsTests` — empty list for a user with no reservations, correct listing fields embedded, only the caller's own reservations returned (not another user's), 401 when unauthenticated.

## Frontend scaffold

New `frontend/` directory (sibling to `backend/`), scaffolded with Vite's `react-ts` template.

- **Routing**: `react-router-dom`. Routes: `/market` (default/index redirect target), `/sell`, `/reservations`, `/login`, `/register`.
- **Data layer**: TanStack Query for server state (listings, reservations, current user), plus a thin `fetch` wrapper (`src/api/client.ts`) that:
  - Prefixes requests with `/api` (same-origin through the reverse proxy in production; Vite dev-server proxy to the local backend in development).
  - Sends `credentials: 'include'` on every request so the ASP.NET Identity session cookie round-trips.
  - Throws a typed `ApiError` (status + parsed `errors`/`error` body) on non-2xx responses, so features can render backend validation messages inline instead of a generic failure.
- **Testing**: Vitest + React Testing Library. Per feature: at least one integration-style test that renders the component tree, drives a user interaction (fill form / click buy / apply filter), and asserts on the resulting UI and the mocked API call — not exhaustive unit coverage of every prop combination.
- **Build/serve**: `npm run build` outputs a static bundle; the reverse proxy (Caddy/nginx, per the architecture spec's deployment path) serves it and forwards `/api/*` to the backend. No SSR.

### Retiring index.html

`index.html` stays in place, unmodified, until the React Market page reaches visual and functional parity with it (same filter chips, search, card layout, buy flow, using the shared design tokens below). At that point it is deleted and the README's "Running it" section is rewritten to describe running `frontend/` instead. This is a parity check before deletion, not a same-commit swap.

## Shared UI kit (design tokens)

Port `index.html`'s `:root` CSS custom properties (colors, fonts, shadows, light/dark via both `prefers-color-scheme` and `data-theme` override), and its component-shaped classes (`.btn`/`.btn-primary`/`.btn-ghost`, `.card`, `.chip`, `.field`, `.toast`, the berry SVG icon library) into `frontend/src/styles/`. The goal is visual continuity with the existing prototype, not a redesign — React components consume the same class names and tokens, restructured as components rather than hand-built DOM strings.

The berry icon library (`iconFor(name)` and its per-berry SVG builders) ports into a `BerryIcon` component taking a berry-type string, preserving the substring-match fallback to a generic icon for unrecognized types.

## Features

### Auth (`/login`, `/register`)
- Login form → `POST /api/accounts/login`; Register form → `POST /api/accounts/register` (email, password, display name). Both set the session cookie server-side on success and redirect to `/market`.
- `useCurrentUser()` query wraps `GET /api/accounts/me`; a 401 is treated as "logged out," not an error state.
- Header replaces the prototype's static basket count with: signed-out → "Log in" link; signed-in → display name + "Log out" (`POST /api/accounts/logout`, then invalidate the current-user query).
- `/sell` and `/reservations` are gated: unauthenticated visitors are redirected to `/login`, preserving the original destination to return to after login.

### Market (`/market`)
- `GET /api/listings` via TanStack Query, refetched on window focus (default) to catch other buyers' activity, per the architecture spec's note that live cross-session updates are a polish item, not a correctness requirement.
- Filter chips (derived from distinct `berryType` values in the fetched list, plus "All") and the search box (matches berry type, farm name — no seller-name field exists in the new data model, unlike the prototype) filter the already-fetched array client-side, matching current behavior.
- "Buy a pint" button: disabled + labeled "Sold out" when `quantityAvailable <= 0`; otherwise calls `POST /api/listings/{id}/reservations`, optimistically decrements the displayed quantity via TanStack Query's cache, and rolls back with a toast ("Sold out.") on a 409. A 400 "You cannot reserve your own listing" is not user-reachable in the UI, since a seller's own listings will not show a buy button for them — checked via the current user's id vs. the listing's `sellerId`.
- Toast reuses the prototype's toast pattern (fixed-position, auto-dismiss) for both success ("Added a pint of X to your reservations") and the sold-out rollback.

### Sell (`/sell`)
- Form fields match the prototype and `CreateListingRequest`: berry type, farm name, price per pint, quantity available, optional note — with the same client-side constraints as the backend validates (40/40/80 char limits, price > 0, quantity >= 0) so obviously-invalid submissions are caught before a round trip, while the backend remains the source of truth.
- On success (`201`), redirect to `/market` with the new listing visible (query invalidation). On `400`, render the backend's `errors` array inline above the form.

### Reservations (`/reservations`)
- `GET /api/reservations/mine` (the new endpoint) via TanStack Query. Each row: berry type, farm name, price per pint, quantity, status, reserved-at date — using the `BerryIcon` component for visual consistency with Market cards.
- Empty state ("No reservations yet — the market's this way") linking to `/market`, mirroring the prototype's empty-state pattern for the listings grid.

## Error handling

- Unauthenticated access to `/sell` or `/reservations` → redirect to `/login`.
- Form validation errors (400 with `errors: string[]`) render inline, near the form, not as a toast.
- Reservation conflicts (409) surface as a toast and trigger a listings refetch so the UI reflects reality.
- Network/unexpected errors (5xx, fetch failure) render a generic inline "Something went wrong — try again" without swallowing the error (logged to console in dev).

## Out of scope

- Real-time updates across simultaneous sessions (explicitly deferred in the architecture spec).
- Editing or cancelling a listing/reservation — not in the current data model's supported operations (no PATCH/DELETE endpoints exist).
- Pagination — listings/reservations are fetched in full, matching the prototype's scale assumptions (solo-developer MVP, not high-volume).
- Seller-name field in listings — the new data model ties listings to `sellerId`/`User`, not a free-text seller name like the prototype; the UI drops that field rather than fabricating one client-side.
