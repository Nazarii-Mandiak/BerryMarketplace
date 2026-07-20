using Microsoft.AspNetCore.Identity;

namespace BerryExchange.Api.Accounts;

public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
}
