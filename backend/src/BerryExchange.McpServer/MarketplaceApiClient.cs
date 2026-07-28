using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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
        if (response.IsSuccessStatusCode)
        {
            return "Reserved one pint.";
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // The Identity auth cookie has expired or been rejected - force the next call
            // to log in again instead of failing permanently for the rest of the process.
            _loggedIn = false;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var message = ExtractErrorMessage(body);
        return message is not null
            ? $"Reservation failed: {message}"
            : $"Reservation failed with status {(int)response.StatusCode}.";
    }

    private static string? ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString();
            }
        }
        catch (JsonException)
        {
            // Not JSON (or not the shape we expect) - fall back to the status-code message.
        }
        return null;
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
