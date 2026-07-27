using BerryExchange.Api.Accounts;
using BerryExchange.Api.Ai;
using BerryExchange.Api.Infrastructure;
using BerryExchange.Api.Infrastructure.Messaging;
using BerryExchange.Api.Listings;
using BerryExchange.Api.Reservations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// The connection string is read inside the AddDbContext delegate (rather than being
// captured into a local variable beforehand) so it is resolved lazily, at DbContext
// construction time. This matters for WebApplicationFactory-based tests: the fixture's
// ConfigureWebHost/ConfigureAppConfiguration override runs after this top-level Program.cs
// code executes but before any DbContext is actually constructed, so an eager read here
// would capture the static appsettings.Development.json value instead of the test's
// Testcontainers connection string.
builder.Services.AddDbContext<BerryExchangeDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("BerryExchangeDb")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:BerryExchangeDb");
    options.UseNpgsql(connectionString, npgsql => npgsql.UseVector());
});

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

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

builder.Services.AddScoped<ListingsService>();
builder.Services.AddScoped<ReservationsService>();

if (!string.IsNullOrEmpty(builder.Configuration["RabbitMq:Host"]))
{
    builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
}
else
{
    builder.Services.AddSingleton<IEventPublisher, NullEventPublisher>();
}

var app = builder.Build();

// Force the lazy AddDbContext options delegate to run now, at startup, instead of on first
// request: this restores fail-fast behavior for a missing ConnectionStrings:BerryExchangeDb
// (throwing InvalidOperationException here crashes the process before app.Run() accepts any
// traffic) while still executing after builder.Build(), so under WebApplicationFactory this
// correctly observes the test fixture's ConfigureAppConfiguration override rather than the
// static appsettings value. This only constructs the DbContext object - no DB connection or
// migration happens here. Do not delete as dead code.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BerryExchangeDbContext>();
    // In containers (compose/k8s) the schema is applied at startup instead of by a
    // developer running `dotnet ef database update`. Off by default so tests and
    // local dev keep their existing behavior.
    if (app.Configuration.GetValue<bool>("Database:AutoMigrate"))
    {
        db.Database.Migrate();
    }
}

app.UseAuthentication();
app.UseAuthorization();

app.MapAccountsEndpoints();
app.MapListingsEndpoints();
app.MapReservationsEndpoints();
app.MapInternalEnrichmentEndpoints();

app.Run();

public partial class Program { }
