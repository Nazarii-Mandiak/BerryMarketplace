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
