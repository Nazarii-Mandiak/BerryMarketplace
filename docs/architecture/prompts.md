# Diagram regeneration log

One entry per diagram creation/update — enough to regenerate or re-derive the diagram later without re-reading the whole design conversation. The `architecture-diagram-update` skill appends to this file every time it touches a `.mmd` file.

## 2026-07-20 — context.mmd (created)

Berry Exchange C4 Level 1. Actors: Buyer, Seller (both are just `User` in the domain model — see data-model.mmd — drawn separately here since they act in different capacities). System: Berry Exchange. No external systems yet — reservation-only marketplace, payment and pickup happen off-platform between buyer and seller, so there's no payment gateway integration to show.

## 2026-07-20 — container.mmd (created)

C4 Level 2. Containers: React SPA, Caddy reverse proxy, ASP.NET Core Web API, PostgreSQL. Proxy serves the SPA bundle and forwards `/api/*` to the API same-origin — required for cookie-based auth (see ADR-0004).

## 2026-07-20 — component-backend.mmd (created)

C4 Level 3 for the API container. Three modules: Listings, Reservations, Accounts, each with endpoints + service + domain entity, sharing one Infrastructure layer (EF Core DbContext, Identity stores, Migrations) that talks to PostgreSQL. Modules never reach into each other's EF entities directly (see ADR-0001) — that boundary is what would let a module split into its own service later.

## 2026-07-20 — component-frontend.mmd (created)

C4 Level 3 for the SPA container. Routes fan out to four features (Market, Sell, Reservations, Auth), all going through a shared API client layer (TanStack Query) and a shared UI kit carrying the design tokens from the original `index.html` prototype (see ADR-0003).

## 2026-07-20 — data-model.mmd (created)

ER diagram, supplementary (outside strict C4). Three entities: User, Listing, Reservation. Any User can both sell and buy — no separate Buyer/Seller entity, matching the prototype's lack of role distinction.

## 2026-07-20 — reservation-flow.mmd (created)

Sequence diagram, supplementary. The one correctness-critical flow: atomic conditional `UPDATE` to decrement listing stock, avoiding a read-then-write race between two simultaneous buyers on the last pint.
