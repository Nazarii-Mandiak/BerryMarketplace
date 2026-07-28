using System.Text.Json;
using BerryExchange.Api.Accounts;

namespace BerryExchange.Api.Tests.Accounts;

// Test double for IGoogleIdTokenValidator. Real Google ID tokens are opaque signed JWTs
// that can't be constructed in a test without a live Google key exchange, so instead of
// mimicking JWT structure, this fake treats the "credential" string as a JSON-encoded
// GoogleIdTokenPayload directly - tests build the exact payload they want to exercise.
// The literal string "invalid-token" simulates a token that fails Google's signature check.
public class FakeGoogleIdTokenValidator : IGoogleIdTokenValidator
{
    public Task<GoogleIdTokenPayload?> ValidateAsync(string idToken)
    {
        if (idToken == "invalid-token")
        {
            return Task.FromResult<GoogleIdTokenPayload?>(null);
        }
        var payload = JsonSerializer.Deserialize<GoogleIdTokenPayload>(idToken);
        return Task.FromResult<GoogleIdTokenPayload?>(payload);
    }
}
