# Berrow

A berry marketplace — growers list fresh berries, buyers browse and reserve a pint, backed by a real API and database.

## Features

- Browse listings with berry-type filter chips and a search box
- Register/log in, then post a new listing (berry, farm, price, quantity, note)
- Reserve a pint, which atomically decrements stock (see `docs/adr/`) and appears in your reservations
- Bold, illustrated berry icons built as inline SVG (no external image assets)

## Running it

Requires the backend running first (see below), then:

```bash
cd frontend
npm install
npm run dev
```

Visit `http://localhost:5173`. The dev server proxies `/api/*` to the backend at `http://localhost:5091`.

## Backend API

The backend lives in `backend/` — an ASP.NET Core Web API over PostgreSQL. See `docs/superpowers/specs/2026-07-20-berry-exchange-architecture-design.md` for the full design and `docs/adr/` for the individual decisions.

Run the backend:

```bash
cd backend
dotnet run --project src/BerryExchange.Api --launch-profile http
```

Run the backend tests (requires Docker running locally, for the Testcontainers-based Postgres):

```bash
cd backend
dotnet test
```

## Frontend

The frontend lives in `frontend/` — a React + TypeScript SPA bundled with Vite. See `docs/superpowers/specs/2026-07-21-berry-exchange-frontend-design.md` for the design.

Run the frontend tests:

```bash
cd frontend
npm test
```
