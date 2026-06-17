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

// Syncs the artist catalog (artists + their albums) from Plex on startup, then daily.
builder.Services.AddHostedService<CatalogSyncService>();

// Diffs each owned artist's Deezer discography against the library to find missing albums; runs
// shortly after startup (so the catalog is populated first), then daily.
builder.Services.AddHostedService<AlbumSyncService>();

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

// Deezer play info for an artist: a 30-second preview MP3 to sample plus the deezer.com artist
// link. The SPA plays the preview in a plain <audio> (no login/cookies, unlike the embed widget).
// Public Deezer metadata, so no auth; cached server-side.
app.MapGet("/deezer/artist", async (string artist, DeezerArtistResolver resolver) =>
    {
        var info = await resolver.ResolvePlayInfo(artist);
        return info is null ? Results.NotFound() : Results.Ok(info);
    })
    .WithName("ResolveDeezerArtist");

// Deezer play info for a specific album id: its previewable tracks plus the deezer.com album link.
// Used to sample "missing album" cards. Public Deezer metadata, so no auth; cached server-side.
app.MapGet("/deezer/album", async (long id, DeezerArtistResolver resolver) =>
        Results.Ok(await resolver.ResolveAlbumPlayInfo(id)))
    .WithName("ResolveDeezerAlbum");

// The missing-album sync job (Deezer discography diff per owned artist). Heavy, so it's a dev-only
// manual trigger; in production it runs on the daily AlbumSyncService schedule.
app.MapPost("/albums/missing/refresh", (MissingAlbumRefresher refresher) =>
    {
        return refresher.Refresh();
    })
    .WithName("RefreshMissingAlbums");

// --- Discovery: the per-user feed + ratings over the similarity graph (DiscoveryEngine) ---
// All require an authenticated user. artist/album are query params (handles '/' in names),
// pageSize is clamped to keep paging sane.

// A paged feed section for one category: RecommendedArtist | LibraryArtist | MissingAlbum.
app.MapGet("/discovery", async (HttpContext http, DiscoveryEngine engine, string? kind, int? page, int? pageSize) =>
    {
        var feedKind = Enum.TryParse<FeedKind>(kind, ignoreCase: true, out var parsed)
            ? parsed
            : FeedKind.RecommendedArtist;
        return Results.Ok(await engine.GetFeed(
            http.User.GetSubject()!, feedKind, Math.Max(page ?? 0, 0), Math.Clamp(pageSize ?? 20, 1, 100)));
    })
    .RequireAuthorization()
    .WithName("GetDiscoveryFeed");

// A single mixed feed across the selected categories (comma-separated `kinds`), round-robin
// interleaved + shuffled by `seed` so the order is stable across pages. This is what the Discover
// page uses; the per-kind endpoint above remains for any single-category view.
app.MapGet("/discovery/mixed", async (
        HttpContext http, DiscoveryEngine engine, string? kinds, int? page, int? pageSize, int? seed) =>
    {
        var requested = (kinds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => Enum.TryParse<FeedKind>(k, ignoreCase: true, out var fk) ? (FeedKind?)fk : null)
            .Where(k => k.HasValue)
            .Select(k => k!.Value)
            .Distinct()
            .ToArray();
        if (requested.Length == 0)
        {
            requested = new[] { FeedKind.RecommendedArtist, FeedKind.MissingAlbum, FeedKind.LibraryArtist };
        }
        return Results.Ok(await engine.GetMixedFeed(
            http.User.GetSubject()!, requested,
            Math.Max(page ?? 0, 0), Math.Clamp(pageSize ?? 20, 1, 100), seed ?? 0));
    })
    .RequireAuthorization()
    .WithName("GetMixedDiscoveryFeed");

// Rebuild the pending recommendations from the current liked artists (keeps ratings).
app.MapPost("/discovery/refresh", async (HttpContext http, DiscoveryEngine engine) =>
    {
        await engine.Rebuild(http.User.GetSubject()!);
        return Results.NoContent();
    })
    .RequireAuthorization()
    .WithName("RefreshDiscoveryQueue");

// Rate an artist or (when album is supplied) a missing album. verdict = "up" (Liked) | "down" (Disliked).
app.MapPost("/discovery/rate", async (
        string artist, string? album, string? albumArt, string verdict,
        HttpContext http, DiscoveryEngine engine) =>
    {
        var status = verdict.Equals("up", StringComparison.OrdinalIgnoreCase)
            ? DiscoveryStatus.Liked
            : DiscoveryStatus.Disliked;
        var userId = http.User.GetSubject()!;
        if (string.IsNullOrEmpty(album))
        {
            await engine.RateArtist(userId, artist, status);
        }
        else
        {
            await engine.RateAlbum(userId, artist, album, albumArt, status);
        }
        return Results.NoContent();
    })
    .RequireAuthorization()
    .WithName("RateCandidate");

// Clear a rating, returning the artist/album to the feed.
app.MapDelete("/discovery/rate", async (string artist, string? album, HttpContext http, DiscoveryEngine engine) =>
    {
        var userId = http.User.GetSubject()!;
        if (string.IsNullOrEmpty(album))
        {
            await engine.ClearArtistRating(userId, artist);
        }
        else
        {
            await engine.ClearAlbumRating(userId, artist, album);
        }
        return Results.NoContent();
    })
    .RequireAuthorization()
    .WithName("ClearRating");

// Every rating the user has made, for the review page (albums that now exist are filtered out).
app.MapGet("/discovery/ratings", async (HttpContext http, DiscoveryEngine engine) =>
        Results.Ok(await engine.GetRatings(http.User.GetSubject()!)))
    .RequireAuthorization()
    .WithName("GetRatings");

// The "to buy" list: liked non-owned artists + liked albums not yet acquired.
app.MapGet("/discovery/purchases", async (HttpContext http, DiscoveryEngine engine) =>
        Results.Ok(await engine.GetPurchases(http.User.GetSubject()!)))
    .RequireAuthorization()
    .WithName("GetPurchases");

app.MapDefaultEndpoints();

app.Run();