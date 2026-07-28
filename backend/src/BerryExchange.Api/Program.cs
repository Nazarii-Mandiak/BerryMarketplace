using System.Security.Claims;
using System.Threading.RateLimiting;
using BerryExchange.Api.Accounts;
using BerryExchange.Api.Ai;
using BerryExchange.Api.Chat;
using BerryExchange.Api.Infrastructure;
using BerryExchange.Api.Infrastructure.Messaging;
using BerryExchange.Api.Listings;
using BerryExchange.Api.Reservations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
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

// Any registered account (registration is open) can otherwise drive unbounded Claude API
// spend by hammering the two LLM-backed endpoint groups. A modest fixed-window limiter,
// partitioned per authenticated user (falling back to client IP for the anonymous
// /api/ai/status check), is enough for a showcase - not tuned precision, just a backstop.
builder.Services.AddRateLimiter(limiterOptions =>
{
    limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiterOptions.AddPolicy("llm", httpContext =>
    {
        var partitionKey = httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous"
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

builder.Services.AddScoped<ListingsService>();
builder.Services.AddScoped<ReservationsService>();
builder.Services.AddScoped<BerryExchange.Api.Chat.ChatService>();
builder.Services.AddSingleton<BerryExchange.AiCore.ITextEmbedder, BerryExchange.AiCore.LocalTextEmbedder>();

var anthropicApiKey = builder.Configuration["Anthropic:ApiKey"]
    ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (!string.IsNullOrEmpty(anthropicApiKey))
{
    builder.Services.AddSingleton<BerryExchange.AiCore.IGenerativeAi>(
        new BerryExchange.AiCore.AnthropicGenerativeAi(anthropicApiKey));
}
else
{
    builder.Services.AddSingleton<BerryExchange.AiCore.IGenerativeAi,
        BerryExchange.AiCore.DisabledGenerativeAi>();
}

builder.Services.AddScoped<BerryExchange.Api.Chat.Agent.IChatToolExecutor,
    BerryExchange.Api.Chat.Agent.ChatToolExecutor>();
builder.Services.AddScoped<BerryExchange.Api.Chat.Agent.ChatAgent>(sp => new(
    sp.GetRequiredService<BerryExchange.Api.Chat.Agent.IChatAgentModel>(),
    sp.GetRequiredService<BerryExchange.Api.Chat.Agent.IChatToolExecutor>()));
if (!string.IsNullOrEmpty(anthropicApiKey))
{
    builder.Services.AddSingleton<BerryExchange.Api.Chat.Agent.IChatAgentModel>(
        new BerryExchange.Api.Chat.Agent.AnthropicChatAgentModel(anthropicApiKey));
}
else
{
    // Endpoint 503s before resolving the agent when AI is disabled, but DI still
    // needs a registration for test overrides to Replace.
    builder.Services.AddSingleton<BerryExchange.Api.Chat.Agent.IChatAgentModel>(
        new BerryExchange.Api.Chat.Agent.ThrowingChatAgentModel());
}

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
app.UseRateLimiter();

app.MapAccountsEndpoints();
app.MapListingsEndpoints();
app.MapReservationsEndpoints();
app.MapInternalEnrichmentEndpoints();
app.MapAiEndpoints();
app.MapChatEndpoints();

app.Run();

public partial class Program { }
