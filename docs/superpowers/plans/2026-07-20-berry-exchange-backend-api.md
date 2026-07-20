# Berry Exchange Backend API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the ASP.NET Core Web API described in `docs/superpowers/specs/2026-07-20-berry-exchange-architecture-design.md` — Accounts, Listings, and Reservations modules over PostgreSQL — as a working, independently testable backend, with the concurrency-safe "buy a pint" flow proven under real simultaneous requests.

**Architecture:** Modular monolith (ADR-0001): one ASP.NET Core Web API, three vertical-slice module folders (`Accounts/`, `Listings/`, `Reservations/`), each owning its own entity + service + minimal-API endpoints, sharing one `BerryExchangeDbContext`. No frontend or Docker Compose in this plan — those are separate follow-on plans (frontend SPA, then self-host deployment) once this backend is proven on its own.

**Tech Stack:** .NET 10 (installed SDK: `10.0.302`), ASP.NET Core minimal APIs, EF Core with `Npgsql.EntityFrameworkCore.PostgreSQL`, ASP.NET Core Identity (`IdentityCore`, cookie auth per ADR-0004), xUnit, `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`), `Testcontainers.PostgreSql` for integration tests against a real Postgres.

## Global Constraints

- Target framework: `net10.0` (matches the installed SDK — do not downgrade to net8.0).
- Database: PostgreSQL only, accessed via EF Core + Npgsql (ADR-0002). No SQL Server, no SQLite, not even for tests — the reservation concurrency guarantee depends on real Postgres semantics.
- Auth: ASP.NET Core Identity with same-site session cookies, never JWT bearer tokens (ADR-0004).
- Module boundary: `Accounts/`, `Listings/`, `Reservations/` folders never reference each other's EF entities directly — only through the small set of service methods this plan defines (ADR-0001).
- The reservation stock decrement must go through a single atomic conditional `UPDATE ... WHERE QuantityAvailable > 0`, never a read-then-write (`docs/architecture/reservation-flow.mmd`).
- No payment processing of any kind — reservation-only, out of scope per the spec.
- All environment-specific config (connection strings) comes from configuration/env vars, never hardcoded (ADR-0005) — `appsettings.Development.json` may hold a non-secret local-dev default, nothing else.
- **Docker must be installed and running locally** — the integration test suite uses Testcontainers to run real Postgres per test run. This is not a new dependency: ADR-0005 already commits to Docker for self-hosting.
- **This repo has a pre-commit hook (ADR-0006)** that blocks commits touching `*.csproj`, `Program.cs`, or `**/Migrations/*.cs` unless a `docs/adr/*.md` and `docs/architecture/*.mmd` file is staged too. Every task in this plan implements architecture already decided and diagrammed in ADRs 0001–0006 — it introduces no new decision. Use `git commit --no-verify` for this plan's commits unless a step says otherwise.

---

### Task 1: Scaffold the .NET solution

**Files:**
- Create: `backend/BerryExchange.sln`
- Create: `backend/src/BerryExchange.Api/BerryExchange.Api.csproj`
- Create: `backend/src/BerryExchange.Api/Program.cs`
- Create: `backend/tests/BerryExchange.Api.Tests/BerryExchange.Api.Tests.csproj`

**Interfaces:**
- Produces: an empty minimal-API `Program.cs` with `public partial class Program { }` at the bottom (required so `WebApplicationFactory<Program>` can reference it from the test project in Task 2).

- [ ] **Step 1: Create the solution and both projects**

```bash
mkdir -p backend/src backend/tests
cd backend
dotnet new sln -n BerryExchange
dotnet new web -n BerryExchange.Api -o src/BerryExchange.Api
dotnet new xunit -n BerryExchange.Api.Tests -o tests/BerryExchange.Api.Tests
dotnet sln add src/BerryExchange.Api/BerryExchange.Api.csproj
dotnet sln add tests/BerryExchange.Api.Tests/BerryExchange.Api.Tests.csproj
dotnet add tests/BerryExchange.Api.Tests/BerryExchange.Api.Tests.csproj reference src/BerryExchange.Api/BerryExchange.Api.csproj
```

- [ ] **Step 2: Remove the xunit template's placeholder test file**

```bash
rm backend/tests/BerryExchange.Api.Tests/UnitTest1.cs
```

- [ ] **Step 3: Add the `Program` partial class marker**

Append to `backend/src/BerryExchange.Api/Program.cs` (the `dotnet new web` template already has `var app = builder.Build(); ... app.Run();` above this — add the line after `app.Run();`):

```csharp
public partial class Program { }
```

- [ ] **Step 4: Verify the solution builds**

Run: `cd backend && dotnet build`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 5: Commit**

```bash
cd backend
git add BerryExchange.sln src/ tests/
git commit --no-verify -m "Scaffold BerryExchange.Api solution and test project"
```

---

### Task 2: PostgreSQL + Identity data layer and test infrastructure

**Files:**
- Create: `backend/src/BerryExchange.Api/Accounts/ApplicationUser.cs`
- Create: `backend/src/BerryExchange.Api/Infrastructure/BerryExchangeDbContext.cs`
- Create: `backend/src/BerryExchange.Api/appsettings.Development.json`
- Modify: `backend/src/BerryExchange.Api/Program.cs`
- Create: `backend/tests/BerryExchange.Api.Tests/ApiTestFixture.cs`
- Create: `backend/tests/BerryExchange.Api.Tests/Infrastructure/DbContextSmokeTests.cs`
- Create: EF Core migration files under `backend/src/BerryExchange.Api/Infrastructure/Migrations/` (generated, not hand-written)

**Interfaces:**
- Produces: `ApplicationUser : IdentityUser<Guid>` with `string DisplayName { get; set; }`.
- Produces: `BerryExchangeDbContext(DbContextOptions<BerryExchangeDbContext> options) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`.
- Produces: `ApiTestFixture : WebApplicationFactory<Program>, IAsyncLifetime` — every later test task depends on this for `IClassFixture<ApiTestFixture>`.

- [ ] **Step 1: Add NuGet packages**

```bash
cd backend
dotnet add src/BerryExchange.Api package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/BerryExchange.Api package Microsoft.AspNetCore.Identity.EntityFrameworkCore
dotnet add src/BerryExchange.Api package Microsoft.EntityFrameworkCore.Design
dotnet add tests/BerryExchange.Api.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/BerryExchange.Api.Tests package Testcontainers.PostgreSql
dotnet tool install --global dotnet-ef || dotnet tool update --global dotnet-ef
```

- [ ] **Step 2: Write `ApplicationUser.cs`**

```csharp
using Microsoft.AspNetCore.Identity;

namespace BerryExchange.Api.Accounts;

public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Write `BerryExchangeDbContext.cs`**

```csharp
using BerryExchange.Api.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BerryExchange.Api.Infrastructure;

public class BerryExchangeDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public BerryExchangeDbContext(DbContextOptions<BerryExchangeDbContext> options) : base(options) { }
}
```

- [ ] **Step 4: Wire the DbContext and Identity core into `Program.cs`**

Add near the top of `backend/src/BerryExchange.Api/Program.cs`, right after `var builder = WebApplication.CreateBuilder(args);`:

```csharp
using BerryExchange.Api.Accounts;
using BerryExchange.Api.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var connectionString = builder.Configuration.GetConnectionString("BerryExchangeDb")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:BerryExchangeDb");

builder.Services.AddDbContext<BerryExchangeDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 8;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<BerryExchangeDbContext>();
```

(The `using` statements go at the very top of the file, above `var builder = ...`, per normal C# top-level statement rules — move them there rather than leaving them inline.)

- [ ] **Step 5: Add the local-dev connection string default**

Create `backend/src/BerryExchange.Api/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "BerryExchangeDb": "Host=localhost;Port=5432;Database=berryexchange_dev;Username=postgres;Password=postgres"
  }
}
```

- [ ] **Step 6: Generate the initial migration**

```bash
cd backend
dotnet ef migrations add InitialIdentity --project src/BerryExchange.Api --startup-project src/BerryExchange.Api --output-dir Infrastructure/Migrations
```

Expected: a `Migrations/` folder appears under `src/BerryExchange.Api/Infrastructure/` with `*_InitialIdentity.cs` and `BerryExchangeDbContextModelSnapshot.cs`.

- [ ] **Step 7: Write the shared test fixture**

Create `backend/tests/BerryExchange.Api.Tests/ApiTestFixture.cs`:

```csharp
using BerryExchange.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace BerryExchange.Api.Tests;

public class ApiTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("berryexchange_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BerryExchangeDbContext>();
        await db.Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BerryExchangeDb"] = _postgres.GetConnectionString()
            });
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }
}
```

- [ ] **Step 8: Write the failing smoke test**

Create `backend/tests/BerryExchange.Api.Tests/Infrastructure/DbContextSmokeTests.cs`:

```csharp
using BerryExchange.Api.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BerryExchange.Api.Tests.Infrastructure;

public class DbContextSmokeTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;

    public DbContextSmokeTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migrations_apply_and_a_user_can_be_created_and_found()
    {
        using var scope = _fixture.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "smoke@example.com",
            Email = "smoke@example.com",
            DisplayName = "Smoke Test"
        };
        var result = await userManager.CreateAsync(user, "Password123!");

        Assert.True(result.Succeeded);
        var found = await userManager.FindByEmailAsync("smoke@example.com");
        Assert.NotNull(found);
        Assert.Equal("Smoke Test", found!.DisplayName);
    }
}
```

- [ ] **Step 9: Run the test to verify it passes**

Run: `cd backend && dotnet test --filter DbContextSmokeTests`
Expected: `Passed!` — 1 passed. (First run pulls the `postgres:16-alpine` image; allow extra time.)

- [ ] **Step 10: Commit**

```bash
cd backend
git add src/BerryExchange.Api/Accounts src/BerryExchange.Api/Infrastructure src/BerryExchange.Api/Program.cs src/BerryExchange.Api/appsettings.Development.json src/BerryExchange.Api/*.csproj tests/
git commit --no-verify -m "Add PostgreSQL/Identity data layer and Testcontainers-based test fixture"
```

---

### Task 3: Accounts module — cookie auth, register/login/logout/me

**Files:**
- Create: `backend/src/BerryExchange.Api/Accounts/AccountDtos.cs`
- Create: `backend/src/BerryExchange.Api/Accounts/AccountsEndpoints.cs`
- Modify: `backend/src/BerryExchange.Api/Program.cs`
- Create: `backend/tests/BerryExchange.Api.Tests/Accounts/AccountsEndpointsTests.cs`

**Interfaces:**
- Consumes: `ApplicationUser` (Task 2), `ApiTestFixture` (Task 2).
- Produces: `RegisterRequest(string Email, string Password, string DisplayName)`, `LoginRequest(string Email, string Password)`, `UserResponse(Guid Id, string Email, string DisplayName)` — later tasks' tests reuse these for registering test users.
- Produces: `WebApplication.MapAccountsEndpoints()` extension method, mounted at `/api/accounts/{register,login,logout,me}`.

- [ ] **Step 1: Write `AccountDtos.cs`**

```csharp
namespace BerryExchange.Api.Accounts;

public record RegisterRequest(string Email, string Password, string DisplayName);
public record LoginRequest(string Email, string Password);
public record UserResponse(Guid Id, string Email, string DisplayName);
```

- [ ] **Step 2: Write `AccountsEndpoints.cs`**

```csharp
using Microsoft.AspNetCore.Identity;

namespace BerryExchange.Api.Accounts;

public static class AccountsEndpoints
{
    public static void MapAccountsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/accounts");

        group.MapPost("/register", async (
            RegisterRequest request,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager) =>
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = request.Email,
                Email = request.Email,
                DisplayName = request.DisplayName
            };
            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });
            }
            await signInManager.SignInAsync(user, isPersistent: true);
            return Results.Ok(new UserResponse(user.Id, user.Email!, user.DisplayName));
        });

        group.MapPost("/login", async (
            LoginRequest request,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user is null)
            {
                return Results.Unauthorized();
            }
            var result = await signInManager.PasswordSignInAsync(user, request.Password, isPersistent: true, lockoutOnFailure: false);
            return result.Succeeded
                ? Results.Ok(new UserResponse(user.Id, user.Email!, user.DisplayName))
                : Results.Unauthorized();
        });

        group.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Ok();
        });

        group.MapGet("/me", (HttpContext http) =>
        {
            if (http.User.Identity?.IsAuthenticated != true)
            {
                return Results.Unauthorized();
            }
            var id = Guid.Parse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var email = http.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
            return Results.Ok(new { id, email });
        }).RequireAuthorization();
    }
}
```

- [ ] **Step 3: Wire cookie authentication into `Program.cs`**

Add this block **before** the existing `builder.Services.AddIdentityCore<ApplicationUser>(...)` call from Task 2:

```csharp
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
```

Then change the Task 2 Identity registration to add `.AddSignInManager()` at the end:

```csharp
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 8;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<BerryExchangeDbContext>()
    .AddSignInManager();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "BerryExchange.Auth";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.HttpOnly = true;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization();
```

Then, between `var app = builder.Build();` and `app.Run();`, add:

```csharp
app.UseAuthentication();
app.UseAuthorization();

app.MapAccountsEndpoints();
```

- [ ] **Step 4: Write the failing tests**

Create `backend/tests/BerryExchange.Api.Tests/Accounts/AccountsEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using BerryExchange.Api.Accounts;
using Xunit;

namespace BerryExchange.Api.Tests.Accounts;

public class AccountsEndpointsTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;

    public AccountsEndpointsTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Register_then_me_returns_the_new_user()
    {
        var client = _fixture.CreateClient();

        var registerResponse = await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "accounts-seller@example.com", Password: "Password123!", DisplayName: "Seller One"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var me = await client.GetAsync("/api/accounts/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Me_without_a_session_returns_unauthorized()
    {
        var client = _fixture.CreateClient();

        var me = await client.GetAsync("/api/accounts/me");

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_unauthorized()
    {
        var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "accounts-buyer@example.com", Password: "Password123!", DisplayName: "Buyer One"));
        await client.PostAsync("/api/accounts/logout", null);

        var loginResponse = await client.PostAsJsonAsync("/api/accounts/login", new LoginRequest(
            Email: "accounts-buyer@example.com", Password: "WrongPassword!"));

        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd backend && dotnet test --filter AccountsEndpointsTests`
Expected: `Passed!` — 3 passed.

- [ ] **Step 6: Commit**

```bash
cd backend
git add src/BerryExchange.Api/Accounts src/BerryExchange.Api/Program.cs tests/BerryExchange.Api.Tests/Accounts
git commit --no-verify -m "Add Accounts module: cookie auth, register/login/logout/me"
```

---

### Task 4: Listings module

**Files:**
- Create: `backend/src/BerryExchange.Api/Listings/Listing.cs`
- Create: `backend/src/BerryExchange.Api/Listings/ListingDtos.cs`
- Create: `backend/src/BerryExchange.Api/Listings/ListingsService.cs`
- Create: `backend/src/BerryExchange.Api/Listings/ListingsEndpoints.cs`
- Modify: `backend/src/BerryExchange.Api/Infrastructure/BerryExchangeDbContext.cs`
- Modify: `backend/src/BerryExchange.Api/Program.cs`
- Create: EF Core migration `AddListings`
- Create: `backend/tests/BerryExchange.Api.Tests/Listings/ListingsEndpointsTests.cs`

**Interfaces:**
- Consumes: `ApiTestFixture`, `RegisterRequest`/`UserResponse` (Task 2/3, for authenticating a seller in tests).
- Produces: `Listing` entity, `CreateListingRequest(string BerryType, string FarmName, decimal PricePerPint, int QuantityAvailable, string? Note)`, `ListingResponse` (with static `FromEntity(Listing)`), `ListingsService` with `Task<List<Listing>> GetAllAsync(CancellationToken)`, `Task<Listing?> GetByIdAsync(Guid, CancellationToken)`, `Task<Listing> CreateAsync(Guid sellerId, CreateListingRequest, CancellationToken)` — **Task 5's Reservations module depends on `GetByIdAsync` to look up the seller before allowing a reservation.**

- [ ] **Step 1: Write `Listing.cs`**

```csharp
namespace BerryExchange.Api.Listings;

public class Listing
{
    public Guid Id { get; set; }
    public Guid SellerId { get; set; }
    public string BerryType { get; set; } = string.Empty;
    public string FarmName { get; set; } = string.Empty;
    public decimal PricePerPint { get; set; }
    public int QuantityAvailable { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 2: Write `ListingDtos.cs`**

```csharp
namespace BerryExchange.Api.Listings;

public record CreateListingRequest(string BerryType, string FarmName, decimal PricePerPint, int QuantityAvailable, string? Note);

public record ListingResponse(
    Guid Id, Guid SellerId, string BerryType, string FarmName,
    decimal PricePerPint, int QuantityAvailable, string? Note, DateTimeOffset CreatedAt)
{
    public static ListingResponse FromEntity(Listing l) =>
        new(l.Id, l.SellerId, l.BerryType, l.FarmName, l.PricePerPint, l.QuantityAvailable, l.Note, l.CreatedAt);
}
```

- [ ] **Step 3: Write `ListingsService.cs`**

```csharp
using BerryExchange.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BerryExchange.Api.Listings;

public class ListingsService
{
    private readonly BerryExchangeDbContext _db;

    public ListingsService(BerryExchangeDbContext db)
    {
        _db = db;
    }

    public async Task<List<Listing>> GetAllAsync(CancellationToken ct)
    {
        return await _db.Listings.OrderByDescending(l => l.CreatedAt).ToListAsync(ct);
    }

    public async Task<Listing?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await _db.Listings.FirstOrDefaultAsync(l => l.Id == id, ct);
    }

    public async Task<Listing> CreateAsync(Guid sellerId, CreateListingRequest request, CancellationToken ct)
    {
        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            SellerId = sellerId,
            BerryType = request.BerryType,
            FarmName = request.FarmName,
            PricePerPint = request.PricePerPint,
            QuantityAvailable = request.QuantityAvailable,
            Note = request.Note,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Listings.Add(listing);
        await _db.SaveChangesAsync(ct);
        return listing;
    }
}
```

- [ ] **Step 4: Write `ListingsEndpoints.cs`**

```csharp
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
            var sellerId = Guid.Parse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var listing = await service.CreateAsync(sellerId, request, ct);
            return Results.Created($"/api/listings/{listing.Id}", ListingResponse.FromEntity(listing));
        }).RequireAuthorization();
    }
}
```

- [ ] **Step 5: Add the `Listing` DbSet to `BerryExchangeDbContext.cs`**

Modify `backend/src/BerryExchange.Api/Infrastructure/BerryExchangeDbContext.cs` to:

```csharp
using BerryExchange.Api.Accounts;
using BerryExchange.Api.Listings;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BerryExchange.Api.Infrastructure;

public class BerryExchangeDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public BerryExchangeDbContext(DbContextOptions<BerryExchangeDbContext> options) : base(options) { }

    public DbSet<Listing> Listings => Set<Listing>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Listing>(entity =>
        {
            entity.Property(l => l.BerryType).HasMaxLength(40).IsRequired();
            entity.Property(l => l.FarmName).HasMaxLength(40).IsRequired();
            entity.Property(l => l.Note).HasMaxLength(80);
            entity.Property(l => l.PricePerPint).HasColumnType("numeric(10,2)");
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(l => l.SellerId);
        });
    }
}
```

- [ ] **Step 6: Register the service and endpoints in `Program.cs`**

Add `builder.Services.AddScoped<ListingsService>();` alongside the other `builder.Services.Add...` calls, and `app.MapListingsEndpoints();` alongside `app.MapAccountsEndpoints();`.

- [ ] **Step 7: Generate the migration**

```bash
cd backend
dotnet ef migrations add AddListings --project src/BerryExchange.Api --startup-project src/BerryExchange.Api --output-dir Infrastructure/Migrations
```

- [ ] **Step 8: Write the failing tests**

Create `backend/tests/BerryExchange.Api.Tests/Listings/ListingsEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using BerryExchange.Api.Accounts;
using BerryExchange.Api.Listings;
using Xunit;

namespace BerryExchange.Api.Tests.Listings;

public class ListingsEndpointsTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;

    public ListingsEndpointsTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_without_auth_returns_unauthorized()
    {
        var client = _fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Blueberries", FarmName: "Blue Hollow Orchard", PricePerPint: 5.2m, QuantityAvailable: 10, Note: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_create_then_list_contains_it()
    {
        var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "listings-seller@example.com", Password: "Password123!", DisplayName: "Listings Seller"));

        var createResponse = await client.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Raspberries", FarmName: "Thistlewood Farm", PricePerPint: 7.8m, QuantityAvailable: 9, Note: "Delicate."));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ListingResponse>();

        var listResponse = await client.GetAsync("/api/listings");
        var listings = await listResponse.Content.ReadFromJsonAsync<List<ListingResponse>>();

        Assert.Contains(listings!, l => l.Id == created!.Id);
    }

    [Fact]
    public async Task Get_by_id_for_unknown_listing_returns_not_found()
    {
        var client = _fixture.CreateClient();

        var response = await client.GetAsync($"/api/listings/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `cd backend && dotnet test --filter ListingsEndpointsTests`
Expected: `Passed!` — 3 passed.

- [ ] **Step 10: Commit**

```bash
cd backend
git add src/BerryExchange.Api/Listings src/BerryExchange.Api/Infrastructure src/BerryExchange.Api/Program.cs tests/BerryExchange.Api.Tests/Listings
git commit --no-verify -m "Add Listings module: create/list/get endpoints"
```

---

### Task 5: Reservations module — the atomic "buy a pint" flow

**Files:**
- Create: `backend/src/BerryExchange.Api/Reservations/Reservation.cs`
- Create: `backend/src/BerryExchange.Api/Reservations/ReservationDtos.cs`
- Create: `backend/src/BerryExchange.Api/Reservations/ReservationsService.cs`
- Create: `backend/src/BerryExchange.Api/Reservations/ReservationsEndpoints.cs`
- Modify: `backend/src/BerryExchange.Api/Infrastructure/BerryExchangeDbContext.cs`
- Modify: `backend/src/BerryExchange.Api/Program.cs`
- Create: EF Core migration `AddReservations`
- Create: `backend/tests/BerryExchange.Api.Tests/Reservations/ReservationsEndpointsTests.cs`

**Interfaces:**
- Consumes: `ListingsService.GetByIdAsync` (Task 4), `ApiTestFixture`, `RegisterRequest`, `CreateListingRequest`/`ListingResponse` (Tasks 2–4).
- Produces: `Reservation` entity, `ReservationStatus` enum, `ReservationResponse` (static `FromEntity(Reservation)`), `ReservationsService.ReserveAsync(Guid listingId, Guid buyerId, CancellationToken) : Task<ReserveResult>` — **Task 6's concurrency test calls this indirectly through the HTTP endpoint, not directly.**

- [ ] **Step 1: Write `Reservation.cs`**

```csharp
namespace BerryExchange.Api.Reservations;

public enum ReservationStatus { Pending, Completed, Cancelled }

public class Reservation
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public Guid BuyerId { get; set; }
    public int Quantity { get; set; }
    public ReservationStatus Status { get; set; }
    public DateTimeOffset ReservedAt { get; set; }
}
```

- [ ] **Step 2: Write `ReservationDtos.cs`**

```csharp
namespace BerryExchange.Api.Reservations;

public record ReservationResponse(Guid Id, Guid ListingId, Guid BuyerId, int Quantity, string Status, DateTimeOffset ReservedAt)
{
    public static ReservationResponse FromEntity(Reservation r) =>
        new(r.Id, r.ListingId, r.BuyerId, r.Quantity, r.Status.ToString(), r.ReservedAt);
}
```

- [ ] **Step 3: Write `ReservationsService.cs`**

This is the correctness-critical piece — a single atomic conditional `UPDATE`, per `docs/architecture/reservation-flow.mmd`, so two simultaneous requests on the last pint can't both succeed:

```csharp
using BerryExchange.Api.Infrastructure;

namespace BerryExchange.Api.Reservations;

public class ReservationsService
{
    private readonly BerryExchangeDbContext _db;

    public ReservationsService(BerryExchangeDbContext db)
    {
        _db = db;
    }

    public async Task<ReserveResult> ReserveAsync(Guid listingId, Guid buyerId, CancellationToken ct)
    {
        var rows = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Listings\" SET \"QuantityAvailable\" = \"QuantityAvailable\" - 1 WHERE \"Id\" = {listingId} AND \"QuantityAvailable\" > 0",
            ct);

        if (rows == 0)
        {
            return ReserveResult.SoldOut;
        }

        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            ListingId = listingId,
            BuyerId = buyerId,
            Quantity = 1,
            Status = ReservationStatus.Pending,
            ReservedAt = DateTimeOffset.UtcNow
        };
        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync(ct);

        return ReserveResult.Success(reservation);
    }
}

public class ReserveResult
{
    public bool Succeeded { get; }
    public Reservation? Reservation { get; }

    private ReserveResult(bool succeeded, Reservation? reservation)
    {
        Succeeded = succeeded;
        Reservation = reservation;
    }

    public static ReserveResult Success(Reservation r) => new(true, r);
    public static readonly ReserveResult SoldOut = new(false, null);
}
```

- [ ] **Step 4: Write `ReservationsEndpoints.cs`**

The self-reservation guard (a buyer can't reserve their own listing) is a separate up-front read, since it's a business rule check, not a concurrency-sensitive stock check:

```csharp
using BerryExchange.Api.Listings;

namespace BerryExchange.Api.Reservations;

public static class ReservationsEndpoints
{
    public static void MapReservationsEndpoints(this WebApplication app)
    {
        app.MapPost("/api/listings/{listingId:guid}/reservations", async (
            Guid listingId,
            HttpContext http,
            ReservationsService reservationsService,
            ListingsService listingsService,
            CancellationToken ct) =>
        {
            var buyerId = Guid.Parse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

            var listing = await listingsService.GetByIdAsync(listingId, ct);
            if (listing is null)
            {
                return Results.NotFound();
            }
            if (listing.SellerId == buyerId)
            {
                return Results.BadRequest(new { error = "You cannot reserve your own listing." });
            }

            var result = await reservationsService.ReserveAsync(listingId, buyerId, ct);
            return result.Succeeded
                ? Results.Created($"/api/reservations/{result.Reservation!.Id}", ReservationResponse.FromEntity(result.Reservation))
                : Results.Conflict(new { error = "Sold out." });
        }).RequireAuthorization();
    }
}
```

- [ ] **Step 5: Add the `Reservation` DbSet to `BerryExchangeDbContext.cs`**

Add to the `BerryExchangeDbContext` class from Task 4:

```csharp
public DbSet<Reservation> Reservations => Set<Reservation>();
```

And inside `OnModelCreating`, after the `Listing` entity block:

```csharp
builder.Entity<Reservation>(entity =>
{
    entity.HasOne<Listing>().WithMany().HasForeignKey(r => r.ListingId);
    entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.BuyerId);
});
```

Add `using BerryExchange.Api.Reservations;` to the top of the file alongside the existing usings.

- [ ] **Step 6: Register the service and endpoints in `Program.cs`**

Add `builder.Services.AddScoped<ReservationsService>();` and `app.MapReservationsEndpoints();` alongside the Listings equivalents.

- [ ] **Step 7: Generate the migration**

```bash
cd backend
dotnet ef migrations add AddReservations --project src/BerryExchange.Api --startup-project src/BerryExchange.Api --output-dir Infrastructure/Migrations
```

- [ ] **Step 8: Write the failing tests**

Create `backend/tests/BerryExchange.Api.Tests/Reservations/ReservationsEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using BerryExchange.Api.Accounts;
using BerryExchange.Api.Listings;
using Xunit;

namespace BerryExchange.Api.Tests.Reservations;

public class ReservationsEndpointsTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;

    public ReservationsEndpointsTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(ListingResponse Listing, HttpClient BuyerClient)> SeedListingAndBuyer(
        string sellerEmail, string buyerEmail, int quantity)
    {
        var sellerClient = _fixture.CreateClient();
        await sellerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: sellerEmail, Password: "Password123!", DisplayName: "Seller"));
        var createResponse = await sellerClient.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Gooseberries", FarmName: "Old Stone Orchard", PricePerPint: 8.5m, QuantityAvailable: quantity, Note: null));
        var listing = (await createResponse.Content.ReadFromJsonAsync<ListingResponse>())!;

        var buyerClient = _fixture.CreateClient();
        await buyerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: buyerEmail, Password: "Password123!", DisplayName: "Buyer"));

        return (listing, buyerClient);
    }

    [Fact]
    public async Task Reserving_decrements_quantity_and_returns_created()
    {
        var (listing, buyerClient) = await SeedListingAndBuyer(
            "res-seller-1@example.com", "res-buyer-1@example.com", quantity: 3);

        var response = await buyerClient.PostAsync($"/api/listings/{listing.Id}/reservations", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var updatedListing = await buyerClient.GetFromJsonAsync<ListingResponse>($"/api/listings/{listing.Id}");
        Assert.Equal(2, updatedListing!.QuantityAvailable);
    }

    [Fact]
    public async Task Reserving_a_sold_out_listing_returns_conflict()
    {
        var (listing, buyerClient) = await SeedListingAndBuyer(
            "res-seller-2@example.com", "res-buyer-2@example.com", quantity: 0);

        var response = await buyerClient.PostAsync($"/api/listings/{listing.Id}/reservations", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Reserving_your_own_listing_returns_bad_request()
    {
        var sellerClient = _fixture.CreateClient();
        await sellerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "res-seller-3@example.com", Password: "Password123!", DisplayName: "Seller"));
        var createResponse = await sellerClient.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Mulberries", FarmName: "Fontan Family Grove", PricePerPint: 9.1m, QuantityAvailable: 4, Note: null));
        var listing = (await createResponse.Content.ReadFromJsonAsync<ListingResponse>())!;

        var response = await sellerClient.PostAsync($"/api/listings/{listing.Id}/reservations", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `cd backend && dotnet test --filter ReservationsEndpointsTests`
Expected: `Passed!` — 3 passed.

- [ ] **Step 10: Commit**

```bash
cd backend
git add src/BerryExchange.Api/Reservations src/BerryExchange.Api/Infrastructure src/BerryExchange.Api/Program.cs tests/BerryExchange.Api.Tests/Reservations
git commit --no-verify -m "Add Reservations module: atomic buy-a-pint endpoint"
```

---

### Task 6: Concurrency test — two buyers, one pint

**Files:**
- Create: `backend/tests/BerryExchange.Api.Tests/Reservations/ReservationsConcurrencyTests.cs`

**Interfaces:**
- Consumes only what Tasks 2–5 already produced. No production code changes in this task — it's the gate that proves Task 5's atomic `UPDATE` actually holds under real simultaneous requests, matching `docs/architecture/reservation-flow.mmd`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Net.Http.Json;
using BerryExchange.Api.Accounts;
using BerryExchange.Api.Listings;
using Xunit;

namespace BerryExchange.Api.Tests.Reservations;

public class ReservationsConcurrencyTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;

    public ReservationsConcurrencyTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Two_simultaneous_buyers_only_one_wins_the_last_pint()
    {
        var sellerClient = _fixture.CreateClient();
        await sellerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "concurrency-seller@example.com", Password: "Password123!", DisplayName: "Seller"));
        var createResponse = await sellerClient.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Strawberries", FarmName: "Sunrow Farm", PricePerPint: 6.4m, QuantityAvailable: 1, Note: null));
        var listing = (await createResponse.Content.ReadFromJsonAsync<ListingResponse>())!;

        var buyerAClient = _fixture.CreateClient();
        await buyerAClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "concurrency-buyer-a@example.com", Password: "Password123!", DisplayName: "Buyer A"));

        var buyerBClient = _fixture.CreateClient();
        await buyerBClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "concurrency-buyer-b@example.com", Password: "Password123!", DisplayName: "Buyer B"));

        var reserveUrl = $"/api/listings/{listing.Id}/reservations";

        var taskA = buyerAClient.PostAsync(reserveUrl, null);
        var taskB = buyerBClient.PostAsync(reserveUrl, null);
        var results = await Task.WhenAll(taskA, taskB);

        var statusCodes = results.Select(r => r.StatusCode).OrderBy(c => c).ToList();
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, statusCodes);

        var finalListing = await sellerClient.GetFromJsonAsync<ListingResponse>($"/api/listings/{listing.Id}");
        Assert.Equal(0, finalListing!.QuantityAvailable);
    }
}
```

- [ ] **Step 2: Run it to verify it passes**

Run: `cd backend && dotnet test --filter ReservationsConcurrencyTests`
Expected: `Passed!` — 1 passed. If this test is flaky (occasionally both succeed), the atomic-`UPDATE` guarantee in `ReservationsService.ReserveAsync` (Task 5) is broken — do not weaken the test, fix the service.

- [ ] **Step 3: Commit**

```bash
cd backend
git add tests/BerryExchange.Api.Tests/Reservations/ReservationsConcurrencyTests.cs
git commit -m "Add concurrency test proving the atomic reservation guarantee"
```

(No `--no-verify` needed here — this commit only touches a test file, which the pre-commit hook's patterns don't match.)

---

### Task 7: End-to-end acceptance test and Phase 1 wrap-up

**Files:**
- Create: `backend/tests/BerryExchange.Api.Tests/EndToEndAcceptanceTests.cs`
- Modify: `README.md` (repo root)

**Interfaces:**
- Consumes everything from Tasks 2–6. No new production interfaces — this is the closing acceptance gate for "Phase 1: Backend API."

- [ ] **Step 1: Write the failing acceptance test**

```csharp
using System.Net;
using System.Net.Http.Json;
using BerryExchange.Api.Accounts;
using BerryExchange.Api.Listings;
using Xunit;

namespace BerryExchange.Api.Tests;

public class EndToEndAcceptanceTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;

    public EndToEndAcceptanceTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Full_flow_register_list_reserve_twice_then_sold_out()
    {
        var sellerClient = _fixture.CreateClient();
        await sellerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "e2e-seller@example.com", Password: "Password123!", DisplayName: "E2E Seller"));

        var createResponse = await sellerClient.PostAsJsonAsync("/api/listings", new CreateListingRequest(
            BerryType: "Blackberries", FarmName: "Bramble & Co", PricePerPint: 6.9m, QuantityAvailable: 2, Note: "Deep, wine-dark."));
        var listing = (await createResponse.Content.ReadFromJsonAsync<ListingResponse>())!;

        var buyerClient = _fixture.CreateClient();
        await buyerClient.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "e2e-buyer@example.com", Password: "Password123!", DisplayName: "E2E Buyer"));

        var reserveUrl = $"/api/listings/{listing.Id}/reservations";

        var first = await buyerClient.PostAsync(reserveUrl, null);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await buyerClient.PostAsync(reserveUrl, null);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var third = await buyerClient.PostAsync(reserveUrl, null);
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);

        var finalListing = await sellerClient.GetFromJsonAsync<ListingResponse>($"/api/listings/{listing.Id}");
        Assert.Equal(0, finalListing!.QuantityAvailable);
    }
}
```

- [ ] **Step 2: Run the full test suite**

Run: `cd backend && dotnet test`
Expected: `Passed!` — all tests across every task in this plan pass (16 total: 1 DbContext smoke test, 3 Accounts, 3 Listings, 3 Reservations, 1 concurrency, 1 acceptance, plus the default template test removed in Task 1 so it isn't counted).

- [ ] **Step 3: Update the repo root README**

Add a new section to `README.md` (after the existing "Running it" section, which still describes the static `index.html` prototype):

```markdown
## Backend API

The real backend lives in `backend/` — an ASP.NET Core Web API over PostgreSQL. See `docs/superpowers/specs/2026-07-20-berry-exchange-architecture-design.md` for the full design and `docs/adr/` for the individual decisions.

Run the tests (requires Docker running locally, for the Testcontainers-based Postgres):

\`\`\`
cd backend
dotnet test
\`\`\`
```

- [ ] **Step 4: Commit**

```bash
git add backend/tests/BerryExchange.Api.Tests/EndToEndAcceptanceTests.cs README.md
git commit -m "Add end-to-end acceptance test; document backend in README (Phase 1 complete)"
```

(No `--no-verify` needed — this commit touches a test file and the README, neither matched by the hook's patterns.)

---

## What's next

This plan ends with a working, fully tested backend API — runnable locally against a real Postgres, with the concurrency-critical reservation flow proven under real simultaneous load. Two follow-on plans complete the system described in the design spec:

1. **Frontend SPA** — React + TypeScript + Vite (ADR-0003), consuming this API.
2. **Deployment** — Dockerfile for the API, `docker-compose.yml` wiring the reverse proxy + API + Postgres (ADR-0005), self-hosted now with the Azure migration path documented for later.

Each should be its own plan via this same `writing-plans` skill once this one is built and reviewed.
