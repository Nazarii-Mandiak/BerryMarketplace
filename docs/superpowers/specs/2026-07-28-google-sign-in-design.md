# Google Sign-In — Design

**Date:** 2026-07-28
**Status:** Approved for implementation

## Context

The login page (`frontend/src/components/ui/modern-stunning-sign-in.tsx`, rendered by
`LoginPage.tsx`) was recently integrated from a third-party UI template. The template
included a "Continue with Google" button that did nothing; it was dropped during
integration since shipping a dead button is worse than omitting it. This spec covers
wiring up real Google sign-in to replace it.

The existing auth system (see `docs/adr/0004-cookie-based-authentication.md`) uses
ASP.NET Core Identity with same-site session cookies, deliberately avoiding JWTs for
the app's own session and avoiding CORS by keeping the SPA and API same-origin behind
nginx (prod/docker) or the Vite dev proxy (local). Any OAuth addition must preserve
that architecture rather than bend it.

Relevant existing code:
- `backend/src/BerryExchange.Api/Accounts/AccountsEndpoints.cs` — minimal-API endpoints
  (`/api/accounts/register`, `/login`, `/logout`, `/me`), using
  `UserManager<ApplicationUser>` / `SignInManager<ApplicationUser>`.
- `backend/src/BerryExchange.Api/Accounts/ApplicationUser.cs` — `IdentityUser<Guid>` +
  `DisplayName`.
- `BerryExchangeDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`
  — standard Identity tables already exist, including `AspNetUserLogins` /
  `AspNetUserTokens` (created in the very first migration), so external-login linking
  needs no new migration.
- `frontend/src/api/accounts.ts`, `frontend/src/api/client.ts` (fetch wrapper, always
  same-origin `/api/...`, `credentials: 'include'`).
- No CORS setup exists anywhere in the backend, by design.

## Chosen approach

Google Identity Services (GIS) **ID-token flow**, verified server-side. Rejected
alternatives: classic redirect-based `AddGoogle()` challenge/callback (requires a
Client Secret, exact redirect-URI registration per environment, and a full-page bounce
through accounts.google.com — heavier, and cuts against the same-origin/no-redirect
architecture already in place) and outsourcing to Firebase/Auth0 (would replace the
existing Identity+cookie system entirely, contradicting ADR-0004).

With the ID-token flow: the frontend gets a signed ID token directly from Google's SDK
(no redirect), POSTs it to a new same-origin endpoint, the backend verifies it and
signs in through the *same* `SignInManager` used today — so the resulting session
cookie is indistinguishable from a password login. Only a public Client ID is needed
on both ends; no Client Secret, no redirect URI.

## Backend design

**New endpoint** — `POST /api/accounts/google` in `AccountsEndpoints.cs` (same file/
pattern as the existing endpoints):

Request: `{ "credential": "<google id token jwt>" }`
Response: `UserResponse` (same shape as `/login` and `/register`), `200 OK`, with the
`BerryExchange.Auth` cookie set via `SignInManager`.

**Token verification seam** — `IGoogleIdTokenValidator`:
```csharp
public interface IGoogleIdTokenValidator
{
    Task<GoogleIdTokenPayload?> ValidateAsync(string idToken);
}

public record GoogleIdTokenPayload(string Subject, string Email, bool EmailVerified, string? Name);
```
Production implementation wraps `Google.Apis.Auth`'s
`GoogleJsonWebSignature.ValidateAsync(idToken, new ValidationSettings { Audience = new[] { clientId } })`,
catching `InvalidJwtException` and returning `null` on any validation failure. The
Client ID comes from config key `Authentication:Google:ClientId`.

This interface is registered in DI and is the seam integration tests substitute a fake
for — consistent with this codebase's existing pattern of exercising the real stack end
to end (see `ApiTestFixture`) rather than mocking `UserManager`/`SignInManager`.

**Endpoint logic:**
1. `payload = await validator.ValidateAsync(credential)`. Null → `401 Unauthorized`.
2. `payload.EmailVerified == false` → `400 BadRequest` with
   `{ errors: ["Google account email is not verified."] }` (matches the existing
   `BadRequest` shape used by `/register`'s validation errors).
3. `existingLogin = await userManager.FindByLoginAsync("Google", payload.Subject)`.
   - Found → `user = existingLogin`.
   - Not found → `userByEmail = await userManager.FindByEmailAsync(payload.Email)`.
     - Found → link: `await userManager.AddLoginAsync(userByEmail, new UserLoginInfo("Google", payload.Subject, "Google"))`; `user = userByEmail`. (Auto-link by email — approved product decision: Google has already verified the email, so this is treated as proof of ownership.)
     - Not found → create: `new ApplicationUser { Id = Guid.NewGuid(), UserName = payload.Email, Email = payload.Email, DisplayName = payload.Name ?? payload.Email }`, `await userManager.CreateAsync(user)` (no password — `PasswordHash` stays null, matching Identity's normal behavior for external-login-only accounts), then `AddLoginAsync` as above.
4. `await signInManager.SignInAsync(user, isPersistent: true)`.
5. Return `Results.Ok(new UserResponse(user.Id, user.Email!, user.DisplayName))`.

**Config additions:**
- `appsettings.Development.json`: `Authentication:Google:ClientId` (placeholder until
  the real value is supplied).
- `docker-compose.yml`, `api` service: `Authentication__Google__ClientId: ${GOOGLE_CLIENT_ID:-}`
  (double-underscore env-var naming, matching the existing `Anthropic__ApiKey` /
  `Internal__ApiKey` pattern).

**New NuGet package:** `Google.Apis.Auth` on `BerryExchange.Api`.

**No new EF migration** — `AspNetUserLogins` already exists; `ApplicationUser` needs no
new columns.

## Frontend design

- New dependency: `@react-oauth/google`.
- `main.tsx` wraps `<App />` in `<GoogleOAuthProvider clientId={import.meta.env.VITE_GOOGLE_CLIENT_ID}>`.
- `SignIn1` (`components/ui/modern-stunning-sign-in.tsx`) re-adds the `<hr>` divider
  (removed earlier since there was only one auth method) and renders Google's
  `<GoogleLogin>` button below it — **only if `VITE_GOOGLE_CLIENT_ID` is set**, so the
  app doesn't break in any environment where the credential hasn't been configured yet
  (e.g. before the Google Cloud project exists). Button is themed as close to the
  card's pill aesthetic as GIS's configuration options allow (`shape="pill"`), with
  light/dark `theme` following the same `data-theme`/`prefers-color-scheme` signal the
  rest of the app uses.
- `onSuccess={(credentialResponse) => ...}` calls a new `loginWithGoogle(credential)` in
  `api/accounts.ts` (`POST /accounts/google`) via a new mutation. This mutation shares
  the exact same `onSuccess` handler as the existing password mutation (both produce an
  identical `UserResponse`): cache the user, redirect to `from ?? '/market'`.
- `onError` shows an inline error using the same error-text styling as the password
  form's validation errors, with copy specific to this path ("Google sign-in failed —
  try again."), not the password-specific "Invalid email or password."

## Docker build-time env var (important gotcha)

`frontend/Dockerfile` does a **build-time** `npm run build` with no runtime env
injection — `import.meta.env.VITE_*` values are baked into the JS bundle at build time.
A plain `environment:` entry on the `frontend` service in `docker-compose.yml` would
have **no effect** (it only affects the running nginx container, not the already-built
static bundle). This needs:

- `frontend/Dockerfile`: add `ARG VITE_GOOGLE_CLIENT_ID` and
  `ENV VITE_GOOGLE_CLIENT_ID=$VITE_GOOGLE_CLIENT_ID` before the `RUN npm run build`
  step.
- `docker-compose.yml`, `frontend` service: change `build: frontend` to
  `build: { context: frontend, args: { VITE_GOOGLE_CLIENT_ID: "${GOOGLE_CLIENT_ID:-}" } }`.
- Both `GOOGLE_CLIENT_ID` (used for both the frontend build arg and the backend config
  env var above) come from a `.env` file at the repo root, matching how
  `ANTHROPIC_API_KEY` is already sourced.

## Error handling summary

| Condition | Backend response | Frontend behavior |
|---|---|---|
| Invalid/expired/tampered token | `401` | "Google sign-in failed — try again." |
| Token valid, email not verified | `400` `{errors:[...]}` | Same inline error area, backend message surfaced |
| Google SDK popup closed/failed client-side | n/a (no request sent) | Same inline error, no network call |
| Any other unexpected error | `500` (unhandled, consistent with rest of app — no global exception middleware exists yet) | Generic "Something went wrong — try again." (existing fallback already used by the password mutation) |

## Testing plan

**Backend** — new `backend/tests/BerryExchange.Api.Tests/Accounts/GoogleLoginEndpointsTests.cs`,
using the existing `ApiTestFixture` (`IClassFixture`, real Postgres via Testcontainers),
with the fake `IGoogleIdTokenValidator` substituted via the test host's service
overrides. Cases:
1. New Google sign-in (no existing account) → creates user, sets
   `BerryExchange.Auth` cookie, `/me` reflects the new user with `DisplayName` from the
   payload.
2. Google sign-in with an email matching an existing password account → auto-links
   (verify via a subsequent `FindByLoginAsync`), signs into the *same* user id as the
   pre-existing account — not a duplicate.
3. Repeat Google sign-in (same subject, already linked) → signs into the existing
   linked account; no duplicate user or duplicate `AspNetUserLogins` row.
4. Invalid/unverifiable token → `401`, no cookie set.
5. Valid token but unverified email → `400`, no user created.

**Frontend** — extend the `LoginPage`/`SignIn1` test coverage by mocking
`@react-oauth/google`'s exported `GoogleLogin` component (`vi.mock('@react-oauth/google', ...)`)
to synchronously invoke `onSuccess`, following the same mocking pattern already used
for `../../api/accounts` — verify the mutation fires and navigation/cache-set behaves
the same as a password login success.

## Out of scope

- Letting a Google-only account later set a password (no requirement given; YAGNI).
- Production redirect URI / domain registration (explicitly deferred — local dev only
  for now, per product decision).
- Any change to the existing password login/register flow.
