using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Auth;

/// <summary>
/// Backend-for-frontend (BFF) authentication: the backend runs the OIDC authorization-code flow
/// against the IdP and issues an HttpOnly session cookie; the SPA never sees tokens. Configured for
/// the dev topology where the browser reaches these endpoints through the Vite proxy at the public
/// origin, so the callback (and cookie) land on the SPA origin regardless of the backend's port.
/// </summary>
public static class BffAuthentication
{
    public static void AddBffAuthentication(this WebApplicationBuilder builder)
    {
        // Issuer URL + client credentials of the OIDC provider (Authentik). Required for login;
        // supplied via env (local.secrets.env). When unset the app still runs — only login fails —
        // so non-auth features stay usable in local dev without an IdP configured.
        var authority = Environment.GetEnvironmentVariable("OIDC_AUTHORITY") ?? "";
        var clientId = Environment.GetEnvironmentVariable("OIDC_CLIENT_ID") ?? "";
        var clientSecret = Environment.GetEnvironmentVariable("OIDC_CLIENT_SECRET") ?? "";
        var publicOrigin = (Environment.GetEnvironmentVariable("BFF_PUBLIC_ORIGIN")
                            ?? "http://localhost:5173").TrimEnd('/');

        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "mc.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;
                // This is an API: don't 302 to a login page, answer with status codes the SPA reads.
                options.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = authority;
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.ResponseType = "code";
                options.UsePkce = true;
                // Use a GET (query) callback instead of the default form_post. The IdP is on a
                // different site from the SPA, and SameSite=Lax cookies are sent on cross-site
                // top-level GET navigations but NOT on cross-site POSTs — form_post would drop the
                // correlation/nonce cookies and fail login. Code stays protected by PKCE.
                options.ResponseMode = "query";
                options.RequireHttpsMetadata = false; // dev: Keycloak runs over http
                options.SaveTokens = true;             // keep id_token for sign-out hint
                options.GetClaimsFromUserInfoEndpoint = true;
                options.MapInboundClaims = false;      // keep raw claim types ("sub", "preferred_username", ...)
                options.CallbackPath = "/signin-oidc";
                options.SignedOutCallbackPath = "/signout-callback-oidc";

                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");

                options.TokenValidationParameters.NameClaimType = "preferred_username";

                // Dev over HTTP: the callback is a same-site top-level GET (localhost:8080 ->
                // localhost:5173 — port doesn't affect "site"), so Lax cookies are sent and we
                // sidestep the SameSite=None+Secure requirement that breaks on plain http.
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                // Challenge happens at /auth/login but the callback is /signin-oidc; scope these to
                // "/" so the cookies set during the challenge are sent back on the callback.
                options.NonceCookie.Path = "/";
                options.CorrelationCookie.Path = "/";

                // Force the browser-facing redirect URI so the callback returns through the Vite
                // proxy onto the SPA origin (where the auth cookie must live), independent of the
                // backend's dynamic internal host/port.
                options.Events.OnRedirectToIdentityProvider = ctx =>
                {
                    ctx.ProtocolMessage.RedirectUri = publicOrigin + options.CallbackPath;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToIdentityProviderForSignOut = ctx =>
                {
                    ctx.ProtocolMessage.PostLogoutRedirectUri = publicOrigin + "/";
                    return Task.CompletedTask;
                };

                // Mirror the IdP identity into our user store on every login.
                options.Events.OnTokenValidated = async ctx =>
                {
                    var principal = ctx.Principal;
                    var subject = principal?.FindFirst("sub")?.Value;
                    if (subject == null) return;

                    var now = DateTimeOffset.UtcNow;
                    var users = ctx.HttpContext.RequestServices.GetRequiredService<IUserRepo>();
                    await users.UpsertOnLogin(new AppUser(
                        Subject: subject,
                        Username: principal!.FindFirst("preferred_username")?.Value,
                        Email: principal.FindFirst("email")?.Value,
                        DisplayName: principal.FindFirst("name")?.Value,
                        FirstSeenAt: now,
                        LastLoginAt: now));
                };
            });

        builder.Services.AddAuthorization();
    }

    /// <summary>The OIDC subject ("sub") of the current user, or null if unauthenticated.</summary>
    public static string? GetSubject(this ClaimsPrincipal principal) =>
        principal.FindFirst("sub")?.Value;
}
