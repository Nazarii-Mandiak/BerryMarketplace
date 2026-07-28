# Google Sign-In Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user sign into Berry Marketplace with their Google account, reusing the existing cookie-session auth system, per `docs/superpowers/specs/2026-07-28-google-sign-in-design.md`.

**Architecture:** Frontend renders Google's own button (via `@react-oauth/google`); on success it hands us a signed ID token directly (no redirect). We POST that token to a new same-origin `POST /api/accounts/google` endpoint, which verifies it server-side (`Google.Apis.Auth`), finds-or-creates/links an `ApplicationUser`, and signs in through the existing `SignInManager` — producing the identical `BerryExchange.Auth` cookie a password login sets.

**Tech Stack:** ASP.NET Core Identity (minimal APIs) + EF Core/Postgres on the backend; React + TypeScript + Vite + `@tanstack/react-query` on the frontend; Docker Compose for local orchestration.

## Global Constraints

- Preserve the same-origin, cookie-session architecture from `docs/adr/0004-cookie-based-authentication.md` — no CORS is added anywhere.
- No Client Secret is used or stored anywhere — this is an ID-token verification flow, Client ID only (public value).
- No new EF Core migration — `AspNetUserLogins` already exists in the schema for external-login linking.
- Auto-link a Google sign-in to an existing password account when emails match (approved product decision — Google has already verified the email).
- Reject any Google token whose `email_verified` claim is `false`.
- Missing `VITE_GOOGLE_CLIENT_ID` / `Authentication:Google:ClientId` must degrade safely (button hidden, endpoint always 401) — never crash the app or the API at startup.
- Local dev only for this scope — no production redirect URI/domain registration.

---

### Task 1: Backend — `POST /api/accounts/google` endpoint

**Files:**
- Create: `backend/src/BerryExchange.Api/Accounts/IGoogleIdTokenValidator.cs`
- Create: `backend/src/BerryExchange.Api/Accounts/GoogleIdTokenValidator.cs`
- Modify: `backend/src/BerryExchange.Api/Accounts/AccountDtos.cs`
- Modify: `backend/src/BerryExchange.Api/Accounts/AccountsEndpoints.cs`
- Modify: `backend/src/BerryExchange.Api/Program.cs`
- Modify: `backend/src/BerryExchange.Api/BerryExchange.Api.csproj` (via `dotnet add package`)
- Create: `backend/tests/BerryExchange.Api.Tests/Accounts/FakeGoogleIdTokenValidator.cs`
- Modify: `backend/tests/BerryExchange.Api.Tests/ApiTestFixture.cs`
- Create: `backend/tests/BerryExchange.Api.Tests/Accounts/GoogleLoginEndpointsTests.cs`

**Interfaces:**
- Produces: `IGoogleIdTokenValidator.ValidateAsync(string idToken) : Task<GoogleIdTokenPayload?>` — the seam Task 3's manual verification and any future work depend on.
- Produces: `record GoogleIdTokenPayload(string Subject, string Email, bool EmailVerified, string? Name)`.
- Produces: `record GoogleLoginRequest(string Credential)` — the request body shape Task 2's frontend `loginWithGoogle` must match exactly (`{ "credential": "..." }`).
- Produces: `POST /api/accounts/google` → `200 OK` with `UserResponse(Guid Id, string Email, string DisplayName)` (same shape as `/login`/`/register`), or `401` (invalid token), or `400 { errors: [...] }` (unverified email).

- [ ] **Step 1: Add the Google.Apis.Auth package**

Run: `dotnet add backend/src/BerryExchange.Api/BerryExchange.Api.csproj package Google.Apis.Auth`
Expected: `BerryExchange.Api.csproj` gets a new `<PackageReference Include="Google.Apis.Auth" .../>` line.

- [ ] **Step 2: Create the validator interface, payload record, and safe-default null implementation**

Create `backend/src/BerryExchange.Api/Accounts/IGoogleIdTokenValidator.cs`:

```csharp
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
```

- [ ] **Step 3: Create the real validator**

Create `backend/src/BerryExchange.Api/Accounts/GoogleIdTokenValidator.cs`:

```csharp
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
```

- [ ] **Step 4: Add the request DTO**

Modify `backend/src/BerryExchange.Api/Accounts/AccountDtos.cs` — add one line so the file reads:

```csharp
namespace BerryExchange.Api.Accounts;

public record RegisterRequest(string Email, string Password, string DisplayName);
public record LoginRequest(string Email, string Password);
public record GoogleLoginRequest(string Credential);
public record UserResponse(Guid Id, string Email, string DisplayName);
```

- [ ] **Step 5: Create the fake validator test double**

Create `backend/tests/BerryExchange.Api.Tests/Accounts/FakeGoogleIdTokenValidator.cs`:

```csharp
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
```

- [ ] **Step 6: Register the fake in the test fixture**

Modify `backend/tests/BerryExchange.Api.Tests/ApiTestFixture.cs` to its full new content:

```csharp
using BerryExchange.Api.Accounts;
using BerryExchange.Api.Infrastructure;
using BerryExchange.Api.Tests.Accounts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace BerryExchange.Api.Tests;

public class ApiTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .WithDatabase("berryexchange_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BerryExchangeDbContext>();
        await db.Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BerryExchangeDb"] = _postgres.GetConnectionString()
            });
        });
        builder.ConfigureTestServices(services =>
        {
            // Registered after Program.cs's own IGoogleIdTokenValidator registration, so this
            // wins when the container resolves it - tests never depend on a real Google credential.
            services.AddSingleton<IGoogleIdTokenValidator, FakeGoogleIdTokenValidator>();
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }
}
```

- [ ] **Step 7: Write the failing tests**

Create `backend/tests/BerryExchange.Api.Tests/Accounts/GoogleLoginEndpointsTests.cs`:

```csharp
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
    }
}
```

- [ ] **Step 8: Run the tests and confirm they fail**

Run: `dotnet test backend/tests/BerryExchange.Api.Tests/BerryExchange.Api.Tests.csproj --filter "FullyQualifiedName~GoogleLoginEndpointsTests"`
Expected: FAIL — all five tests fail because `/api/accounts/google` doesn't exist yet (404, not the expected status codes).

- [ ] **Step 9: Add the endpoint**

Modify `backend/src/BerryExchange.Api/Accounts/AccountsEndpoints.cs` to its full new content:

```csharp
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
                await userManager.AddLoginAsync(user, new UserLoginInfo("Google", payload.Subject, "Google"));
            }

            await signInManager.SignInAsync(user, isPersistent: true);
            return Results.Ok(new UserResponse(user.Id, user.Email!, user.DisplayName));
        });

        group.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Ok();
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
```

- [ ] **Step 10: Wire up DI in Program.cs**

Modify `backend/src/BerryExchange.Api/Program.cs` — insert this block right after the existing `builder.Services.AddAuthorization();` line (before the rate limiter block):

```csharp
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
if (!string.IsNullOrEmpty(googleClientId))
{
    builder.Services.AddSingleton<IGoogleIdTokenValidator>(new GoogleIdTokenValidator(googleClientId));
}
else
{
    builder.Services.AddSingleton<IGoogleIdTokenValidator, NullGoogleIdTokenValidator>();
}
```

(`IGoogleIdTokenValidator`, `GoogleIdTokenValidator`, and `NullGoogleIdTokenValidator` resolve unqualified because `Program.cs` already has `using BerryExchange.Api.Accounts;` at the top.)

- [ ] **Step 11: Run the tests and confirm they pass**

Run: `dotnet test backend/tests/BerryExchange.Api.Tests/BerryExchange.Api.Tests.csproj --filter "FullyQualifiedName~GoogleLoginEndpointsTests"`
Expected: PASS — all five tests green.

Run: `dotnet test backend/tests/BerryExchange.Api.Tests/BerryExchange.Api.Tests.csproj`
Expected: PASS — full backend test suite, no regressions (in particular `AccountsEndpointsTests`).

- [ ] **Step 12: Commit**

```bash
git add backend/src/BerryExchange.Api/Accounts backend/src/BerryExchange.Api/Program.cs backend/src/BerryExchange.Api/BerryExchange.Api.csproj backend/tests/BerryExchange.Api.Tests/Accounts backend/tests/BerryExchange.Api.Tests/ApiTestFixture.cs
git commit -m "Add Google sign-in endpoint (POST /api/accounts/google)"
```

---

### Task 2: Frontend — Google button in `SignIn1`

**Files:**
- Modify: `frontend/package.json` (via `npm install`)
- Modify: `frontend/src/api/types.ts`
- Modify: `frontend/src/api/accounts.ts`
- Modify: `frontend/src/main.tsx`
- Modify: `frontend/src/components/ui/modern-stunning-sign-in.tsx`
- Create: `frontend/src/components/ui/modern-stunning-sign-in.test.tsx`

**Interfaces:**
- Consumes: `POST /api/accounts/google` from Task 1, body `{ credential: string }`, response `UserResponse`.
- Produces: `loginWithGoogle(request: GoogleLoginRequest): Promise<UserResponse>` in `api/accounts.ts`.

- [ ] **Step 1: Install the Google Identity Services React wrapper**

Run: `cd frontend && npm install @react-oauth/google`

- [ ] **Step 2: Add the request type**

Modify `frontend/src/api/types.ts` — add this interface near `LoginRequest`/`RegisterRequest`:

```ts
export interface GoogleLoginRequest {
  credential: string;
}
```

- [ ] **Step 3: Add the API function**

Modify `frontend/src/api/accounts.ts` to its full new content:

```ts
import { apiRequest } from './client';
import type { GoogleLoginRequest, LoginRequest, RegisterRequest, UserResponse } from './types';

export function login(request: LoginRequest): Promise<UserResponse> {
  return apiRequest<UserResponse>('/accounts/login', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function loginWithGoogle(request: GoogleLoginRequest): Promise<UserResponse> {
  return apiRequest<UserResponse>('/accounts/google', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function register(request: RegisterRequest): Promise<UserResponse> {
  return apiRequest<UserResponse>('/accounts/register', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function logout(): Promise<void> {
  return apiRequest<void>('/accounts/logout', { method: 'POST' });
}

export function getMe(): Promise<UserResponse> {
  return apiRequest<UserResponse>('/accounts/me');
}
```

- [ ] **Step 4: Write the failing component test**

Create `frontend/src/components/ui/modern-stunning-sign-in.test.tsx`:

```tsx
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../testUtils';
import { SignIn1 } from './modern-stunning-sign-in';
import * as accountsApi from '../../api/accounts';
import { ApiError } from '../../api/client';

vi.mock('../../api/accounts');
vi.mock('@react-oauth/google', () => ({
  GoogleLogin: ({ onSuccess }: { onSuccess: (response: { credential: string }) => void }) => (
    <button type="button" onClick={() => onSuccess({ credential: 'fake-id-token' })}>
      Continue with Google (mock)
    </button>
  ),
}));

describe('SignIn1 Google sign-in', () => {
  beforeEach(() => {
    vi.stubEnv('VITE_GOOGLE_CLIENT_ID', 'test-client-id');
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it('signs in with Google when the credential succeeds', async () => {
    vi.mocked(accountsApi.loginWithGoogle).mockResolvedValue({
      id: 'user-1', email: 'buyer@example.com', displayName: 'Buyer',
    });
    const user = userEvent.setup();

    renderWithProviders(<SignIn1 />, { route: '/login' });

    await user.click(screen.getByRole('button', { name: 'Continue with Google (mock)' }));

    await waitFor(() =>
      expect(accountsApi.loginWithGoogle).toHaveBeenCalledWith({ credential: 'fake-id-token' }),
    );
  });

  it('shows an error message when Google sign-in fails', async () => {
    vi.mocked(accountsApi.loginWithGoogle).mockRejectedValue(new ApiError(401, []));
    const user = userEvent.setup();

    renderWithProviders(<SignIn1 />, { route: '/login' });

    await user.click(screen.getByRole('button', { name: 'Continue with Google (mock)' }));

    expect(await screen.findByText('Google sign-in failed — try again.')).toBeInTheDocument();
  });

  it('does not render the Google button when no client id is configured', () => {
    vi.stubEnv('VITE_GOOGLE_CLIENT_ID', '');

    renderWithProviders(<SignIn1 />, { route: '/login' });

    expect(screen.queryByRole('button', { name: 'Continue with Google (mock)' })).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 5: Run the tests and confirm they fail**

Run: `cd frontend && npm run test -- modern-stunning-sign-in.test.tsx`
Expected: FAIL — `SignIn1` doesn't render a Google button yet, so `getByRole('button', { name: 'Continue with Google (mock)' })` throws.

- [ ] **Step 6: Wrap the app in `GoogleOAuthProvider`**

Modify `frontend/src/main.tsx` to its full new content:

```tsx
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { GoogleOAuthProvider } from '@react-oauth/google';
import { App } from './App';
import { ToastProvider } from './components/ToastProvider';
import './styles/global.css';

const queryClient = new QueryClient();
const googleClientId = import.meta.env.VITE_GOOGLE_CLIENT_ID as string | undefined;

const app = (
  <QueryClientProvider client={queryClient}>
    <BrowserRouter>
      <ToastProvider>
        <App />
      </ToastProvider>
    </BrowserRouter>
  </QueryClientProvider>
);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {googleClientId ? <GoogleOAuthProvider clientId={googleClientId}>{app}</GoogleOAuthProvider> : app}
  </StrictMode>,
);
```

- [ ] **Step 7: Add the Google button to `SignIn1`**

Modify `frontend/src/components/ui/modern-stunning-sign-in.tsx` to its full new content:

```tsx
import { type FormEvent, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { GoogleLogin, type CredentialResponse } from '@react-oauth/google';
import { login, loginWithGoogle } from '@/api/accounts';
import { ApiError } from '@/api/client';
import type { UserResponse } from '@/api/types';
import { CURRENT_USER_QUERY_KEY } from '@/features/auth/useCurrentUser';
import { BerryIcon } from '@/components/BerryIcon';

function validateEmail(email: string) {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
}

const SignIn1 = () => {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const googleClientId = import.meta.env.VITE_GOOGLE_CLIENT_ID as string | undefined;

  function handleSignedIn(user: UserResponse) {
    queryClient.setQueryData(CURRENT_USER_QUERY_KEY, user);
    const from = (location.state as { from?: { pathname: string } })?.from?.pathname ?? '/market';
    navigate(from, { replace: true });
  }

  function handleAuthError(err: unknown, invalidCredentialsMessage: string) {
    if (err instanceof ApiError && err.status === 401) {
      setError(invalidCredentialsMessage);
    } else if (err instanceof ApiError) {
      setError(err.errors[0] ?? 'Something went wrong — try again.');
    } else {
      setError('Something went wrong — try again.');
    }
  }

  const mutation = useMutation({
    mutationFn: () => login({ email, password }),
    onSuccess: handleSignedIn,
    onError: (err) => handleAuthError(err, 'Invalid email or password.'),
  });

  const googleMutation = useMutation({
    mutationFn: (credential: string) => loginWithGoogle({ credential }),
    onSuccess: handleSignedIn,
    onError: (err) => handleAuthError(err, 'Google sign-in failed — try again.'),
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!email || !password) {
      setError('Please enter both email and password.');
      return;
    }
    if (!validateEmail(email)) {
      setError('Please enter a valid email address.');
      return;
    }
    setError('');
    mutation.mutate();
  }

  function handleGoogleSuccess(credentialResponse: CredentialResponse) {
    if (!credentialResponse.credential) {
      setError('Google sign-in failed — try again.');
      return;
    }
    setError('');
    googleMutation.mutate(credentialResponse.credential);
  }

  return (
    <div className="flex flex-col items-center justify-center w-full">
      <div className="relative z-10 w-full max-w-sm rounded-3xl bg-gradient-to-br from-[var(--panel)] to-[var(--ground-2)] backdrop-blur-sm border border-[var(--line)] shadow-[var(--shadow)] p-8 flex flex-col items-center">
        <div className="flex items-center justify-center w-14 h-14 rounded-full bg-[var(--ground-2)] mb-4 shadow-[var(--shadow)] [&>svg]:w-8 [&>svg]:h-8">
          <BerryIcon berryType="raspberries" />
        </div>
        <span className="text-xs font-bold uppercase tracking-[0.08em] text-[var(--ink-muted)] mb-1 text-center">
          Berrow
        </span>
        <h2 className="text-2xl font-extrabold text-[var(--ink)] mb-6 text-center">Log in</h2>

        <form onSubmit={handleSubmit} noValidate className="flex flex-col w-full gap-4">
          <div className="w-full flex flex-col gap-3">
            <div className="flex flex-col gap-1.5">
              <label htmlFor="signin-email" className="sr-only">
                Email
              </label>
              <input
                id="signin-email"
                placeholder="Email"
                type="email"
                autoComplete="email"
                value={email}
                className="w-full px-5 py-3 rounded-xl border-2 border-[var(--line-strong)] bg-[var(--ground)] text-[var(--ink)]! placeholder-[var(--ink-muted)] text-sm focus:outline-none focus:border-[var(--accent)]"
                onChange={(e) => setEmail(e.target.value)}
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <label htmlFor="signin-password" className="sr-only">
                Password
              </label>
              <input
                id="signin-password"
                placeholder="Password"
                type="password"
                autoComplete="current-password"
                value={password}
                className="w-full px-5 py-3 rounded-xl border-2 border-[var(--line-strong)] bg-[var(--ground)] text-[var(--ink)]! placeholder-[var(--ink-muted)] text-sm focus:outline-none focus:border-[var(--accent)]"
                onChange={(e) => setPassword(e.target.value)}
              />
            </div>
            {error && <div className="text-sm text-[var(--accent)] text-left">{error}</div>}
          </div>
          <hr className="border-[var(--line)]" />
          <button
            type="submit"
            disabled={mutation.isPending}
            className="w-full bg-[var(--ink)] text-[var(--ground)]! font-bold px-5 py-3 rounded-full shadow hover:bg-[var(--accent)] hover:text-[var(--accent-ink)]! transition text-sm disabled:opacity-60 disabled:cursor-not-allowed"
          >
            {mutation.isPending ? 'Logging in…' : 'Log in'}
          </button>
        </form>

        {googleClientId && (
          <>
            <div className="flex items-center gap-3 w-full my-5">
              <hr className="flex-1 border-[var(--line)]" />
              <span className="text-xs text-[var(--ink-muted)]">or</span>
              <hr className="flex-1 border-[var(--line)]" />
            </div>
            <GoogleLogin
              onSuccess={handleGoogleSuccess}
              onError={() => setError('Google sign-in failed — try again.')}
              theme="outline"
              shape="pill"
              size="large"
              text="continue_with"
              width="320"
            />
          </>
        )}

        <div className="w-full text-center mt-5">
          <span className="text-xs text-[var(--ink-muted)]">
            Need an account?{' '}
            <Link to="/register" className="underline text-[var(--ink)] hover:text-[var(--accent)] font-semibold">
              Register
            </Link>
          </span>
        </div>
      </div>
    </div>
  );
};

export { SignIn1 };
```

- [ ] **Step 8: Run the tests and confirm they pass**

Run: `cd frontend && npm run test -- modern-stunning-sign-in.test.tsx`
Expected: PASS — all three new tests green.

- [ ] **Step 9: Run the full frontend suite and typecheck**

Run: `cd frontend && npm run test`
Expected: PASS — no regressions (`LoginPage.test.tsx` and `App.test.tsx` still pass; `VITE_GOOGLE_CLIENT_ID` is unset in that run, so `SignIn1` renders without the Google section, same as before).

Run: `cd frontend && npx tsc -b`
Expected: no output (no type errors).

- [ ] **Step 10: Commit**

```bash
git add frontend/package.json frontend/package-lock.json frontend/src/api/types.ts frontend/src/api/accounts.ts frontend/src/main.tsx frontend/src/components/ui/modern-stunning-sign-in.tsx frontend/src/components/ui/modern-stunning-sign-in.test.tsx
git commit -m "Add Google sign-in button to the login page"
```

---

### Task 3: Docker build-arg plumbing + safe-default verification

**Files:**
- Modify: `frontend/Dockerfile`
- Modify: `docker-compose.yml`
- Modify: `README.md`

**Interfaces:**
- Consumes: `GOOGLE_CLIENT_ID` shell env var (source of truth for both the frontend build arg and the backend's `Authentication__Google__ClientId`).

- [ ] **Step 1: Add the build arg to the frontend Dockerfile**

Modify `frontend/Dockerfile` to its full new content:

```dockerfile
FROM node:22-alpine AS build
WORKDIR /app
ARG VITE_GOOGLE_CLIENT_ID
ENV VITE_GOOGLE_CLIENT_ID=$VITE_GOOGLE_CLIENT_ID
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
```

(Vite bakes `import.meta.env.VITE_*` into the JS bundle at build time — a plain `environment:` entry on the compose service would have no effect on an already-built static bundle, so this has to be a build `ARG`.)

- [ ] **Step 2: Pass it through docker-compose.yml**

Modify `docker-compose.yml` — change the `frontend` service's `build:` from the one-line `build: frontend` to:

```yaml
  frontend:
    build:
      context: frontend
      args:
        VITE_GOOGLE_CLIENT_ID: ${GOOGLE_CLIENT_ID:-}
    ports:
      - "5173:80"
    depends_on:
      - api
```

And add one line to the `api` service's `environment:` block (after `Anthropic__ApiKey`):

```yaml
      Anthropic__ApiKey: ${ANTHROPIC_API_KEY:-}
      Authentication__Google__ClientId: ${GOOGLE_CLIENT_ID:-}
```

- [ ] **Step 3: Document it in the README**

Modify `README.md` — in the "Quickstart (Docker)" section, change:

```
    export ANTHROPIC_API_KEY=sk-ant-...   # optional; AI features degrade gracefully without it
    docker compose up --build
```

to:

```
    export ANTHROPIC_API_KEY=sk-ant-...   # optional; AI features degrade gracefully without it
    export GOOGLE_CLIENT_ID=123...apps.googleusercontent.com   # optional; Google sign-in hides itself without it
    docker compose up --build
```

- [ ] **Step 4: Rebuild and verify the safe default (no `GOOGLE_CLIENT_ID` set)**

Run: `docker compose build frontend api && docker compose up -d frontend api`

Then navigate a browser to `http://localhost:5173/login` and confirm:
- The page loads normally with no console errors.
- No "or" divider / Google button appears (client id is unset, so both the provider wrap and the button render are skipped).
- Email/password login still works exactly as before.

- [ ] **Step 5: Commit**

```bash
git add frontend/Dockerfile docker-compose.yml README.md
git commit -m "Wire GOOGLE_CLIENT_ID through Docker Compose for Google sign-in"
```

---

### Task 4: End-to-end verification with a real Google Client ID

**Blocked until the user supplies a real Google OAuth Client ID** (Web application type, Authorized JavaScript origin `http://localhost:5173`, per the design spec).

**Files:** none — this is a verification-only task, no code changes.

- [ ] **Step 1: Set the real Client ID and rebuild**

Run:
```bash
export GOOGLE_CLIENT_ID=<the real client id>
docker compose build frontend api
docker compose up -d frontend api
```

- [ ] **Step 2: Verify the button renders and the flow completes**

Navigate to `http://localhost:5173/login` and confirm:
- The "or" divider and Google's rendered button now appear below the password form.
- Clicking it opens Google's real account chooser/consent UI (confirms the frontend is correctly configured with a valid Client ID recognized by Google).
- Completing it with a real Google account redirects to `/market` and the header shows the logged-in state (confirms the full round trip: frontend → `/api/accounts/google` → real `GoogleIdTokenValidator` → session cookie).

- [ ] **Step 3: Report results**

Confirm to the user that the flow works end-to-end, or report exactly what failed (browser console error, network response body, etc.) if it didn't.
