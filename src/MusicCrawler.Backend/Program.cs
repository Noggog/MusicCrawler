using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using MusicCrawler.Backend;
using MusicCrawler.Backend.Services.Auth;
using MusicCrawler.Backend.Services.Background;
using MusicCrawler.Backend.Services.Singletons;
using MusicCrawler.Interfaces;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Write logs to a rolling file under the backend's content root (logs/backend-<date>.log,
// gitignored) so failures are inspectable without watching the live console. writeToProviders
// keeps the existing OpenTelemetry logging from ServiceDefaults intact.
builder.Services.AddSerilog((_, lc) => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(builder.Environment.ContentRootPath, "logs", "backend-.log"),
        rollingInterval: RollingInterval.Day,
        shared: true),
    writeToProviders: true);

// Use Redis when a "cache" connection string is provided (the Aspire AppHost injects one);
// otherwise fall back to an in-memory cache so the backend can run standalone in local dev.
if (builder.Configuration.GetConnectionString("cache") is not null)
{
    builder.AddRedisDistributedCache("cache");
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Allow the React SPA to call the API directly (the Vite dev proxy keeps things same-origin in
// dev, so this is mainly a fallback / for running the SPA outside the proxy).
const string spaCorsPolicy = "spa";
builder.Services.AddCors(options =>
{
    options.AddPolicy(spaCorsPolicy, policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

// Syncs the artist catalog from Plex on startup, then daily.
builder.Services.AddHostedService<CatalogSyncService>();

// BFF auth: cookie session + OIDC (Keycloak) code flow. See BffAuthentication.
builder.AddBffAuthentication();

builder.Host.RegisterAutofacModule<MainModule>();

var app = builder.Build();

app.UseExceptionHandler();

// One log line per HTTP request (method, path, status, duration) — the fastest way to
// spot which endpoint failed and with what status.
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(spaCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

// --- BFF auth endpoints (reached by the browser through the Vite proxy, verbatim) ---
// Start login: triggers the OIDC challenge, returning to a local returnUrl afterward.
app.MapGet("/auth/login", (string? returnUrl) =>
    {
        // Only allow local return paths (no open redirect).
        var target = returnUrl is not null && returnUrl.StartsWith('/') ? returnUrl : "/";
        return Results.Challenge(
            new AuthenticationProperties { RedirectUri = target },
            new[] { OpenIdConnectDefaults.AuthenticationScheme });
    })
    .WithName("Login");

// Sign out of both the local cookie and the IdP session.
app.MapGet("/auth/logout", () =>
        Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            new[] { CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme }))
    .WithName("Logout");

// Current user (200 with profile, or 401 if not signed in) — the SPA polls this to know auth state.
app.MapGet("/auth/me", (HttpContext http) =>
    {
        var user = http.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }
        return Results.Ok(new
        {
            subject = user.GetSubject(),
            username = user.FindFirst("preferred_username")?.Value,
            email = user.FindFirst("email")?.Value,
            displayName = user.FindFirst("name")?.Value,
        });
    })
    .WithName("Me");

app.MapGet("/artists", (ILibraryProvider libraryProvider) =>
    {
        return libraryProvider.GetAllArtistMetadata();
    })
    .WithName("GetArtists");

// The Library Catalog sync job: pull the artist list from Plex into the local catalog.
// Daily reads (GET /artists) serve from that catalog, so this is the only Plex-touching path.
app.MapPost("/catalog/refresh", (CatalogRefresher refresher) =>
    {
        return refresher.Refresh();
    })
    .WithName("RefreshCatalog");

// Related artists, unified across every similarity source. Ingests from Deezer on a cache
// miss/stale entry (persisting into the graph); pass ?refresh=true to force a re-fetch.
// The artist is a query param (not a path segment) so names with '/' (e.g. "AC/DC") work —
// an encoded slash in a path segment is rejected by ASP.NET routing by default.
app.MapGet("/related", (string artist, bool? refresh, RelatedArtistInteractor interactor) =>
    {
        return interactor.GetRelated(new ArtistKey(artist), forceRefresh: refresh ?? false);
    })
    .WithName("GetRelated");

// --- Per-user seeds (the taste anchors the recommendation engine grows from) ---
// artist is a query param (handles '/' in names); all require an authenticated user.
app.MapGet("/seeds", async (HttpContext http, IUserSeedRepo seeds) =>
        Results.Ok(await seeds.GetSeeds(http.User.GetSubject()!)))
    .RequireAuthorization()
    .WithName("GetSeeds");

app.MapPut("/seeds", async (string artist, HttpContext http, IUserSeedRepo seeds) =>
    {
        await seeds.AddSeed(http.User.GetSubject()!, artist);
        return Results.NoContent();
    })
    .RequireAuthorization()
    .WithName("AddSeed");

app.MapDelete("/seeds", async (string artist, HttpContext http, IUserSeedRepo seeds) =>
    {
        await seeds.RemoveSeed(http.User.GetSubject()!, artist);
        return Results.NoContent();
    })
    .RequireAuthorization()
    .WithName("RemoveSeed");

// --- Discovery: the per-user swipe loop over the similarity graph (DiscoveryEngine) ---
// All require an authenticated user; the queue is built lazily from the user's seeds on first read.
// artist is a query param (handles '/' in names), pageSize is clamped to keep paging sane.
app.MapGet("/discovery", async (HttpContext http, DiscoveryEngine engine, int? page, int? pageSize) =>
        Results.Ok(await engine.GetQueue(http.User.GetSubject()!, Math.Max(page ?? 0, 0), Math.Clamp(pageSize ?? 20, 1, 100))))
    .RequireAuthorization()
    .WithName("GetDiscoveryQueue");

// Rebuild the pending queue from the current seeds (keeps likes/dislikes) — use after editing seeds.
app.MapPost("/discovery/refresh", async (HttpContext http, DiscoveryEngine engine, int? page, int? pageSize) =>
        Results.Ok(await engine.Rebuild(http.User.GetSubject()!, Math.Max(page ?? 0, 0), Math.Clamp(pageSize ?? 20, 1, 100))))
    .RequireAuthorization()
    .WithName("RefreshDiscoveryQueue");

// Thumbs-up: like the artist, grow the frontier, and add it to the "to buy" wishlist.
app.MapPost("/discovery/like", async (string artist, HttpContext http, DiscoveryEngine engine) =>
    {
        await engine.Like(http.User.GetSubject()!, artist);
        return Results.NoContent();
    })
    .RequireAuthorization()
    .WithName("LikeCandidate");

// Thumbs-down: prune the artist from the queue (never shown or expanded again).
app.MapPost("/discovery/dislike", async (string artist, HttpContext http, DiscoveryEngine engine) =>
    {
        await engine.Dislike(http.User.GetSubject()!, artist);
        return Results.NoContent();
    })
    .RequireAuthorization()
    .WithName("DislikeCandidate");

// The "to buy" wishlist: every artist the user has thumbed-up.
app.MapGet("/discovery/purchases", async (HttpContext http, DiscoveryEngine engine) =>
        Results.Ok(await engine.GetPurchases(http.User.GetSubject()!)))
    .RequireAuthorization()
    .WithName("GetPurchases");

app.MapDefaultEndpoints();

app.Run();