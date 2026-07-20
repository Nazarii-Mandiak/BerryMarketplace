using BerryExchange.Api.Accounts;
using BerryExchange.Api.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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
    options.UseNpgsql(connectionString);
});

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 8;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<BerryExchangeDbContext>();

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
    scope.ServiceProvider.GetRequiredService<BerryExchangeDbContext>();
}

app.MapGet("/", () => "Hello World!");

app.Run();

public partial class Program { }
