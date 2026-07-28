using System.Net;
using BerryExchange.McpServer;

namespace BerryExchange.McpServer.Tests;

public class MarketplaceApiClientTests
{
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var body = request.RequestUri!.AbsolutePath switch
            {
                "/api/accounts/login" => """{"id":"00000000-0000-0000-0000-000000000001"}""",
                var p when p.StartsWith("/api/listings/search") => """{"mode":"semantic","results":[]}""",
                var p when p.EndsWith("/reservations") => "",
                _ => "{}",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task Search_passes_query_through_and_reservation_logs_in_first()
    {
        var handler = new ScriptedHandler();
        var client = new MarketplaceApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://api.test") }, "mcp@test.dev", "Password1!");

        var search = await client.SearchAsync("sweet strawberries", CancellationToken.None);
        Assert.Contains("semantic", search);
        Assert.Contains("q=sweet%20strawberries", handler.Requests[0].RequestUri!.Query);

        await client.CreateReservationAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath == "/api/accounts/login");

        // Second reservation must not log in again.
        var loginCount = handler.Requests.Count(r => r.RequestUri!.AbsolutePath == "/api/accounts/login");
        await client.CreateReservationAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(loginCount, handler.Requests.Count(r => r.RequestUri!.AbsolutePath == "/api/accounts/login"));
    }

    [Fact]
    public async Task Reservation_without_configured_account_returns_disabled_message()
    {
        var client = new MarketplaceApiClient(
            new HttpClient(new ScriptedHandler()) { BaseAddress = new Uri("http://api.test") }, null, null);
        var result = await client.CreateReservationAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Contains("disabled", result, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ExpiringSessionHandler : HttpMessageHandler
    {
        public int LoginCount;
        public bool SessionExpired;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/accounts/login")
            {
                LoginCount++;
                SessionExpired = false;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("""{"id":"00000000-0000-0000-0000-000000000001"}""") });
            }
            if (path.EndsWith("/reservations"))
            {
                return Task.FromResult(SessionExpired
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("") }
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    [Fact]
    public async Task A_401_on_reservation_forces_re_login_on_the_next_call()
    {
        var handler = new ExpiringSessionHandler();
        var client = new MarketplaceApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://api.test") }, "mcp@test.dev", "Password1!");

        await client.CreateReservationAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(1, handler.LoginCount);

        // Simulate the session having expired server-side.
        handler.SessionExpired = true;
        var result = await client.CreateReservationAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Contains("401", result);

        // The next reservation attempt must re-authenticate rather than staying "logged in"
        // forever, since the cookie is no longer valid.
        await client.CreateReservationAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(2, handler.LoginCount);
    }

    private sealed class DetailedErrorHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/accounts/login")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("""{"id":"00000000-0000-0000-0000-000000000001"}""") });
            }
            if (path.EndsWith("/reservations"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                { Content = new StringContent("""{"error":"You cannot reserve your own listing."}""") });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    [Fact]
    public async Task Reservation_failure_passes_through_the_apis_actual_error_message()
    {
        var client = new MarketplaceApiClient(
            new HttpClient(new DetailedErrorHandler()) { BaseAddress = new Uri("http://api.test") }, "mcp@test.dev", "Password1!");

        var result = await client.CreateReservationAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Contains("You cannot reserve your own listing.", result);
    }
}
