namespace BerryExchange.Api.Accounts;

public record GoogleIdTokenPayload(string Subject, string Email, bool EmailVerified, string? Name);

public interface IGoogleIdTokenValidator
{
    Task<GoogleIdTokenPayload?> ValidateAsync(string idToken);
}

// Registered when Authentication:Google:ClientId isn't configured (e.g. local dev before
// the Google Cloud OAuth client has been created). Always rejects rather than throwing at
// startup or at request time, so the rest of the app keeps working without it.
public class NullGoogleIdTokenValidator : IGoogleIdTokenValidator
{
    public Task<GoogleIdTokenPayload?> ValidateAsync(string idToken) => Task.FromResult<GoogleIdTokenPayload?>(null);
}
