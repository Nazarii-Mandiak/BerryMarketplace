using Microsoft.AspNetCore.Identity;

namespace BerryExchange.Api.Accounts;

public static class AccountsEndpoints
{
    public static void MapAccountsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/accounts");

        group.MapPost("/register", async (
            RegisterRequest request,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager) =>
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = request.Email,
                Email = request.Email,
                DisplayName = request.DisplayName
            };
            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });
            }
            await signInManager.SignInAsync(user, isPersistent: true);
            return Results.Ok(new UserResponse(user.Id, user.Email!, user.DisplayName));
        });

        group.MapPost("/login", async (
            LoginRequest request,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user is null)
            {
                return Results.Unauthorized();
            }
            var result = await signInManager.PasswordSignInAsync(user, request.Password, isPersistent: true, lockoutOnFailure: false);
            return result.Succeeded
                ? Results.Ok(new UserResponse(user.Id, user.Email!, user.DisplayName))
                : Results.Unauthorized();
        });

        group.MapPost("/google", async (
            GoogleLoginRequest request,
            IGoogleIdTokenValidator googleValidator,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager) =>
        {
            if (string.IsNullOrWhiteSpace(request.Credential))
            {
                return Results.Unauthorized();
            }

            var payload = await googleValidator.ValidateAsync(request.Credential);
            if (payload is null)
            {
                return Results.Unauthorized();
            }
            if (!payload.EmailVerified)
            {
                return Results.BadRequest(new { errors = new[] { "Google account email is not verified." } });
            }

            var user = await userManager.FindByLoginAsync("Google", payload.Subject);
            if (user is null)
            {
                // Auto-link by email: Google has already verified this address, so treat it
                // as proof of ownership of any existing password account with the same email.
                user = await userManager.FindByEmailAsync(payload.Email);
                if (user is null)
                {
                    user = new ApplicationUser
                    {
                        Id = Guid.NewGuid(),
                        UserName = payload.Email,
                        Email = payload.Email,
                        EmailConfirmed = true,
                        DisplayName = payload.Name ?? payload.Email
                    };
                    var createResult = await userManager.CreateAsync(user);
                    if (!createResult.Succeeded)
                    {
                        return Results.BadRequest(new { errors = createResult.Errors.Select(e => e.Description) });
                    }
                }
                var addLoginResult = await userManager.AddLoginAsync(user, new UserLoginInfo("Google", payload.Subject, "Google"));
                if (!addLoginResult.Succeeded)
                {
                    return Results.BadRequest(new { errors = addLoginResult.Errors.Select(e => e.Description) });
                }

                if (!user.EmailConfirmed)
                {
                    // This matched account's email was never confirmed: registration is open and
                    // unauthenticated, and this app never sends a confirmation email, so an
                    // unconfirmed account could belong to an attacker who pre-registered the
                    // victim's address with a password only they know (account pre-hijacking).
                    // Google has now proven the signed-in user owns this exact address, so:
                    // strip any password on the account (kills the attacker's password-login
                    // access), mark the email confirmed (Google's verification stands in for
                    // the confirmation email we never send), and rotate the security stamp
                    // (invalidates any session/cookie the attacker already holds for it).
                    var removePasswordResult = await userManager.RemovePasswordAsync(user);
                    if (!removePasswordResult.Succeeded)
                    {
                        return Results.BadRequest(new { errors = removePasswordResult.Errors.Select(e => e.Description) });
                    }

                    user.EmailConfirmed = true;
                    var updateResult = await userManager.UpdateAsync(user);
                    if (!updateResult.Succeeded)
                    {
                        return Results.BadRequest(new { errors = updateResult.Errors.Select(e => e.Description) });
                    }

                    var stampResult = await userManager.UpdateSecurityStampAsync(user);
                    if (!stampResult.Succeeded)
                    {
                        return Results.BadRequest(new { errors = stampResult.Errors.Select(e => e.Description) });
                    }
                }
            }

            await signInManager.SignInAsync(user, isPersistent: true);
            return Results.Ok(new UserResponse(user.Id, user.Email!, user.DisplayName));
        });

        group.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.NoContent();
        });

        group.MapGet("/me", async (HttpContext http, UserManager<ApplicationUser> userManager) =>
        {
            if (http.User.Identity?.IsAuthenticated != true)
            {
                return Results.Unauthorized();
            }
            var id = Guid.Parse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var email = http.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
            // DisplayName is a custom property on ApplicationUser, not a standard claim carried by
            // the Identity cookie principal, so it can't be read from http.User claims like id/email
            // are. Look the user up via UserManager to get it, matching /register and /login's shape.
            var user = await userManager.FindByIdAsync(id.ToString());
            var displayName = user?.DisplayName ?? "";
            return Results.Ok(new UserResponse(id, email, displayName));
        }).RequireAuthorization();
    }
}
