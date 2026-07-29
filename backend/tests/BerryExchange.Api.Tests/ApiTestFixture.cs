using BerryExchange.Api.Accounts;
using BerryExchange.Api.Infrastructure;
using BerryExchange.Api.Tests.Accounts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace BerryExchange.Api.Tests;

public class ApiTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
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
        builder.ConfigureTestServices(services =>
        {
            // Registered after Program.cs's own IGoogleIdTokenValidator registration, so this
            // wins when the container resolves it - tests never depend on a real Google credential.
            services.AddSingleton<IGoogleIdTokenValidator, FakeGoogleIdTokenValidator>();
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }
}
