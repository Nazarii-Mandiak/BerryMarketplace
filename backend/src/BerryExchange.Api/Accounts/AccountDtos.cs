namespace BerryExchange.Api.Accounts;

public record RegisterRequest(string Email, string Password, string DisplayName);
public record LoginRequest(string Email, string Password);
public record GoogleLoginRequest(string Credential);
public record UserResponse(Guid Id, string Email, string DisplayName);
