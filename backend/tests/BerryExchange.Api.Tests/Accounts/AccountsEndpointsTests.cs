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
        Assert.True(registerResponse.Headers.TryGetValues("Set-Cookie", out var setCookieValues), "register response did not set a session cookie");
        Assert.Contains(setCookieValues!, v => v.StartsWith("BerryExchange.Auth=", StringComparison.Ordinal));

        var me = await client.GetAsync("/api/accounts/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        // /me must return the same UserResponse shape as /register and /login - not an
        // anonymous { id, email } object - so clients can deserialize all three identically.
        var registered = await registerResponse.Content.ReadFromJsonAsync<UserResponse>();
        var meBody = await me.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(registered!.Id, meBody!.Id);
        Assert.Equal("accounts-seller@example.com", meBody.Email);
        Assert.Equal("Seller One", meBody.DisplayName);
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

    [Fact]
    public async Task Logout_returns_no_content()
    {
        // A 200 with an empty body previously made the frontend's apiRequest() throw on
        // JSON.parse, so logout silently failed client-side despite the server already
        // clearing the cookie. 204 is the correct response for a void endpoint.
        var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "accounts-logout@example.com", Password: "Password123!", DisplayName: "Logout One"));

        var logoutResponse = await client.PostAsync("/api/accounts/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
    }

    [Fact]
    public async Task Login_after_logout_sets_a_fresh_cookie_and_reauthenticates()
    {
        var client = _fixture.CreateClient();
        await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "accounts-relogin@example.com", Password: "Password123!", DisplayName: "Relogin One"));
        await client.PostAsync("/api/accounts/logout", null);

        var meAfterLogout = await client.GetAsync("/api/accounts/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfterLogout.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/accounts/login", new LoginRequest(
            Email: "accounts-relogin@example.com", Password: "Password123!"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.True(loginResponse.Headers.TryGetValues("Set-Cookie", out var loginSetCookieValues), "login response did not set a session cookie");
        Assert.Contains(loginSetCookieValues!, v => v.StartsWith("BerryExchange.Auth=", StringComparison.Ordinal));

        var meAfterLogin = await client.GetAsync("/api/accounts/me");
        Assert.Equal(HttpStatusCode.OK, meAfterLogin.StatusCode);
    }
}
