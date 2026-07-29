using Google.Apis.Auth;

namespace BerryExchange.Api.Accounts;

public class GoogleIdTokenValidator : IGoogleIdTokenValidator
{
    private readonly string _clientId;

    public GoogleIdTokenValidator(string clientId)
    {
        _clientId = clientId;
    }

    public async Task<GoogleIdTokenPayload?> ValidateAsync(string idToken)
    {
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _clientId }
            });
            return new GoogleIdTokenPayload(payload.Subject, payload.Email, payload.EmailVerified, payload.Name);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}
