# Berrow

A single-page berry marketplace concept — growers list fresh berries, buyers browse and buy, all in the browser.

## Features

- Browse listings with berry-type filter chips and a search box
- Post a new listing (berry, seller, farm, price, quantity, note)
- Buy a pint, which decrements stock and updates a basket count
- Everything persists locally via `localStorage` — no backend required
- Bold, illustrated berry icons built as inline SVG (no external image assets)

## Running it

It's a single self-contained HTML file. Either open it directly:

```
open index.html
```

or serve it locally:

```
python3 -m http.server 8000
```

then visit `http://localhost:8000`.

## Backend API

The real backend lives in `backend/` — an ASP.NET Core Web API over PostgreSQL. See `docs/superpowers/specs/2026-07-20-berry-exchange-architecture-design.md` for the full design and `docs/adr/` for the individual decisions.

Run the tests (requires Docker running locally, for the Testcontainers-based Postgres):

```
cd backend
dotnet test
```
