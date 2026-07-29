using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BerryExchange.Api.Accounts;
using Xunit;

namespace BerryExchange.Api.Tests.Accounts;

public class GoogleLoginEndpointsTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;

    public GoogleLoginEndpointsTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static string PayloadJson(string subject, string email, bool emailVerified, string? name) =>
        JsonSerializer.Serialize(new GoogleIdTokenPayload(subject, email, emailVerified, name));

    [Fact]
    public async Task New_google_sign_in_creates_a_user_and_sets_a_session_cookie()
    {
        var client = _fixture.CreateClient();
        var credential = PayloadJson("google-sub-new-1", "new-google-user@example.com", true, "New Google User");

        var response = await client.PostAsJsonAsync("/api/accounts/google", new GoogleLoginRequest(credential));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookieValues), "google sign-in response did not set a session cookie");
        Assert.Contains(setCookieValues!, v => v.StartsWith("BerryExchange.Auth=", StringComparison.Ordinal));

        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("new-google-user@example.com", body!.Email);
        Assert.Equal("New Google User", body.DisplayName);

        var me = await client.GetAsync("/api/accounts/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var meBody = await me.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("New Google User", meBody!.DisplayName);
    }

    [Fact]
    public async Task Google_sign_in_with_an_email_matching_an_existing_password_account_links_instead_of_duplicating()
    {
        var client = _fixture.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "linked-user@example.com", Password: "Password123!", DisplayName: "Password User"));
        var registered = await registerResponse.Content.ReadFromJsonAsync<UserResponse>();
        await client.PostAsync("/api/accounts/logout", null);

        var credential = PayloadJson("google-sub-link-1", "linked-user@example.com", true, "Google Display Name");
        var googleResponse = await client.PostAsJsonAsync("/api/accounts/google", new GoogleLoginRequest(credential));

        Assert.Equal(HttpStatusCode.OK, googleResponse.StatusCode);
        var googleBody = await googleResponse.Content.ReadFromJsonAsync<UserResponse>();
        // Must sign into the SAME account the password registration created, not a new one.
        Assert.Equal(registered!.Id, googleBody!.Id);
        Assert.Equal("Password User", googleBody.DisplayName);
    }

    [Fact]
    public async Task Auto_link_of_an_unconfirmed_account_removes_the_existing_password_so_a_pre_registration_squatter_loses_access()
    {
        var client = _fixture.CreateClient();
        // Simulate an attacker pre-registering the victim's email address with a password
        // only the attacker knows. Registration is open/unauthenticated and this app never
        // sends a confirmation email, so the account is created with EmailConfirmed == false.
        var registerResponse = await client.PostAsJsonAsync("/api/accounts/register", new RegisterRequest(
            Email: "prehijack-victim@example.com", Password: "AttackerPassword123!", DisplayName: "Attacker-Chosen Name"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        await client.PostAsync("/api/accounts/logout", null);

        // The real victim later signs in with Google using their real address. Google has
        // verified this address, so the auto-link path should treat that as stronger proof
        // of ownership than the squatted password and neutralize the squatter's access.
        var credential = PayloadJson("google-sub-prehijack-1", "prehijack-victim@example.com", true, "Victim Real Name");
        var googleResponse = await client.PostAsJsonAsync("/api/accounts/google", new GoogleLoginRequest(credential));
        Assert.Equal(HttpStatusCode.OK, googleResponse.StatusCode);
        await client.PostAsync("/api/accounts/logout", null);

        // The attacker's original password must no longer authenticate this account.
        var attackerLoginAttempt = await client.PostAsJsonAsync("/api/accounts/login", new LoginRequest(
            Email: "prehijack-victim@example.com", Password: "AttackerPassword123!"));
        Assert.Equal(HttpStatusCode.Unauthorized, attackerLoginAttempt.StatusCode);
    }

    [Fact]
    public async Task Repeat_google_sign_in_reuses_the_linked_account()
    {
        var client = _fixture.CreateClient();
        var credential = PayloadJson("google-sub-repeat-1", "repeat-google-user@example.com", true, "Repeat User");

        var firstResponse = await client.PostAsJsonAsync("/api/accounts/google", new GoogleLoginRequest(credential));
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<UserResponse>();
        await client.PostAsync("/api/accounts/logout", null);

        var secondResponse = await client.PostAsJsonAsync("/api/accounts/google", new GoogleLoginRequest(credential));
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<UserResponse>();

        Assert.Equal(firstBody!.Id, secondBody!.Id);
    }

    [Fact]
    public async Task Invalid_token_returns_unauthorized_and_sets_no_cookie()
    {
        var client = _fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/accounts/google", new GoogleLoginRequest("invalid-token"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Unverified_email_is_rejected_and_sets_no_cookie()
    {
        var client = _fixture.CreateClient();
        var credential = PayloadJson("google-sub-unverified-1", "unverified@example.com", false, "Unverified User");

        var response = await client.PostAsJsonAsync("/api/accounts/google", new GoogleLoginRequest(credential));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = body.GetProperty("errors");
        Assert.Equal("Google account email is not verified.", errors[0].GetString());
    }
}
