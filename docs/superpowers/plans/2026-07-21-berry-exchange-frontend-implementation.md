# Berry Exchange Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the client-only `index.html` prototype with a real React + TypeScript SPA (in `frontend/`) backed by the existing ASP.NET Core API, covering all four spec'd routes (`/market`, `/sell`, `/reservations`, `/login`/`/register`), plus a new `GET /api/reservations/mine` backend endpoint the frontend needs.

**Architecture:** A modular-monolith backend (unchanged, one new read endpoint added) serving a Vite-bundled React SPA. The SPA uses TanStack Query for server state, a thin `fetch` wrapper for API calls (cookie auth via `credentials: 'include'`), and react-router-dom for client-side routing. Visual design is ported verbatim from `index.html`'s CSS custom properties and component classes — this is not a redesign.

**Tech Stack:** Backend: ASP.NET Core Minimal API (.NET 10), EF Core, PostgreSQL, xUnit + Testcontainers (all existing, unchanged versions). Frontend: React 19, TypeScript, Vite 8, react-router-dom 7, @tanstack/react-query 5, Vitest 4 + React Testing Library 16 + @testing-library/user-event.

## Global Constraints

- Listing field limits (already enforced server-side, mirror client-side): `BerryType` and `FarmName` ≤ 40 characters, `Note` ≤ 80 characters, `PricePerPint` > 0 and < 100,000,000, `QuantityAvailable` ≥ 0.
- All API calls go through `/api/*`, same-origin, with `credentials: 'include'` for the ASP.NET Identity session cookie (cookie name `BerryExchange.Auth`).
- No pagination, no listing/reservation edit or delete (no such backend endpoints exist), no seller-name field on listings (data model ties listings to `sellerId`/`User`, not free text).
- Backend modules communicate only through service interfaces, never by reaching into another module's EF entities directly (ADR-0001) — cross-module reads are composed at the endpoint layer.
- `frontend/` is a new top-level directory sibling to `backend/`. The existing root `index.html` prototype is retired (deleted) only in the final task, after parity is confirmed.

---

### Task 1: Backend — GET /api/reservations/mine endpoint

**Files:**
- Modify: `backend/src/BerryExchange.Api/Reservations/ReservationDtos.cs`
- Modify: `backend/src/BerryExchange.Api/Reservations/ReservationsService.cs`
- Modify: `backend/src/BerryExchange.Api/Listings/ListingsService.cs`
- Modify: `backend/src/BerryExchange.Api/Reservations/ReservationsEndpoints.cs`
- Test: `backend/tests/BerryExchange.Api.Tests/Reservations/ReservationsEndpointsTests.cs`

**Interfaces:**
- Produces: `ReservationWithListingResponse(Guid Id, Guid ListingId, int Quantity, string Status, DateTimeOffset ReservedAt, string BerryType, string FarmName, decimal PricePerPint)` — consumed by the frontend's `ReservationsPage` (Task 9) via `GET /api/reservations/mine`.
- Produces: `ReservationsService.GetByBuyerAsync(Guid buyerId, CancellationToken ct) : Task<List<Reservation>>`.
- Produces: `ListingsService.GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct) : Task<List<Listing>>`.

- [ ] **Step 1: Write the failing tests**

Add these three test methods to the end of the `ReservationsEndpointsTests` class in `backend/tests/BerryExchange.Api.Tests/Reservations/ReservationsEndpointsTests.cs` (before the final closing `}` of the class):

```csharp
    [Fact]
    public async Task Mine_with_no_reservations_returns_empty_list()
    {
        var buyerClient = _fixture.CreateClient();
        await buyerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "res-mine-empty@example.com", Password: "Password123!", DisplayName: "Buyer"));

        var response = await buyerClient.GetAsync("/api/reservations/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reservations = await response.Content.ReadFromJsonAsync<List<ReservationWithListingResponse>>();
        Assert.Empty(reservations!);
    }

    [Fact]
    public async Task Mine_returns_reservation_with_embedded_listing_details_and_only_the_callers_own()
    {
        var (listing, buyerClient) = await SeedListingAndBuyer(
            "res-mine-seller@example.com", "res-mine-buyer@example.com", quantity: 3);
        await buyerClient.PostAsync($"/api/listings/{listing.Id}/reservations", null);

        var otherBuyerClient = _fixture.CreateClient();
        await otherBuyerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "res-mine-other-buyer@example.com", Password: "Password123!", DisplayName: "Other Buyer"));

        var response = await buyerClient.GetAsync("/api/reservations/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reservations = await response.Content.ReadFromJsonAsync<List<ReservationWithListingResponse>>();
        var reservation = Assert.Single(reservations!);
        Assert.Equal(listing.Id, reservation.ListingId);
        Assert.Equal("Gooseberries", reservation.BerryType);
        Assert.Equal("Old Stone Orchard", reservation.FarmName);
        Assert.Equal(8.5m, reservation.PricePerPint);
        Assert.Equal("Pending", reservation.Status);

        var otherResponse = await otherBuyerClient.GetAsync("/api/reservations/mine");
        var otherReservations = await otherResponse.Content.ReadFromJsonAsync<List<ReservationWithListingResponse>>();
        Assert.Empty(otherReservations!);
    }

    [Fact]
    public async Task Mine_without_authentication_returns_unauthorized()
    {
        var anonymousClient = _fixture.CreateClient();

        var response = await anonymousClient.GetAsync("/api/reservations/mine");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run (from `backend/`, Docker must be running for Testcontainers):
```bash
dotnet test --filter "FullyQualifiedName~ReservationsEndpointsTests"
```
Expected: build FAILS with `CS0246: The type or namespace name 'ReservationWithListingResponse' could not be found`.

- [ ] **Step 3: Add the response DTO**

Modify `backend/src/BerryExchange.Api/Reservations/ReservationDtos.cs` — add after the existing `ReservationResponse` record:

```csharp
public record ReservationWithListingResponse(
    Guid Id, Guid ListingId, int Quantity, string Status, DateTimeOffset ReservedAt,
    string BerryType, string FarmName, decimal PricePerPint);
```

- [ ] **Step 4: Add ListingsService.GetByIdsAsync**

Modify `backend/src/BerryExchange.Api/Listings/ListingsService.cs` — add after `GetByIdAsync`:

```csharp
    public async Task<List<Listing>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        return await _db.Listings.Where(l => ids.Contains(l.Id)).ToListAsync(ct);
    }
```

- [ ] **Step 5: Add ReservationsService.GetByBuyerAsync**

Modify `backend/src/BerryExchange.Api/Reservations/ReservationsService.cs` — add as a new method on `ReservationsService`, before the closing brace of the class (after `ReserveAsync`):

```csharp
    public async Task<List<Reservation>> GetByBuyerAsync(Guid buyerId, CancellationToken ct)
    {
        return await _db.Reservations
            .Where(r => r.BuyerId == buyerId)
            .OrderByDescending(r => r.ReservedAt)
            .ToListAsync(ct);
    }
```

- [ ] **Step 6: Add the GET /api/reservations/mine endpoint**

Modify `backend/src/BerryExchange.Api/Reservations/ReservationsEndpoints.cs` — add inside `MapReservationsEndpoints`, after the existing `app.MapPost(...)` block (still before the closing `}` of the method):

```csharp
        app.MapGet("/api/reservations/mine", async (
            HttpContext http,
            ReservationsService reservationsService,
            ListingsService listingsService,
            CancellationToken ct) =>
        {
            var buyerId = Guid.Parse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

            var reservations = await reservationsService.GetByBuyerAsync(buyerId, ct);
            var listings = await listingsService.GetByIdsAsync(reservations.Select(r => r.ListingId), ct);
            var listingsById = listings.ToDictionary(l => l.Id);

            var response = reservations.Select(r =>
            {
                var listing = listingsById[r.ListingId];
                return new ReservationWithListingResponse(
                    r.Id, r.ListingId, r.Quantity, r.Status.ToString(), r.ReservedAt,
                    listing.BerryType, listing.FarmName, listing.PricePerPint);
            });

            return Results.Ok(response);
        }).RequireAuthorization();
```

- [ ] **Step 7: Run tests to verify they pass**

Run:
```bash
dotnet test --filter "FullyQualifiedName~ReservationsEndpointsTests"
```
Expected: PASS, all 6 tests in `ReservationsEndpointsTests` (3 existing + 3 new) green.

- [ ] **Step 8: Run the full backend suite and commit**

```bash
dotnet test
```
Expected: PASS, all tests green.

```bash
git add backend/src/BerryExchange.Api/Reservations/ReservationDtos.cs \
        backend/src/BerryExchange.Api/Reservations/ReservationsService.cs \
        backend/src/BerryExchange.Api/Listings/ListingsService.cs \
        backend/src/BerryExchange.Api/Reservations/ReservationsEndpoints.cs \
        backend/tests/BerryExchange.Api.Tests/Reservations/ReservationsEndpointsTests.cs
git commit -m "Add GET /api/reservations/mine endpoint with embedded listing details"
```

---

### Task 2: Frontend scaffold — Vite + React + TypeScript + Vitest

**Files:**
- Create: `frontend/` (via Vite scaffold — package.json, tsconfig*.json, index.html, src/main.tsx, src/App.tsx, etc.)
- Modify: `frontend/vite.config.ts`
- Create: `frontend/src/setupTests.ts`
- Modify: `frontend/package.json` (scripts)

**Interfaces:**
- Produces: a `frontend/` app buildable with `npm run build` and testable with `npm test`, dev server proxying `/api` to `http://localhost:5091` (the backend's `http` launch profile).

- [ ] **Step 1: Scaffold the Vite project**

From the repo root:
```bash
npm create vite@latest frontend -- --template react-ts
```
Expected: `frontend/` created with the standard Vite React+TS template files.

- [ ] **Step 2: Install dependencies**

```bash
cd frontend
npm install
```
Expected: `node_modules/` populated, no errors.

- [ ] **Step 3: Install routing and data-fetching libraries**

```bash
npm install react-router-dom @tanstack/react-query
```

- [ ] **Step 4: Install test tooling**

```bash
npm install -D vitest jsdom @testing-library/react @testing-library/jest-dom @testing-library/user-event
```

- [ ] **Step 5: Configure Vite (dev proxy + Vitest)**

Replace the full contents of `frontend/vite.config.ts` with:

```typescript
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5091',
        changeOrigin: true,
      },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/setupTests.ts'],
  },
});
```

- [ ] **Step 6: Create the test setup file**

Create `frontend/src/setupTests.ts`:

```typescript
import '@testing-library/jest-dom/vitest';
```

- [ ] **Step 7: Add test scripts to package.json**

Modify `frontend/package.json` — in the `"scripts"` object, add two entries alongside the existing `dev`/`build`/`lint`/`preview`:

```json
    "test": "vitest run",
    "test:watch": "vitest"
```

- [ ] **Step 8: Verify the test pipeline with a throwaway smoke test**

Create a temporary file `frontend/src/sanity.test.tsx`:

```tsx
import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';

describe('test pipeline sanity check', () => {
  it('renders and asserts with jsdom + RTL + jest-dom', () => {
    render(<p>ok</p>);
    expect(screen.getByText('ok')).toBeInTheDocument();
  });
});
```

Run:
```bash
npm test
```
Expected: PASS, 1 test.

Then delete the temporary file — it was only there to prove the toolchain works, not a real test:
```bash
rm frontend/src/sanity.test.tsx
```

- [ ] **Step 9: Verify the build**

```bash
npm run build
```
(from inside `frontend/`)
Expected: builds successfully, `frontend/dist/` produced.

- [ ] **Step 10: Commit**

```bash
cd /Users/user/BerryMarketplace
git add frontend/
git commit -m "Scaffold Vite + React + TypeScript frontend with Vitest test tooling"
```

---

### Task 3: Shared UI kit — design tokens, global styles, BerryIcon, ToastProvider

**Files:**
- Create: `frontend/src/styles/global.css`
- Create: `frontend/src/components/BerryIcon.tsx`
- Test: `frontend/src/components/BerryIcon.test.tsx`
- Create: `frontend/src/components/ToastProvider.tsx`

**Interfaces:**
- Produces: `BerryIcon({ berryType: string })` — a React component rendering an `<svg class="berry-icon">`, consumed by `MarketPage` (Task 6) and `ReservationsPage` (Task 8).
- Produces: `ToastProvider({ children })` and `useToast(): { showToast: (message: string) => void }`, consumed by `main.tsx` (Task 9, wraps the app) and `MarketPage` (Task 6).
- Produces: CSS classes ported from `index.html` — `.btn`/`.btn-primary`/`.btn-ghost`, `.card`, `.chip`, `.chips`, `.search-input`, `.field`, `.row-2`, `.panel-card`, `.form-errors`, `.toast`, `.grid`, `.empty-state`, `.site-header`, `.site-nav`, `.auth-status`, `.hero`, `.harvest-banner`/`.harvest-row`/`.harvest-tag`, `.market`/`.market-head`/`.filter-row`, `.sell`/`.auth`/`.reservations` — consumed by every feature component in later tasks.

- [ ] **Step 1: Write the failing BerryIcon test**

Create `frontend/src/components/BerryIcon.test.tsx`:

```tsx
import { describe, expect, it } from 'vitest';
import { render } from '@testing-library/react';
import { BerryIcon } from './BerryIcon';

describe('BerryIcon', () => {
  it('renders a themed icon for a known berry type', () => {
    const { container } = render(<BerryIcon berryType="Strawberries" />);
    expect(container.querySelector('svg.berry-icon')).not.toBeNull();
    expect(container.querySelector('path[fill="#e5384f"]')).not.toBeNull();
  });

  it('falls back to the generic icon for an unrecognized berry type', () => {
    const { container } = render(<BerryIcon berryType="Kiwi" />);
    expect(container.querySelector('svg.berry-icon')).not.toBeNull();
    expect(container.querySelector('circle[fill="var(--accent)"]')).not.toBeNull();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
npm test
```
(from `frontend/`)
Expected: FAIL — `Failed to resolve import "./BerryIcon"`.

- [ ] **Step 3: Implement BerryIcon**

Create `frontend/src/components/BerryIcon.tsx`:

```tsx
import type { ReactNode } from 'react';

const INK = 'var(--ink)';
const LEAF = '#3f5b3c';

function Calyx({ cx, cy, fill }: { cx: number; cy: number; fill: string }) {
  return (
    <g transform={`translate(${cx},${cy})`}>
      {[-40, -20, 0, 20, 40].map((deg) => (
        <ellipse
          key={deg}
          cx={0}
          cy={-7}
          rx={6.5}
          ry={deg === 0 ? 18 : 16}
          fill={fill}
          stroke={INK}
          strokeWidth={4}
          transform={`rotate(${deg})`}
        />
      ))}
    </g>
  );
}

function Drupelets({ fill }: { fill: string }) {
  const rows = [
    { y: 90, xs: [78, 100, 122] },
    { y: 111, xs: [64, 87, 110, 133] },
    { y: 132, xs: [72, 95, 118, 140] },
    { y: 151, xs: [84, 107, 129] },
  ];
  return (
    <>
      {rows.map((row) =>
        row.xs.map((x) => (
          <circle key={`${row.y}-${x}`} cx={x} cy={row.y} r={13} fill={fill} stroke={INK} strokeWidth={2} />
        )),
      )}
    </>
  );
}

function BaseLeaf({ x, y }: { x: number; y: number }) {
  const d = `M ${x} ${y} C ${x - 16} ${y - 4} ${x - 20} ${y - 18} ${x - 10} ${y - 28} C ${x - 1} ${y - 16} ${x - 2} ${y - 4} ${x} ${y} Z`;
  return <path d={d} fill={LEAF} stroke={INK} strokeWidth={4} />;
}

function Wrap({ children }: { children: ReactNode }) {
  return (
    <svg className="berry-icon" viewBox="0 0 200 200" role="img" aria-hidden="true">
      {children}
    </svg>
  );
}

const ICON_BUILDERS: Record<string, () => ReactNode> = {
  strawberries: () => {
    const seeds: [number, number, number][] = [
      [78, 90, -10], [104, 82, 15], [128, 98, -20], [70, 118, 5], [96, 112, -5],
      [122, 124, 20], [82, 146, -15], [108, 150, 10], [60, 96, 25], [138, 118, -8],
    ];
    return (
      <>
        <path
          d="M100,56 C132,50 162,76 158,108 C154,144 128,178 100,178 C72,178 46,144 42,108 C38,76 68,50 100,56 Z"
          fill="#e5384f"
          stroke={INK}
          strokeWidth={6}
        />
        {seeds.map(([x, y, rot]) => (
          <ellipse key={`${x}-${y}`} cx={x} cy={y} rx={4.5} ry={7} fill="#f5d77a" transform={`rotate(${rot} ${x} ${y})`} />
        ))}
        <Calyx cx={100} cy={52} fill={LEAF} />
      </>
    );
  },
  blueberries: () => (
    <>
      <circle cx={100} cy={112} r={56} fill="#4c5fa8" stroke={INK} strokeWidth={6} />
      <ellipse cx={78} cy={90} rx={16} ry={10} fill="#ffffff" opacity={0.35} transform="rotate(-20 78 90)" />
      <path
        d="M100,60 L94,47 M100,60 L100,44 M100,60 L106,47 M100,60 L90,54 M100,60 L110,54"
        stroke="#22284a"
        strokeWidth={4}
        strokeLinecap="round"
        fill="none"
      />
      <line x1={100} y1={44} x2={100} y2={32} stroke={LEAF} strokeWidth={5} strokeLinecap="round" />
    </>
  ),
  raspberries: () => (
    <>
      <path
        d="M60,150 C55,98 70,58 100,56 C130,58 145,98 140,150 C140,169 120,180 100,180 C80,180 60,169 60,150 Z"
        fill="#e8637a"
        stroke={INK}
        strokeWidth={6}
      />
      <Drupelets fill="#f3a6b4" />
      <BaseLeaf x={52} y={158} />
    </>
  ),
  blackberries: () => (
    <>
      <path
        d="M60,150 C55,98 70,58 100,56 C130,58 145,98 140,150 C140,169 120,180 100,180 C80,180 60,169 60,150 Z"
        fill="#3b2740"
        stroke={INK}
        strokeWidth={6}
      />
      <Drupelets fill="#5c4066" />
      <BaseLeaf x={52} y={158} />
    </>
  ),
  gooseberries: () => (
    <>
      <ellipse cx={100} cy={112} rx={54} ry={60} fill="#cfe29a" fillOpacity={0.65} stroke={INK} strokeWidth={6} />
      <path d="M72,72 C87,98 87,132 72,158" fill="none" stroke="#8fae55" strokeWidth={2.5} />
      <path d="M100,62 C105,98 105,132 100,166" fill="none" stroke="#8fae55" strokeWidth={2.5} />
      <path d="M128,72 C113,98 113,132 128,158" fill="none" stroke="#8fae55" strokeWidth={2.5} />
      <ellipse cx={82} cy={86} rx={14} ry={9} fill="#ffffff" opacity={0.4} transform="rotate(-25 82 86)" />
      <line x1={100} y1={52} x2={100} y2={38} stroke={LEAF} strokeWidth={5} strokeLinecap="round" />
    </>
  ),
  mulberries: () => (
    <>
      <g transform="translate(100,116) scale(0.85,1.12) translate(-100,-116)">
        <path
          d="M60,150 C55,98 70,58 100,56 C130,58 145,98 140,150 C140,169 120,180 100,180 C80,180 60,169 60,150 Z"
          fill="#4a1e38"
          stroke={INK}
          strokeWidth={6}
        />
        <Drupelets fill="#7a3a5c" />
      </g>
      <BaseLeaf x={58} y={62} />
    </>
  ),
};

function GenericIcon(): ReactNode {
  return (
    <>
      <circle cx={100} cy={112} r={56} fill="var(--accent)" stroke={INK} strokeWidth={6} />
      <ellipse cx={78} cy={90} rx={16} ry={10} fill="#ffffff" opacity={0.3} transform="rotate(-20 78 90)" />
      <line x1={100} y1={56} x2={100} y2={40} stroke={LEAF} strokeWidth={5} strokeLinecap="round" />
      <ellipse cx={110} cy={42} rx={11} ry={6} fill={LEAF} stroke={INK} strokeWidth={3} transform="rotate(25 110 42)" />
    </>
  );
}

export function BerryIcon({ berryType }: { berryType: string }) {
  const key = berryType.toLowerCase();
  const match = Object.keys(ICON_BUILDERS).find((name) => key.includes(name.slice(0, -3)));
  const build = match ? ICON_BUILDERS[match] : GenericIcon;
  return <Wrap>{build()}</Wrap>;
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
npm test
```
Expected: PASS.

- [ ] **Step 5: Write global.css (design tokens + shared component classes)**

Create `frontend/src/styles/global.css`:

```css
:root {
  --ground: #f7f3ec;
  --ground-2: #ecd9d3;
  --panel: #ffffff;
  --ink: #171310;
  --ink-muted: #7a7168;
  --accent: #c4293f;
  --accent-ink: #ffffff;
  --leaf: #3f5b3c;
  --line: rgba(23, 19, 16, 0.14);
  --line-strong: rgba(23, 19, 16, 0.5);
  --shadow: 0 10px 30px rgba(23, 19, 16, 0.08);
  --font-display: "Helvetica Neue", "Neue Haas Grotesk Display Pro", Arial, sans-serif;
  --font-body: "Neue Haas Grotesk Text Pro", "Helvetica Neue", Arial, sans-serif;
  --font-accent: Georgia, "Iowan Old Style", "Palatino Linotype", serif;
  --font-mono: ui-monospace, "SF Mono", "Cascadia Mono", "Roboto Mono", monospace;
}
@media (prefers-color-scheme: dark) {
  :root {
    --ground: #15120d;
    --ground-2: #2c1f22;
    --panel: #1e1a15;
    --ink: #f5efe4;
    --ink-muted: #b4a996;
    --accent: #ff7d8f;
    --accent-ink: #1a1310;
    --leaf: #8fb386;
    --line: rgba(245, 239, 228, 0.14);
    --line-strong: rgba(245, 239, 228, 0.45);
    --shadow: 0 10px 30px rgba(0, 0, 0, 0.4);
  }
}
:root[data-theme="dark"] {
  --ground: #15120d; --ground-2: #2c1f22; --panel: #1e1a15; --ink: #f5efe4; --ink-muted: #b4a996;
  --accent: #ff7d8f; --accent-ink: #1a1310; --leaf: #8fb386;
  --line: rgba(245, 239, 228, 0.14); --line-strong: rgba(245, 239, 228, 0.45); --shadow: 0 10px 30px rgba(0, 0, 0, 0.4);
}
:root[data-theme="light"] {
  --ground: #f7f3ec; --ground-2: #ecd9d3; --panel: #ffffff; --ink: #171310; --ink-muted: #7a7168;
  --accent: #c4293f; --accent-ink: #ffffff; --leaf: #3f5b3c;
  --line: rgba(23, 19, 16, 0.14); --line-strong: rgba(23, 19, 16, 0.5); --shadow: 0 10px 30px rgba(23, 19, 16, 0.08);
}

* { box-sizing: border-box; }
body {
  margin: 0;
  background: var(--ground);
  color: var(--ink);
  font-family: var(--font-body);
  font-size: 17px;
  line-height: 1.55;
  -webkit-font-smoothing: antialiased;
}
a { color: inherit; }
h1, h2, h3 { font-family: var(--font-display); text-wrap: balance; margin: 0; }
button, input { font-family: inherit; font-size: inherit; color: inherit; }
svg { display: block; }

/* header */
.site-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  max-width: 1180px;
  margin: 0 auto;
  padding: 24px 28px;
  gap: 20px;
}
.wordmark {
  font-family: var(--font-display);
  font-weight: 800;
  font-size: 24px;
  letter-spacing: -0.01em;
  text-decoration: none;
}
.site-nav { display: flex; gap: 10px; }
.site-nav a {
  text-decoration: none;
  font-weight: 700;
  font-size: 13.5px;
  color: var(--ink);
  padding: 10px 18px;
  border-radius: 999px;
  border: 2px solid var(--ink);
}
.site-nav a:hover, .site-nav a:focus-visible { background: var(--ink); color: var(--ground); }
.auth-status { display: flex; align-items: center; gap: 12px; font-weight: 700; font-size: 13.5px; white-space: nowrap; }
@media (max-width: 780px) { .site-nav { display: none; } }

/* hero */
.hero {
  max-width: 1180px;
  margin: 0 auto;
  padding: 30px 28px 46px;
  display: grid;
  grid-template-columns: 1fr 0.85fr;
  gap: 40px;
  align-items: end;
}
.eyebrow {
  font-size: 13px;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--ink);
  margin: 0 0 16px;
  font-weight: 800;
  display: flex;
  align-items: center;
  gap: 8px;
}
.eyebrow::before { content: "/"; color: var(--accent); font-weight: 800; }
.hero h1 {
  font-size: clamp(38px, 5.4vw, 68px);
  line-height: 0.98;
  letter-spacing: -0.02em;
  font-weight: 800;
}
.hero-copy-right { padding-bottom: 6px; }
.hero .lede { color: var(--ink-muted); font-size: 17px; max-width: 42ch; margin: 0; }
@media (max-width: 780px) {
  .hero { grid-template-columns: 1fr; align-items: start; }
}

/* buttons */
.btn {
  display: inline-block;
  text-decoration: none;
  font-family: var(--font-body);
  font-weight: 700;
  font-size: 14.5px;
  padding: 14px 26px;
  border-radius: 999px;
  border: 2px solid var(--ink);
  cursor: pointer;
  background: transparent;
  color: var(--ink);
  transition: background 0.15s ease, color 0.15s ease, transform 0.15s ease;
}
.btn-primary { background: var(--ink); border-color: var(--ink); color: var(--ground); }
.btn-primary:hover { background: var(--accent); border-color: var(--accent); color: var(--accent-ink); }
.btn-ghost { background: transparent; }
.btn-ghost:hover { background: var(--ink); color: var(--ground); }
.btn:active { transform: scale(0.97); }
.btn:focus-visible { outline: 2px solid var(--accent); outline-offset: 3px; }
.btn:disabled { opacity: 0.6; cursor: not-allowed; }

/* full-bleed banner */
.harvest-banner {
  position: relative;
  background: var(--ground-2);
  padding: 34px 0 26px;
  overflow: hidden;
}
.harvest-row {
  max-width: 1180px;
  margin: 0 auto;
  padding: 0 28px;
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 12px;
}
.harvest-row .berry-icon { flex: 0 1 130px; }
.harvest-row .berry-icon:nth-child(1) { transform: rotate(-6deg) translateY(6px); }
.harvest-row .berry-icon:nth-child(2) { flex-basis: 100px; transform: translateY(-10px); }
.harvest-row .berry-icon:nth-child(3) { flex-basis: 150px; transform: rotate(4deg); }
.harvest-row .berry-icon:nth-child(4) { flex-basis: 95px; transform: rotate(-4deg) translateY(4px); }
.harvest-row .berry-icon:nth-child(5) { flex-basis: 120px; transform: rotate(7deg) translateY(-6px); }
.harvest-tag {
  position: absolute;
  left: 28px;
  top: 34px;
  background: var(--ink);
  color: var(--ground);
  font-weight: 700;
  font-size: 12.5px;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  padding: 8px 16px;
  border-radius: 999px;
  z-index: 2;
}
@media (max-width: 780px) {
  .harvest-row .berry-icon:nth-child(5) { display: none; }
  .harvest-row .berry-icon { flex-basis: 80px !important; }
}

/* market */
.market { max-width: 1180px; margin: 0 auto; padding: 44px 28px 20px; }
.market-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 20px;
  flex-wrap: wrap;
  margin-bottom: 22px;
}
.market-head h2 { font-size: 30px; font-weight: 800; letter-spacing: -0.01em; }
.market-head .status { font-size: 13px; color: var(--leaf); font-weight: 700; }
.market-head .status::before { content: "● "; }

.filter-row {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  margin-bottom: 22px;
}
.chips { display: flex; gap: 8px; flex-wrap: wrap; }
.chip {
  font-weight: 700;
  font-size: 13px;
  padding: 9px 16px;
  border-radius: 999px;
  border: 2px solid var(--ink);
  background: transparent;
  color: var(--ink);
  cursor: pointer;
}
.chip:hover { border-color: var(--accent); }
.chip.active { background: var(--ink); color: var(--ground); border-color: var(--ink); }
.chip:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.search-input {
  background: var(--panel);
  border: 2px solid var(--line-strong);
  border-radius: 999px;
  padding: 9px 18px;
  min-width: 220px;
  color: var(--ink);
  margin-left: auto;
}
.search-input::placeholder { color: var(--ink-muted); }
.search-input:focus-visible { outline: 2px solid var(--accent); outline-offset: 1px; }
@media (max-width: 640px) { .search-input { margin-left: 0; width: 100%; } }

.grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(250px, 1fr));
  gap: 22px;
  padding-bottom: 10px;
}
.card {
  background: var(--panel);
  border: 1px solid var(--line);
  border-radius: 10px;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  box-shadow: var(--shadow);
}
.card .art {
  position: relative;
  background: var(--ground-2);
  height: 152px;
  display: flex;
  align-items: center;
  justify-content: center;
}
.card .art .berry-icon { width: 96px; height: 96px; }
.card .price-tag {
  position: absolute;
  right: 12px;
  bottom: -14px;
  background: var(--ink);
  color: var(--ground);
  font-family: var(--font-mono);
  font-size: 13px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  padding: 7px 12px;
  border-radius: 999px;
}
.card-body { padding: 24px 20px 20px; display: flex; flex-direction: column; gap: 8px; flex-grow: 1; }
.card h3 { font-size: 20px; font-weight: 800; }
.card .farm { font-size: 12px; text-transform: uppercase; letter-spacing: 0.06em; color: var(--accent); font-weight: 700; }
.card .note { font-family: var(--font-accent); font-style: italic; font-size: 14.5px; color: var(--ink-muted); flex-grow: 1; margin: 2px 0 0; }
.card .qty { font-family: var(--font-mono); font-size: 12.5px; font-variant-numeric: tabular-nums; color: var(--ink-muted); }
.card .qty.low { color: var(--accent); font-weight: 700; }
.card .status-badge { font-family: var(--font-mono); font-size: 12px; text-transform: uppercase; letter-spacing: 0.04em; color: var(--leaf); font-weight: 700; }
.card-foot { display: flex; align-items: center; justify-content: space-between; gap: 10px; margin-top: 6px; }
.btn-buy {
  font-weight: 700;
  font-size: 13px;
  background: var(--ink);
  border: 2px solid var(--ink);
  color: var(--ground);
  padding: 9px 18px;
  border-radius: 999px;
  cursor: pointer;
}
.btn-buy:hover:not(:disabled) { background: var(--accent); border-color: var(--accent); color: var(--accent-ink); }
.btn-buy:disabled {
  background: transparent;
  border-color: var(--line);
  color: var(--ink-muted);
  cursor: not-allowed;
}
.btn-buy:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.empty-state { color: var(--ink-muted); padding: 30px 0; grid-column: 1 / -1; }

/* sell / auth / reservations panels */
.sell, .auth, .reservations { max-width: 1180px; margin: 0 auto; padding: 50px 28px 80px; }
.reservations h2 { font-size: 30px; font-weight: 800; letter-spacing: -0.01em; margin-bottom: 22px; }
.panel-card {
  background: var(--panel);
  border: 1px solid var(--line);
  box-shadow: var(--shadow);
  border-radius: 20px;
  padding: 36px clamp(24px, 5vw, 56px);
  max-width: 640px;
  margin: 0 auto;
}
.panel-card > p { color: var(--ink-muted); margin: 8px 0 26px; }
.panel-card h2 { font-size: 26px; font-weight: 800; }
.panel-card .btn-primary { margin-top: 8px; width: 100%; text-align: center; }
.panel-card p:last-child { color: var(--ink-muted); font-size: 14px; margin: 18px 0 0; text-align: center; }

.form-errors {
  color: var(--accent);
  font-size: 13.5px;
  margin: 0 0 18px;
  padding-left: 18px;
}

.field { margin-bottom: 16px; display: flex; flex-direction: column; gap: 6px; }
.row-2 { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; }
label {
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--ink-muted);
  font-weight: 700;
}
input[type="text"], input[type="number"], input[type="email"], input[type="password"], input:not([type]) {
  border: 2px solid var(--line-strong);
  border-radius: 12px;
  background: var(--ground);
  padding: 10px 14px;
  color: var(--ink);
  width: 100%;
}
input:focus-visible { outline: none; border-color: var(--accent); }

footer {
  border-top: 1px solid var(--line);
  padding: 26px 28px 44px;
  max-width: 1180px;
  margin: 0 auto;
  display: flex;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 10px;
  color: var(--ink-muted);
  font-size: 13px;
}

.toast {
  position: fixed;
  left: 24px;
  bottom: 24px;
  background: var(--ink);
  color: var(--ground);
  padding: 13px 22px;
  font-size: 14px;
  font-weight: 600;
  border-radius: 999px;
  opacity: 0;
  transform: translateY(8px);
  transition: opacity 0.25s ease, transform 0.25s ease;
  pointer-events: none;
  z-index: 20;
}
.toast.show { opacity: 1; transform: translateY(0); }

@media (prefers-reduced-motion: reduce) {
  .btn, .toast, .btn-buy { transition: none; }
}

@media (max-width: 640px) {
  .row-2 { grid-template-columns: 1fr; }
}
```

- [ ] **Step 6: Implement ToastProvider**

Create `frontend/src/components/ToastProvider.tsx`:

```tsx
import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react';

interface ToastContextValue {
  showToast: (message: string) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

export function ToastProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState<string | null>(null);
  const timeoutRef = useRef<number | null>(null);

  const showToast = useCallback((next: string) => {
    setMessage(next);
    if (timeoutRef.current) {
      window.clearTimeout(timeoutRef.current);
    }
    timeoutRef.current = window.setTimeout(() => setMessage(null), 2200);
  }, []);

  return (
    <ToastContext.Provider value={{ showToast }}>
      {children}
      <div className={`toast${message ? ' show' : ''}`} role="status" aria-live="polite">
        {message}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): ToastContextValue {
  const ctx = useContext(ToastContext);
  if (!ctx) {
    throw new Error('useToast must be used within a ToastProvider');
  }
  return ctx;
}
```

`global.css` is not imported anywhere yet — that happens in Task 9 when `main.tsx` is rewritten. This task only needs to leave the build/tests green, which a standalone unimported CSS file does not affect.

- [ ] **Step 7: Run the full frontend test suite and commit**

```bash
npm test
```
Expected: PASS.

```bash
cd /Users/user/BerryMarketplace
git add frontend/src/styles/global.css frontend/src/components/BerryIcon.tsx \
        frontend/src/components/BerryIcon.test.tsx frontend/src/components/ToastProvider.tsx
git commit -m "Port design tokens, shared styles, BerryIcon, and ToastProvider from index.html"
```

---

### Task 4: API client layer

**Files:**
- Create: `frontend/src/api/client.ts`
- Test: `frontend/src/api/client.test.ts`
- Create: `frontend/src/api/types.ts`
- Create: `frontend/src/api/accounts.ts`
- Create: `frontend/src/api/listings.ts`
- Create: `frontend/src/api/reservations.ts`

**Interfaces:**
- Produces: `apiRequest<T>(path: string, init?: RequestInit): Promise<T>` and `class ApiError extends Error { status: number; errors: string[] }`, consumed by every `api/*.ts` module and by feature components' error handling (Tasks 5–8).
- Produces: `login`, `register`, `logout`, `getMe` (from `accounts.ts`); `getListings`, `createListing`, `reserveListing` (from `listings.ts`); `getMyReservations` (from `reservations.ts`) — consumed by Tasks 5–8. All are `vi.mock`-able module-level functions.
- Produces TypeScript types matching backend DTOs (camelCase, since ASP.NET Core Minimal APIs default to camelCase JSON): `UserResponse`, `ListingResponse`, `ReservationWithListingResponse`, `CreateListingRequest`, `LoginRequest`, `RegisterRequest`.

- [ ] **Step 1: Write the failing client tests**

Create `frontend/src/api/client.test.ts`:

```typescript
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiRequest, ApiError } from './client';

describe('apiRequest', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('sends credentials and a JSON content-type when a body is present', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ ok: true }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await apiRequest<{ ok: boolean }>('/thing', {
      method: 'POST',
      body: JSON.stringify({ a: 1 }),
    });

    expect(result).toEqual({ ok: true });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/thing');
    expect(init.credentials).toBe('include');
    expect(init.headers['Content-Type']).toBe('application/json');
  });

  it('throws ApiError with parsed errors on a non-2xx JSON error body', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ errors: ['BerryType is required.'] }), { status: 400 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    await expect(apiRequest('/listings', { method: 'POST', body: '{}' })).rejects.toMatchObject({
      status: 400,
      errors: ['BerryType is required.'],
    });
  });

  it('returns undefined for a 204 No Content response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })));

    const result = await apiRequest('/accounts/logout', { method: 'POST' });

    expect(result).toBeUndefined();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
npm test
```
Expected: FAIL — `Failed to resolve import "./client"`.

- [ ] **Step 3: Implement the API client**

Create `frontend/src/api/client.ts`:

```typescript
export class ApiError extends Error {
  status: number;
  errors: string[];

  constructor(status: number, errors: string[]) {
    super(errors.join(' '));
    this.status = status;
    this.errors = errors;
  }
}

async function parseErrorBody(response: Response): Promise<string[]> {
  try {
    const body = await response.json();
    if (Array.isArray(body?.errors)) return body.errors;
    if (typeof body?.error === 'string') return [body.error];
  } catch {
    // no JSON body
  }
  return [response.statusText || `Request failed with status ${response.status}`];
}

export async function apiRequest<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api${path}`, {
    ...init,
    credentials: 'include',
    headers: {
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
    },
  });

  if (response.status === 204) {
    return undefined as T;
  }

  if (!response.ok) {
    throw new ApiError(response.status, await parseErrorBody(response));
  }

  return (await response.json()) as T;
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
npm test
```
Expected: PASS.

- [ ] **Step 5: Add shared types**

Create `frontend/src/api/types.ts`:

```typescript
export interface UserResponse {
  id: string;
  email: string;
  displayName: string;
}

export interface ListingResponse {
  id: string;
  sellerId: string;
  berryType: string;
  farmName: string;
  pricePerPint: number;
  quantityAvailable: number;
  note: string | null;
  createdAt: string;
}

export interface ReservationWithListingResponse {
  id: string;
  listingId: string;
  quantity: number;
  status: string;
  reservedAt: string;
  berryType: string;
  farmName: string;
  pricePerPint: number;
}

export interface CreateListingRequest {
  berryType: string;
  farmName: string;
  pricePerPint: number;
  quantityAvailable: number;
  note: string | null;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
}
```

- [ ] **Step 6: Add the accounts, listings, and reservations API modules**

Create `frontend/src/api/accounts.ts`:

```typescript
import { apiRequest } from './client';
import type { LoginRequest, RegisterRequest, UserResponse } from './types';

export function login(request: LoginRequest): Promise<UserResponse> {
  return apiRequest<UserResponse>('/accounts/login', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function register(request: RegisterRequest): Promise<UserResponse> {
  return apiRequest<UserResponse>('/accounts/register', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function logout(): Promise<void> {
  return apiRequest<void>('/accounts/logout', { method: 'POST' });
}

export function getMe(): Promise<UserResponse> {
  return apiRequest<UserResponse>('/accounts/me');
}
```

Create `frontend/src/api/listings.ts`:

```typescript
import { apiRequest } from './client';
import type { CreateListingRequest, ListingResponse } from './types';

export function getListings(): Promise<ListingResponse[]> {
  return apiRequest<ListingResponse[]>('/listings');
}

export function createListing(request: CreateListingRequest): Promise<ListingResponse> {
  return apiRequest<ListingResponse>('/listings', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function reserveListing(listingId: string): Promise<void> {
  return apiRequest<void>(`/listings/${listingId}/reservations`, { method: 'POST' });
}
```

Create `frontend/src/api/reservations.ts`:

```typescript
import { apiRequest } from './client';
import type { ReservationWithListingResponse } from './types';

export function getMyReservations(): Promise<ReservationWithListingResponse[]> {
  return apiRequest<ReservationWithListingResponse[]>('/reservations/mine');
}
```

- [ ] **Step 7: Run the full frontend test suite and commit**

```bash
npm test
```
Expected: PASS.

```bash
cd /Users/user/BerryMarketplace
git add frontend/src/api/
git commit -m "Add API client layer with typed accounts, listings, and reservations calls"
```

---

### Task 5: Auth feature — useCurrentUser, RequireAuth, LoginPage, RegisterPage

**Files:**
- Create: `frontend/src/testUtils.tsx`
- Create: `frontend/src/features/auth/useCurrentUser.ts`
- Create: `frontend/src/features/auth/RequireAuth.tsx`
- Test: `frontend/src/features/auth/RequireAuth.test.tsx`
- Create: `frontend/src/features/auth/LoginPage.tsx`
- Test: `frontend/src/features/auth/LoginPage.test.tsx`
- Create: `frontend/src/features/auth/RegisterPage.tsx`

**Interfaces:**
- Consumes: `getMe`, `login`, `register` from `api/accounts.ts` (Task 4); `ApiError` from `api/client.ts` (Task 4).
- Produces: `useCurrentUser(): UseQueryResult<UserResponse | null>` and `CURRENT_USER_QUERY_KEY`, consumed by `Header` (Task 9) and `MarketPage` (Task 6).
- Produces: `RequireAuth` (a route-guard element for use with react-router's nested `<Route element={<RequireAuth />}>`), consumed by `App.tsx` (Task 9).
- Produces: `renderWithProviders(ui, { route? })` test helper, consumed by every feature test file from here on.

- [ ] **Step 1: Add the shared test-rendering helper**

Create `frontend/src/testUtils.tsx`:

```tsx
import type { ReactElement } from 'react';
import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ToastProvider } from './components/ToastProvider';

export function renderWithProviders(ui: ReactElement, { route = '/' }: { route?: string } = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[route]}>
        <ToastProvider>{ui}</ToastProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}
```

- [ ] **Step 2: Implement useCurrentUser**

Create `frontend/src/features/auth/useCurrentUser.ts`:

```typescript
import { useQuery } from '@tanstack/react-query';
import { ApiError } from '../../api/client';
import { getMe } from '../../api/accounts';
import type { UserResponse } from '../../api/types';

export const CURRENT_USER_QUERY_KEY = ['currentUser'];

export function useCurrentUser() {
  return useQuery<UserResponse | null>({
    queryKey: CURRENT_USER_QUERY_KEY,
    queryFn: async () => {
      try {
        return await getMe();
      } catch (err) {
        if (err instanceof ApiError && err.status === 401) {
          return null;
        }
        throw err;
      }
    },
  });
}
```

- [ ] **Step 3: Write the failing RequireAuth tests**

Create `frontend/src/features/auth/RequireAuth.test.tsx`:

```tsx
import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { Route, Routes } from 'react-router-dom';
import { renderWithProviders } from '../../testUtils';
import { RequireAuth } from './RequireAuth';
import * as accountsApi from '../../api/accounts';
import { ApiError } from '../../api/client';

vi.mock('../../api/accounts');

function TestRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<p>Login page</p>} />
      <Route element={<RequireAuth />}>
        <Route path="/sell" element={<p>Sell page</p>} />
      </Route>
    </Routes>
  );
}

describe('RequireAuth', () => {
  it('redirects to /login when there is no signed-in user', async () => {
    vi.mocked(accountsApi.getMe).mockRejectedValue(new ApiError(401, []));

    renderWithProviders(<TestRoutes />, { route: '/sell' });

    expect(await screen.findByText('Login page')).toBeInTheDocument();
  });

  it('renders the protected route when a user is signed in', async () => {
    vi.mocked(accountsApi.getMe).mockResolvedValue({
      id: 'user-1', email: 'buyer@example.com', displayName: 'Buyer',
    });

    renderWithProviders(<TestRoutes />, { route: '/sell' });

    expect(await screen.findByText('Sell page')).toBeInTheDocument();
  });
});
```

- [ ] **Step 4: Run the tests to verify they fail**

```bash
npm test
```
Expected: FAIL — `Failed to resolve import "./RequireAuth"`.

- [ ] **Step 5: Implement RequireAuth**

Create `frontend/src/features/auth/RequireAuth.tsx`:

```tsx
import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useCurrentUser } from './useCurrentUser';

export function RequireAuth() {
  const { data: user, isLoading } = useCurrentUser();
  const location = useLocation();

  if (isLoading) {
    return <p>Loading…</p>;
  }

  if (!user) {
    return <Navigate to="/login" state={{ from: location }} replace />;
  }

  return <Outlet />;
}
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
npm test
```
Expected: PASS.

- [ ] **Step 7: Write the failing LoginPage tests**

Create `frontend/src/features/auth/LoginPage.test.tsx`:

```tsx
import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../testUtils';
import { LoginPage } from './LoginPage';
import * as accountsApi from '../../api/accounts';
import { ApiError } from '../../api/client';

vi.mock('../../api/accounts');

describe('LoginPage', () => {
  it('logs in and shows a friendly message on invalid credentials', async () => {
    vi.mocked(accountsApi.login).mockRejectedValue(new ApiError(401, []));
    const user = userEvent.setup();

    renderWithProviders(<LoginPage />, { route: '/login' });

    await user.type(screen.getByLabelText('Email'), 'buyer@example.com');
    await user.type(screen.getByLabelText('Password'), 'wrong-password');
    await user.click(screen.getByRole('button', { name: 'Log in' }));

    expect(await screen.findByText('Invalid email or password.')).toBeInTheDocument();
  });

  it('submits credentials to the login API', async () => {
    vi.mocked(accountsApi.login).mockResolvedValue({
      id: 'user-1', email: 'buyer@example.com', displayName: 'Buyer',
    });
    const user = userEvent.setup();

    renderWithProviders(<LoginPage />, { route: '/login' });

    await user.type(screen.getByLabelText('Email'), 'buyer@example.com');
    await user.type(screen.getByLabelText('Password'), 'Password123!');
    await user.click(screen.getByRole('button', { name: 'Log in' }));

    await waitFor(() =>
      expect(accountsApi.login).toHaveBeenCalledWith({ email: 'buyer@example.com', password: 'Password123!' }),
    );
  });
});
```

- [ ] **Step 8: Run the tests to verify they fail**

```bash
npm test
```
Expected: FAIL — `Failed to resolve import "./LoginPage"`.

- [ ] **Step 9: Implement LoginPage**

Create `frontend/src/features/auth/LoginPage.tsx`:

```tsx
import { type FormEvent, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { login } from '../../api/accounts';
import { ApiError } from '../../api/client';
import { CURRENT_USER_QUERY_KEY } from './useCurrentUser';

export function LoginPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: () => login({ email, password }),
    onSuccess: (user) => {
      queryClient.setQueryData(CURRENT_USER_QUERY_KEY, user);
      const from = (location.state as { from?: { pathname: string } })?.from?.pathname ?? '/market';
      navigate(from, { replace: true });
    },
    onError: (err) => {
      if (err instanceof ApiError && err.status === 401) {
        setErrors(['Invalid email or password.']);
      } else if (err instanceof ApiError) {
        setErrors(err.errors);
      } else {
        setErrors(['Something went wrong — try again.']);
      }
    },
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setErrors([]);
    mutation.mutate();
  }

  return (
    <section className="auth">
      <div className="panel-card">
        <h2>Log in</h2>
        {errors.length > 0 && (
          <ul className="form-errors">
            {errors.map((error) => (
              <li key={error}>{error}</li>
            ))}
          </ul>
        )}
        <form onSubmit={handleSubmit}>
          <div className="field">
            <label htmlFor="login-email">Email</label>
            <input
              id="login-email"
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          </div>
          <div className="field">
            <label htmlFor="login-password">Password</label>
            <input
              id="login-password"
              type="password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </div>
          <button type="submit" className="btn btn-primary" disabled={mutation.isPending}>
            {mutation.isPending ? 'Logging in…' : 'Log in'}
          </button>
        </form>
        <p>
          Need an account? <Link to="/register">Register</Link>
        </p>
      </div>
    </section>
  );
}
```

Note: the `<label>` elements above rely on `htmlFor`/`id` pairing (not `aria-label`) so `screen.getByLabelText('Email')` in the test resolves correctly — this matches the pattern already used by `index.html`'s form fields.

- [ ] **Step 10: Run the tests to verify they pass**

```bash
npm test
```
Expected: PASS.

- [ ] **Step 11: Implement RegisterPage**

Create `frontend/src/features/auth/RegisterPage.tsx`:

```tsx
import { type FormEvent, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { register } from '../../api/accounts';
import { ApiError } from '../../api/client';
import { CURRENT_USER_QUERY_KEY } from './useCurrentUser';

export function RegisterPage() {
  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: () => register({ email, password, displayName }),
    onSuccess: (user) => {
      queryClient.setQueryData(CURRENT_USER_QUERY_KEY, user);
      navigate('/market', { replace: true });
    },
    onError: (err) => {
      setErrors(err instanceof ApiError ? err.errors : ['Something went wrong — try again.']);
    },
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setErrors([]);
    mutation.mutate();
  }

  return (
    <section className="auth">
      <div className="panel-card">
        <h2>Create an account</h2>
        {errors.length > 0 && (
          <ul className="form-errors">
            {errors.map((error) => (
              <li key={error}>{error}</li>
            ))}
          </ul>
        )}
        <form onSubmit={handleSubmit}>
          <div className="field">
            <label htmlFor="register-name">Your name</label>
            <input
              id="register-name"
              required
              maxLength={40}
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
            />
          </div>
          <div className="field">
            <label htmlFor="register-email">Email</label>
            <input
              id="register-email"
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          </div>
          <div className="field">
            <label htmlFor="register-password">Password</label>
            <input
              id="register-password"
              type="password"
              required
              minLength={8}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </div>
          <button type="submit" className="btn btn-primary" disabled={mutation.isPending}>
            {mutation.isPending ? 'Creating account…' : 'Create account'}
          </button>
        </form>
        <p>
          Already have an account? <Link to="/login">Log in</Link>
        </p>
      </div>
    </section>
  );
}
```

- [ ] **Step 12: Run the full frontend test suite and commit**

```bash
npm test
```
Expected: PASS.

```bash
cd /Users/user/BerryMarketplace
git add frontend/src/testUtils.tsx frontend/src/features/auth/
git commit -m "Add auth feature: useCurrentUser, RequireAuth, LoginPage, RegisterPage"
```

---

### Task 6: Market feature

**Files:**
- Create: `frontend/src/features/market/MarketPage.tsx`
- Test: `frontend/src/features/market/MarketPage.test.tsx`

**Interfaces:**
- Consumes: `getListings`, `reserveListing` from `api/listings.ts` (Task 4); `useCurrentUser` from `features/auth/useCurrentUser.ts` (Task 5); `useToast` from `components/ToastProvider.tsx` (Task 3); `BerryIcon` from `components/BerryIcon.tsx` (Task 3).
- Produces: `MarketPage`, consumed by `App.tsx` (Task 9).

- [ ] **Step 1: Write the failing MarketPage tests**

Create `frontend/src/features/market/MarketPage.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../testUtils';
import { MarketPage } from './MarketPage';
import * as listingsApi from '../../api/listings';
import * as accountsApi from '../../api/accounts';
import { ApiError } from '../../api/client';
import type { ListingResponse } from '../../api/types';

vi.mock('../../api/listings');
vi.mock('../../api/accounts');

const listings: ListingResponse[] = [
  {
    id: 'l1', sellerId: 'seller-1', berryType: 'Strawberries', farmName: 'Sunrow Farm',
    pricePerPint: 6.4, quantityAvailable: 3, note: null, createdAt: new Date().toISOString(),
  },
  {
    id: 'l2', sellerId: 'seller-2', berryType: 'Blueberries', farmName: 'Blue Hollow Orchard',
    pricePerPint: 5.2, quantityAvailable: 0, note: null, createdAt: new Date().toISOString(),
  },
];

beforeEach(() => {
  vi.mocked(accountsApi.getMe).mockRejectedValue(new ApiError(401, []));
});

describe('MarketPage', () => {
  it('filters listings by berry-type chip', async () => {
    vi.mocked(listingsApi.getListings).mockResolvedValue(listings);
    const user = userEvent.setup();

    renderWithProviders(<MarketPage />, { route: '/market' });

    await screen.findByText('Strawberries');
    await user.click(screen.getByRole('button', { name: 'Blueberries' }));

    expect(screen.queryByText('Strawberries')).not.toBeInTheDocument();
    expect(screen.getByText('Blueberries')).toBeInTheDocument();
  });

  it('disables buying a sold-out listing', async () => {
    vi.mocked(listingsApi.getListings).mockResolvedValue(listings);

    renderWithProviders(<MarketPage />, { route: '/market' });

    const blueberryCard = (await screen.findByText('Blueberries')).closest('.card') as HTMLElement;
    expect(within(blueberryCard).getByRole('button', { name: 'Sold out' })).toBeDisabled();
  });

  it('optimistically decrements quantity on buy, then rolls back on 409', async () => {
    vi.mocked(listingsApi.getListings).mockResolvedValue(listings);
    vi.mocked(listingsApi.reserveListing).mockRejectedValue(new ApiError(409, ['Sold out.']));
    const user = userEvent.setup();

    renderWithProviders(<MarketPage />, { route: '/market' });

    const strawberryCard = (await screen.findByText('Strawberries')).closest('.card') as HTMLElement;
    await user.click(within(strawberryCard).getByRole('button', { name: 'Buy a pint' }));

    await waitFor(() => expect(within(strawberryCard).getByText('2 pts left')).toBeInTheDocument());
    await waitFor(() => expect(within(strawberryCard).getByText('3 pts left')).toBeInTheDocument());
    expect(await screen.findByText('Sold out.')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
npm test
```
Expected: FAIL — `Failed to resolve import "./MarketPage"`.

- [ ] **Step 3: Implement MarketPage**

Create `frontend/src/features/market/MarketPage.tsx`:

```tsx
import { useMemo, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getListings, reserveListing } from '../../api/listings';
import { ApiError } from '../../api/client';
import { useCurrentUser } from '../auth/useCurrentUser';
import { useToast } from '../../components/ToastProvider';
import { BerryIcon } from '../../components/BerryIcon';
import type { ListingResponse } from '../../api/types';

const LISTINGS_QUERY_KEY = ['listings'];
const HARVEST_BERRIES = ['Strawberries', 'Blueberries', 'Raspberries', 'Blackberries', 'Gooseberries'];

function formatPrice(price: number): string {
  return `$${price.toFixed(2)}/pt`;
}

export function MarketPage() {
  const { data: user } = useCurrentUser();
  const { data: listings, isLoading } = useQuery<ListingResponse[]>({
    queryKey: LISTINGS_QUERY_KEY,
    queryFn: getListings,
  });
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const navigate = useNavigate();
  const location = useLocation();
  const [activeType, setActiveType] = useState('all');
  const [search, setSearch] = useState('');

  const reserveMutation = useMutation({
    mutationFn: (listingId: string) => reserveListing(listingId),
    onMutate: async (listingId: string) => {
      await queryClient.cancelQueries({ queryKey: LISTINGS_QUERY_KEY });
      const previous = queryClient.getQueryData<ListingResponse[]>(LISTINGS_QUERY_KEY);
      queryClient.setQueryData<ListingResponse[]>(LISTINGS_QUERY_KEY, (current) =>
        current?.map((listing) =>
          listing.id === listingId
            ? { ...listing, quantityAvailable: listing.quantityAvailable - 1 }
            : listing,
        ),
      );
      return { previous };
    },
    onError: (err, _listingId, context) => {
      if (context?.previous) {
        queryClient.setQueryData(LISTINGS_QUERY_KEY, context.previous);
      }
      if (err instanceof ApiError && err.status === 401) {
        navigate('/login', { state: { from: location } });
        return;
      }
      showToast(err instanceof ApiError && err.status === 409 ? 'Sold out.' : 'Something went wrong — try again.');
    },
    onSuccess: (_data, listingId) => {
      const listing = listings?.find((l) => l.id === listingId);
      if (listing) {
        showToast(`Added a pint of ${listing.berryType.toLowerCase()} to your reservations.`);
      }
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: LISTINGS_QUERY_KEY });
    },
  });

  const types = useMemo(() => {
    const seen = new Set<string>();
    const out: string[] = [];
    (listings ?? []).forEach((listing) => {
      if (!seen.has(listing.berryType)) {
        seen.add(listing.berryType);
        out.push(listing.berryType);
      }
    });
    return out;
  }, [listings]);

  const filtered = useMemo(() => {
    let items = listings ?? [];
    if (activeType !== 'all') {
      items = items.filter((listing) => listing.berryType === activeType);
    }
    if (search.trim()) {
      const term = search.trim().toLowerCase();
      items = items.filter(
        (listing) =>
          listing.berryType.toLowerCase().includes(term) || listing.farmName.toLowerCase().includes(term),
      );
    }
    return items;
  }, [listings, activeType, search]);

  return (
    <>
      <section className="hero">
        <div className="hero-copy-left">
          <p className="eyebrow">Sunrow Valley</p>
          <h1>Berries, straight from the row.</h1>
        </div>
        <div className="hero-copy-right">
          <p className="lede">
            Berrow connects backyard growers and small orchards directly with buyers nearby — no
            middleman, no cold-chain trucking, just crates changing hands the same day they're picked.
          </p>
        </div>
      </section>

      <div className="harvest-banner">
        <span className="harvest-tag">Today's harvest</span>
        <div className="harvest-row">
          {HARVEST_BERRIES.map((berry) => (
            <BerryIcon key={berry} berryType={berry} />
          ))}
        </div>
      </div>

      <section className="market">
        <div className="market-head">
          <h2>The Market</h2>
          <span className="status">Fresh listings updated live</span>
        </div>
        <div className="filter-row">
          <div className="chips">
            <button
              type="button"
              className={`chip${activeType === 'all' ? ' active' : ''}`}
              onClick={() => setActiveType('all')}
            >
              All
            </button>
            {types.map((type) => (
              <button
                key={type}
                type="button"
                className={`chip${activeType === type ? ' active' : ''}`}
                onClick={() => setActiveType(type)}
              >
                {type}
              </button>
            ))}
          </div>
          <input
            className="search-input"
            type="search"
            placeholder="Search berries, farms…"
            aria-label="Search listings"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <div className="grid">
          {isLoading && <p className="empty-state">Loading the market…</p>}
          {!isLoading && filtered.length === 0 && <p className="empty-state">No crates match that search.</p>}
          {filtered.map((listing) => {
            const soldOut = listing.quantityAvailable <= 0;
            const low = !soldOut && listing.quantityAvailable <= 5;
            const isOwnListing = user?.id === listing.sellerId;
            return (
              <div className="card" key={listing.id}>
                <div className="art">
                  <BerryIcon berryType={listing.berryType} />
                  <span className="price-tag">{formatPrice(listing.pricePerPint)}</span>
                </div>
                <div className="card-body">
                  <h3>{listing.berryType}</h3>
                  <span className="farm">{listing.farmName}</span>
                  {listing.note && <p className="note">{listing.note}</p>}
                  <div className="card-foot">
                    <span className={`qty${low ? ' low' : ''}`}>
                      {soldOut ? 'Sold out' : `${listing.quantityAvailable} pt${listing.quantityAvailable === 1 ? '' : 's'} left`}
                    </span>
                    {!isOwnListing && (
                      <button
                        type="button"
                        className="btn-buy"
                        disabled={soldOut || reserveMutation.isPending}
                        onClick={() => reserveMutation.mutate(listing.id)}
                      >
                        {soldOut ? 'Sold out' : 'Buy a pint'}
                      </button>
                    )}
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      </section>
    </>
  );
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
npm test
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/features/market/
git commit -m "Add Market feature: browse, filter, search, and buy a pint"
```

---

### Task 7: Sell feature

**Files:**
- Create: `frontend/src/features/sell/SellPage.tsx`
- Test: `frontend/src/features/sell/SellPage.test.tsx`

**Interfaces:**
- Consumes: `createListing` from `api/listings.ts` (Task 4).
- Produces: `SellPage`, consumed by `App.tsx` (Task 9).

- [ ] **Step 1: Write the failing SellPage tests**

Create `frontend/src/features/sell/SellPage.test.tsx`:

```tsx
import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../testUtils';
import { SellPage } from './SellPage';
import * as listingsApi from '../../api/listings';
import { ApiError } from '../../api/client';

vi.mock('../../api/listings');

describe('SellPage', () => {
  it('shows backend validation errors inline', async () => {
    vi.mocked(listingsApi.createListing).mockRejectedValue(new ApiError(400, ['BerryType is required.']));
    const user = userEvent.setup();

    renderWithProviders(<SellPage />, { route: '/sell' });

    await user.type(screen.getByLabelText('Farm or garden'), 'Sunrow Farm');
    await user.type(screen.getByLabelText('Price per pint ($)'), '6.40');
    await user.type(screen.getByLabelText('Pints available'), '10');
    await user.click(screen.getByRole('button', { name: 'Post listing' }));

    expect(await screen.findByText('BerryType is required.')).toBeInTheDocument();
  });

  it('submits a new listing with the entered fields', async () => {
    vi.mocked(listingsApi.createListing).mockResolvedValue({
      id: 'listing-1', sellerId: 'user-1', berryType: 'Tayberries', farmName: 'Sunrow Farm',
      pricePerPint: 6.4, quantityAvailable: 10, note: null, createdAt: new Date().toISOString(),
    });
    const user = userEvent.setup();

    renderWithProviders(<SellPage />, { route: '/sell' });

    await user.type(screen.getByLabelText('Berry'), 'Tayberries');
    await user.type(screen.getByLabelText('Farm or garden'), 'Sunrow Farm');
    await user.type(screen.getByLabelText('Price per pint ($)'), '6.40');
    await user.type(screen.getByLabelText('Pints available'), '10');
    await user.click(screen.getByRole('button', { name: 'Post listing' }));

    await waitFor(() =>
      expect(listingsApi.createListing).toHaveBeenCalledWith({
        berryType: 'Tayberries', farmName: 'Sunrow Farm', pricePerPint: 6.4, quantityAvailable: 10, note: null,
      }),
    );
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
npm test
```
Expected: FAIL — `Failed to resolve import "./SellPage"`.

- [ ] **Step 3: Implement SellPage**

Create `frontend/src/features/sell/SellPage.tsx`:

```tsx
import { type FormEvent, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { createListing } from '../../api/listings';
import { ApiError } from '../../api/client';

const LISTINGS_QUERY_KEY = ['listings'];

export function SellPage() {
  const [berryType, setBerryType] = useState('');
  const [farmName, setFarmName] = useState('');
  const [pricePerPint, setPricePerPint] = useState('');
  const [quantityAvailable, setQuantityAvailable] = useState('');
  const [note, setNote] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: () =>
      createListing({
        berryType,
        farmName,
        pricePerPint: Number(pricePerPint),
        quantityAvailable: Number(quantityAvailable),
        note: note.trim() ? note.trim() : null,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: LISTINGS_QUERY_KEY });
      navigate('/market');
    },
    onError: (err) => {
      setErrors(err instanceof ApiError ? err.errors : ['Something went wrong — try again.']);
    },
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setErrors([]);
    mutation.mutate();
  }

  return (
    <section className="sell">
      <div className="panel-card">
        <h2>List your berries</h2>
        <p>Got a surplus from the garden or the orchard? Post a crate and Berrow lists it on the market instantly.</p>
        {errors.length > 0 && (
          <ul className="form-errors">
            {errors.map((error) => (
              <li key={error}>{error}</li>
            ))}
          </ul>
        )}
        <form onSubmit={handleSubmit}>
          <div className="field">
            <label htmlFor="f-name">Berry</label>
            <input
              id="f-name"
              required
              maxLength={40}
              placeholder="e.g. Tayberries"
              value={berryType}
              onChange={(e) => setBerryType(e.target.value)}
            />
          </div>
          <div className="field">
            <label htmlFor="f-farm">Farm or garden</label>
            <input
              id="f-farm"
              required
              maxLength={40}
              value={farmName}
              onChange={(e) => setFarmName(e.target.value)}
            />
          </div>
          <div className="row-2">
            <div className="field">
              <label htmlFor="f-price">Price per pint ($)</label>
              <input
                id="f-price"
                type="number"
                min="0.10"
                step="0.05"
                required
                value={pricePerPint}
                onChange={(e) => setPricePerPint(e.target.value)}
              />
            </div>
            <div className="field">
              <label htmlFor="f-qty">Pints available</label>
              <input
                id="f-qty"
                type="number"
                min="0"
                step="1"
                required
                value={quantityAvailable}
                onChange={(e) => setQuantityAvailable(e.target.value)}
              />
            </div>
          </div>
          <div className="field">
            <label htmlFor="f-note">Note (optional)</label>
            <input
              id="f-note"
              maxLength={80}
              placeholder="Sweet, a little tart, best by Friday"
              value={note}
              onChange={(e) => setNote(e.target.value)}
            />
          </div>
          <button type="submit" className="btn btn-primary" disabled={mutation.isPending}>
            {mutation.isPending ? 'Posting…' : 'Post listing'}
          </button>
        </form>
      </div>
    </section>
  );
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
npm test
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/features/sell/
git commit -m "Add Sell feature: post a new listing"
```

---

### Task 8: Reservations feature

**Files:**
- Create: `frontend/src/features/reservations/ReservationsPage.tsx`
- Test: `frontend/src/features/reservations/ReservationsPage.test.tsx`

**Interfaces:**
- Consumes: `getMyReservations` from `api/reservations.ts` (Task 4); `BerryIcon` from `components/BerryIcon.tsx` (Task 3).
- Produces: `ReservationsPage`, consumed by `App.tsx` (Task 9).

- [ ] **Step 1: Write the failing ReservationsPage tests**

Create `frontend/src/features/reservations/ReservationsPage.test.tsx`:

```tsx
import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../testUtils';
import { ReservationsPage } from './ReservationsPage';
import * as reservationsApi from '../../api/reservations';

vi.mock('../../api/reservations');

describe('ReservationsPage', () => {
  it('shows an empty state with a link to the market when there are no reservations', async () => {
    vi.mocked(reservationsApi.getMyReservations).mockResolvedValue([]);

    renderWithProviders(<ReservationsPage />, { route: '/reservations' });

    expect(await screen.findByText(/No reservations yet/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'this way' })).toHaveAttribute('href', '/market');
  });

  it('lists reservations with their listing details', async () => {
    vi.mocked(reservationsApi.getMyReservations).mockResolvedValue([
      {
        id: 'r1', listingId: 'l1', quantity: 1, status: 'Pending', reservedAt: '2026-07-20T12:00:00Z',
        berryType: 'Gooseberries', farmName: 'Old Stone Orchard', pricePerPint: 8.5,
      },
    ]);

    renderWithProviders(<ReservationsPage />, { route: '/reservations' });

    expect(await screen.findByText('Gooseberries')).toBeInTheDocument();
    expect(screen.getByText('Old Stone Orchard')).toBeInTheDocument();
    expect(screen.getByText('Pending')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
npm test
```
Expected: FAIL — `Failed to resolve import "./ReservationsPage"`.

- [ ] **Step 3: Implement ReservationsPage**

Create `frontend/src/features/reservations/ReservationsPage.tsx`:

```tsx
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { getMyReservations } from '../../api/reservations';
import { BerryIcon } from '../../components/BerryIcon';
import type { ReservationWithListingResponse } from '../../api/types';

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

export function ReservationsPage() {
  const { data: reservations, isLoading } = useQuery<ReservationWithListingResponse[]>({
    queryKey: ['reservations', 'mine'],
    queryFn: getMyReservations,
  });

  return (
    <section className="reservations">
      <h2>My Reservations</h2>
      {isLoading && <p className="empty-state">Loading your reservations…</p>}
      {!isLoading && (reservations?.length ?? 0) === 0 && (
        <p className="empty-state">
          No reservations yet — the market's <Link to="/market">this way</Link>.
        </p>
      )}
      <div className="grid">
        {reservations?.map((reservation) => (
          <div className="card" key={reservation.id}>
            <div className="art">
              <BerryIcon berryType={reservation.berryType} />
              <span className="price-tag">${reservation.pricePerPint.toFixed(2)}/pt</span>
            </div>
            <div className="card-body">
              <h3>{reservation.berryType}</h3>
              <span className="farm">{reservation.farmName}</span>
              <div className="card-foot">
                <span className="status-badge">{reservation.status}</span>
                <span className="qty">{formatDate(reservation.reservedAt)}</span>
              </div>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
npm test
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/features/reservations/
git commit -m "Add Reservations feature: my reservations list"
```

---

### Task 9: App shell — Header, Footer, Layout, routing, and wiring

**Files:**
- Create: `frontend/src/components/Header.tsx`
- Create: `frontend/src/components/Footer.tsx`
- Create: `frontend/src/components/Layout.tsx`
- Modify: `frontend/src/App.tsx` (full rewrite)
- Test: `frontend/src/App.test.tsx`
- Modify: `frontend/src/main.tsx` (full rewrite)
- Modify: `frontend/index.html` (title)
- Delete: `frontend/src/App.css`
- Delete: `frontend/src/index.css`
- Delete: `frontend/src/assets/react.svg`

**Interfaces:**
- Consumes: everything produced by Tasks 3–8 (`ToastProvider`, `useCurrentUser`, `RequireAuth`, `LoginPage`, `RegisterPage`, `MarketPage`, `SellPage`, `ReservationsPage`).
- Produces: a fully wired `App` mounted by `main.tsx` — the app is now runnable end-to-end via `npm run dev`.

- [ ] **Step 1: Implement Header**

Create `frontend/src/components/Header.tsx`:

```tsx
import { Link } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useCurrentUser, CURRENT_USER_QUERY_KEY } from '../features/auth/useCurrentUser';
import { logout } from '../api/accounts';

export function Header() {
  const { data: user } = useCurrentUser();
  const queryClient = useQueryClient();
  const logoutMutation = useMutation({
    mutationFn: logout,
    onSuccess: () => {
      queryClient.setQueryData(CURRENT_USER_QUERY_KEY, null);
    },
  });

  return (
    <header className="site-header">
      <Link to="/market" className="wordmark">
        Berrow
      </Link>
      <nav className="site-nav">
        <Link to="/market">The Market</Link>
        <Link to="/sell">Sell Berries</Link>
        <Link to="/reservations">My Reservations</Link>
      </nav>
      {user ? (
        <div className="auth-status">
          <span>{user.displayName}</span>
          <button
            type="button"
            className="btn btn-ghost"
            onClick={() => logoutMutation.mutate()}
            disabled={logoutMutation.isPending}
          >
            Log out
          </button>
        </div>
      ) : (
        <Link to="/login" className="btn btn-ghost">
          Log in
        </Link>
      )}
    </header>
  );
}
```

- [ ] **Step 2: Implement Footer**

Create `frontend/src/components/Footer.tsx`:

```tsx
export function Footer() {
  return (
    <footer>
      <span>Berrow — a farm stand for the whole valley</span>
      <span>Est. {new Date().getFullYear()}</span>
    </footer>
  );
}
```

- [ ] **Step 3: Implement Layout**

Create `frontend/src/components/Layout.tsx`:

```tsx
import { Outlet } from 'react-router-dom';
import { Header } from './Header';
import { Footer } from './Footer';

export function Layout() {
  return (
    <>
      <Header />
      <main>
        <Outlet />
      </main>
      <Footer />
    </>
  );
}
```

- [ ] **Step 4: Write the failing App routing tests**

Create `frontend/src/App.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from './testUtils';
import { App } from './App';
import * as accountsApi from './api/accounts';
import * as listingsApi from './api/listings';
import { ApiError } from './api/client';

vi.mock('./api/accounts');
vi.mock('./api/listings');

beforeEach(() => {
  vi.mocked(accountsApi.getMe).mockRejectedValue(new ApiError(401, []));
  vi.mocked(listingsApi.getListings).mockResolvedValue([]);
});

describe('App', () => {
  it('redirects the index route to the market and shows a logged-out header', async () => {
    renderWithProviders(<App />, { route: '/' });

    expect(await screen.findByText('Berries, straight from the row.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Log in' })).toBeInTheDocument();
  });

  it('redirects an unauthenticated visitor away from /sell to /login', async () => {
    renderWithProviders(<App />, { route: '/sell' });

    expect(await screen.findByRole('heading', { name: 'Log in' })).toBeInTheDocument();
  });
});
```

- [ ] **Step 5: Run the tests to verify they fail**

```bash
npm test
```
Expected: FAIL — `App.tsx` still exports the default Vite template component, so `screen.findByText('Berries, straight from the row.')` never resolves (times out).

- [ ] **Step 6: Rewrite App.tsx**

Replace the full contents of `frontend/src/App.tsx` with:

```tsx
import { Navigate, Route, Routes } from 'react-router-dom';
import { Layout } from './components/Layout';
import { RequireAuth } from './features/auth/RequireAuth';
import { LoginPage } from './features/auth/LoginPage';
import { RegisterPage } from './features/auth/RegisterPage';
import { MarketPage } from './features/market/MarketPage';
import { SellPage } from './features/sell/SellPage';
import { ReservationsPage } from './features/reservations/ReservationsPage';

export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Navigate to="/market" replace />} />
        <Route path="/market" element={<MarketPage />} />
        <Route path="/login" element={<LoginPage />} />
        <Route path="/register" element={<RegisterPage />} />
        <Route element={<RequireAuth />}>
          <Route path="/sell" element={<SellPage />} />
          <Route path="/reservations" element={<ReservationsPage />} />
        </Route>
      </Route>
    </Routes>
  );
}
```

- [ ] **Step 7: Run the tests to verify they pass**

```bash
npm test
```
Expected: PASS.

- [ ] **Step 8: Rewrite main.tsx to wire up all providers**

Replace the full contents of `frontend/src/main.tsx` with:

```tsx
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App } from './App';
import { ToastProvider } from './components/ToastProvider';
import './styles/global.css';

const queryClient = new QueryClient();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <ToastProvider>
          <App />
        </ToastProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
```

- [ ] **Step 9: Update the HTML shell title and remove now-dead scaffold files**

Modify `frontend/index.html` — change the `<title>` element to:

```html
    <title>Berrow — Farm Stand</title>
```

Delete the default-template files that are no longer referenced:

```bash
rm frontend/src/App.css frontend/src/index.css frontend/src/assets/react.svg
```

- [ ] **Step 10: Run the full frontend test suite and build**

```bash
npm test
```
Expected: PASS, full suite (client, BerryIcon, auth, market, sell, reservations, App) green.

```bash
npm run build
```
Expected: builds successfully.

- [ ] **Step 11: Manually verify the running app**

With the backend running (from `backend/`: `dotnet run --launch-profile http`, requires PostgreSQL reachable per `appsettings.Development.json`) and the frontend dev server running (from `frontend/`: `npm run dev`), visit `http://localhost:5173` and confirm:
- `/` redirects to `/market`, showing the hero, harvest banner, and listing grid (empty until a listing is posted).
- Registering an account logs you in and the header shows your display name.
- Posting a listing via `/sell` redirects to `/market` and the new listing appears.
- Buying a pint on a listing you don't own decrements its quantity and shows a toast.
- `/reservations` shows the reservation just made.
- Logging out returns the header to "Log in", and visiting `/sell` redirects to `/login`.

- [ ] **Step 12: Commit**

```bash
git add frontend/src/components/Header.tsx frontend/src/components/Footer.tsx \
        frontend/src/components/Layout.tsx frontend/src/App.tsx frontend/src/App.test.tsx \
        frontend/src/main.tsx frontend/index.html
git rm frontend/src/App.css frontend/src/index.css frontend/src/assets/react.svg
git commit -m "Wire up App shell: Header, Footer, Layout, routing, and providers"
```

---

### Task 10: Retire the root index.html prototype

**Files:**
- Delete: `index.html` (repo root)
- Modify: `README.md`

**Interfaces:** none — this is a cleanup task with no code interfaces.

- [ ] **Step 1: Confirm parity with the running frontend**

With the frontend running from Task 9's manual verification, compare it side-by-side against the root `index.html` prototype (`open index.html` or `python3 -m http.server 8000` from the repo root). Confirm the React `/market` page matches the prototype's look and behavior: same hero copy, same harvest banner berries in the same order, same filter-chip and search behavior, same card layout, same buy-a-pint flow (now backed by the real API instead of `localStorage`). Minor intentional differences are expected and fine: no seller-name field (data model doesn't have one), and buying requires being logged in (the prototype had no accounts).

- [ ] **Step 2: Delete the prototype**

```bash
git rm index.html
```

- [ ] **Step 3: Update the README**

Modify `README.md` — replace the entire file contents with:

```markdown
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
```

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "Retire index.html prototype now that the React frontend has parity"
```

---

## Self-Review Notes

- **Spec coverage:** all four routes (auth/market/sell/reservations), the API client layer, design-token/shared-UI port, optimistic buy-with-rollback, inline validation errors, auth-gated routing, and the `GET /api/reservations/mine` endpoint are each covered by a task. Out-of-scope items from the design spec (pagination, edit/delete, real-time updates, seller-name field) are correctly absent.
- **Placeholder scan:** no TBD/TODO markers; every step shows complete, runnable code.
- **Type consistency:** `ReservationWithListingResponse` fields (`id`, `listingId`, `quantity`, `status`, `reservedAt`, `berryType`, `farmName`, `pricePerPint`) match between the backend record (Task 1) and the frontend `types.ts` interface (Task 4) and its consumers (Task 8). `ListingResponse` fields match between `ListingDtos.cs` and `types.ts`. `CURRENT_USER_QUERY_KEY` is defined once in `useCurrentUser.ts` (Task 5) and imported everywhere else it's used (Header in Task 9, LoginPage/RegisterPage in Task 5) rather than redefined.
