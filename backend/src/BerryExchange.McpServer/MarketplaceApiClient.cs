using System.Net.Http.Json;

namespace BerryExchange.McpServer;

public sealed class MarketplaceApiClient
{
    private readonly HttpClient _http;
    private readonly string? _email;
    private readonly string? _password;
    private bool _loggedIn;

    public MarketplaceApiClient(HttpClient http, string? email, string? password)
    {
        _http = http;
        _email = email;
        _password = password;
    }

    public Task<string> SearchAsync(string query, CancellationToken ct) =>
        _http.GetStringAsync($"/api/listings/search?q={Uri.EscapeDataString(query)}", ct);

    public Task<string> GetListingAsync(Guid listingId, CancellationToken ct) =>
        _http.GetStringAsync($"/api/listings/{listingId}", ct);

    public async Task<string> CreateReservationAsync(Guid listingId, CancellationToken ct)
    {
        if (_email is null || _password is null)
        {
            return "Reservations are disabled: no marketplace account is configured for this MCP server "
                 + "(set BerryMcp:Email and BerryMcp:Password).";
        }
        await EnsureLoggedInAsync(ct);
        var response = await _http.PostAsync($"/api/listings/{listingId}/reservations", content: null, ct);
        return response.IsSuccessStatusCode
            ? "Reserved one pint."
            : $"Reservation failed with status {(int)response.StatusCode}.";
    }

    private async Task EnsureLoggedInAsync(CancellationToken ct)
    {
        if (_loggedIn) return;
        var response = await _http.PostAsJsonAsync("/api/accounts/login",
            new { Email = _email, Password = _password }, ct);
        response.EnsureSuccessStatusCode();
        _loggedIn = true; // auth cookie now lives in the handler's CookieContainer
    }
}
