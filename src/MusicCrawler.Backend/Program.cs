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

// Periodically tops up each user's recommendation queue (additive — grows the frontier and refreshes
// stale similarity edges without clearing pending). Cadence via QUEUE_REPLENISH_INTERVAL_HOURS.
builder.Services.AddHostedService<QueueReplenishService>();

// The Deezer download engine (DownloadService) is registered in MainModule as a shared singleton
// hosted service, so the "download now" endpoint and the drainer loop are the same instance.

// BFF auth: cookie session + OIDC (Keycloak) code flow. See BffAuthentication.
builder.AddBffAuthentication();

builder.Host.RegisterAutofacModule<MainModule>();

var app = builder.Build();

app.UseExceptionHandler();

// One log line per HTTP request (method, path, status, duration) — the fastest way to
// spot which endpoint failed and with what status.
app.UseSerilogRequestLogging();

// Serve the built SPA (production: the Vite build is copied to wwwroot in the image). No-op in
// local dev, where Vite serves the SPA itself and proxies /api + /auth to this backend.
app.UseDefaultFiles();
app.UseStaticFiles();

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
app.MapGet("/auth/me", (HttpContext http, DevUsers devUsers, ILoggerFactory loggerFactory) =>
    {
        var user = http.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        var isDev = devUsers.Includes(user);

        // Diagnostic: dump every claim and the dev-match decision so we can see exactly what the IdP
        // sends (e.g. whether preferred_username is present, and under what value). Remove once the
        // DEV_USERNAMES match is confirmed.
        /*var log = loggerFactory.CreateLogger("AuthMe");
        log.LogInformation(
            "auth/me claims: [{Claims}]; preferred_username={Username}; DEV_USERNAMES=[{DevUsers}]; isDev={IsDev}",
            string.Join(", ", user.Claims.Select(c => $"{c.Type}={c.Value}")),
            user.FindFirst("preferred_username")?.Value ?? "(none)",
            string.Join(", ", devUsers.Configured),
            isDev);*/

        return Results.Ok(new
        {
            subject = user.GetSubject(),
            username = user.FindFirst("preferred_username")?.Value,
            email = user.FindFirst("email")?.Value,
            displayName = user.FindFirst("name")?.Value,
            // Drives the in-app dev panel's visibility (DEV_USERNAMES). The dev endpoints enforce the
            // same check server-side, so this only governs what the UI bothers to show.
            isDev,
        });
    })
    .WithName("Me");

// Application API, grouped under /api so it shares the origin with the SPA without the SPA's
// client routes (/artists, /related, /purchases, ...) colliding with same-named API paths.
var api = app.MapGroup("/api");

api.MapGet("/artists", (ILibraryProvider libraryProvider) =>
    {
        return libraryProvider.GetArtistList();
    })
    .WithName("GetArtists");

// Pin a library artist to a specific Deezer artist id — the fix for a misassociation (e.g. a
// common name like "Alex" resolving to the wrong, more popular act). Stores a sticky override and
// force-refreshes that artist's similarity edges so the graph re-derives from the correct id, then
// rebuilds the caller's recommendation queue so candidates from the old (wrong) edges drop off
// immediately rather than lingering until the next manual rebuild.
// Auth-gated: this is a maintainer correction. artist is a query param so '/' in names works.
api.MapPost("/artists/deezer-id", async (HttpContext http, string artist, long id,
        DeezerArtistResolver resolver, RelatedArtistInteractor interactor, DiscoveryEngine engine) =>
    {
        var identity = await resolver.SetOverride(artist, id);
        if (identity is null)
        {
            return Results.NotFound();
        }

        await interactor.GetRelated(new ArtistKey(artist), forceRefresh: true);
        await engine.Rebuild(http.User.GetSubject()!);
        return Results.Ok(identity);
    })
    .RequireAuthorization()
    .WithName("SetArtistDeezerId");

// Clear a Deezer override so the artist re-resolves from a name search next time. Auth-gated.
api.MapDelete("/artists/deezer-id", async (string artist, DeezerArtistResolver resolver) =>
    {
        await resolver.ClearOverride(artist);
        return Results.NoContent();
    })
    .RequireAuthorization()
    .WithName("ClearArtistDeezerId");

// Free-text Deezer artist search powering the "Correct association" picker: candidate artists
// (id, name, fans, link, photo) in relevance order. Public Deezer metadata, so no auth.
api.MapGet("/deezer/search", async (string q, int? limit, DeezerArtistResolver resolver) =>
        Results.Ok(await resolver.SearchArtists(q, Math.Clamp(limit ?? 10, 1, 25))))
    .WithName("SearchDeezerArtists");

// ---- Cross-source identity ("Sources" tab): one set of generic routes over every registered
// ISourceIdentityCorrector (deezer, musicbrainz, …), dispatched by the {source} path segment. ----

// Every source's resolved identity (id + link-out + override flag) for one artist, for the tab.
api.MapGet("/artists/sources", async (string artist, ArtistSourcesService sources) =>
        Results.Ok(await sources.Get(new ArtistKey(artist))))
    .RequireAuthorization()
    .WithName("GetArtistSources");

// Free-text candidate search within one source, powering that source's "Correct association" picker.
api.MapGet("/sources/{source}/search",
        async (string source, string q, int? limit, IEnumerable<ISourceIdentityCorrector> correctors) =>
        {
            var corrector = correctors.FirstOrDefault(c => c.Source == source);
            return corrector is null
                ? Results.NotFound()
                : Results.Ok(await corrector.Search(q, Math.Clamp(limit ?? 10, 1, 25)));
        })
    .RequireAuthorization()
    .WithName("SearchSource");

// Pin an artist to a specific id on one source (sticky override), then re-derive that artist's
// similarity edges from the corrected ids and rebuild the caller's queue so the old (wrong) edges
// drop off immediately — mirrors the original Deezer-pin behaviour, now source-generic.
api.MapPost("/artists/sources/{source}",
        async (HttpContext http, string source, string artist, string id,
            IEnumerable<ISourceIdentityCorrector> correctors,
            RelatedArtistInteractor interactor, DiscoveryEngine engine) =>
        {
            var corrector = correctors.FirstOrDefault(c => c.Source == source);
            if (corrector is null) return Results.NotFound();

            var identity = await corrector.Pin(new ArtistKey(artist), id);
            if (identity is null) return Results.NotFound();

            await interactor.GetRelated(new ArtistKey(artist), forceRefresh: true);
            await engine.Rebuild(http.User.GetSubject()!);
            return Results.Ok(identity);
        })
    .RequireAuthorization()
    .WithName("PinArtistSource");

// Clear a source's pin so the artist re-resolves from a name search next time.
api.MapDelete("/artists/sources/{source}",
        async (string source, string artist, IEnumerable<ISourceIdentityCorrector> correctors) =>
        {
            var corrector = correctors.FirstOrDefault(c => c.Source == source);
            if (corrector is null) return Results.NotFound();
            await corrector.Clear(new ArtistKey(artist));
            return Results.NoContent();
        })
    .RequireAuthorization()
    .WithName("ClearArtistSource");

// Backfill the Deezer identity for every present artist (id/name/fans/link/photo) into the catalog
// so the Artists page can flag misassociations. Heavy (one lookup per artist), so it's a one-shot
// maintenance trigger; afterwards ids are captured opportunistically as artists are sampled/rated.
api.MapPost("/artists/deezer/resolve-all", async (ILibraryProvider library, DeezerArtistResolver resolver) =>
    {
        var artists = await library.GetAllArtistMetadata();
        var resolved = 0;
        foreach (var a in artists)
        {
            if (await resolver.ResolveIdentity(a.ArtistKey.ArtistName) != null) resolved++;
        }
        return Results.Ok(new { total = artists.Length, resolved });
    })
    .RequireAuthorization()
    .WithName("ResolveAllDeezer");

// The Library Catalog sync job: pull the artist list from Plex into the local catalog.
// Daily reads (GET /artists) serve from that catalog, so this is the only Plex-touching path.
api.MapPost("/catalog/refresh", (CatalogRefresher refresher) =>
    {
        return refresher.Refresh();
    })
    .WithName("RefreshCatalog");

// Maintenance: clean up Plex's ';'-joined multi-artist names (e.g. "Nina Simone;Hot Chip") that
// leaked into the catalog and user ratings before ingestion-time splitting. GET previews the work;
// POST resolves it (splits catalog docs, re-attributes ratings). Auth-gated — the maintainer's tool.
api.MapGet("/maintenance/combined-artists", async (LibraryCleanupService cleanup) =>
        Results.Ok(await cleanup.Scan()))
    .RequireAuthorization()
    .WithName("ScanCombinedArtists");

api.MapPost("/maintenance/combined-artists/resolve", async (LibraryCleanupService cleanup) =>
        Results.Ok(await cleanup.Resolve()))
    .RequireAuthorization()
    .WithName("ResolveCombinedArtists");

// Related artists, unified across every similarity source. Ingests from Deezer on a cache
// miss/stale entry (persisting into the graph); pass ?refresh=true to force a re-fetch.
// The artist is a query param (not a path segment) so names with '/' (e.g. "AC/DC") work —
// an encoded slash in a path segment is rejected by ASP.NET routing by default.
api.MapGet("/related", (string artist, bool? refresh, RelatedArtistInteractor interactor) =>
    {
        return interactor.GetRelated(new ArtistKey(artist), forceRefresh: refresh ?? false);
    })
    .WithName("GetRelated");

// Deezer play info for an artist: a 30-second preview MP3 to sample plus the deezer.com artist
// link. The SPA plays the preview in a plain <audio> (no login/cookies, unlike the embed widget).
// Public Deezer metadata, so no auth; cached server-side.
// `fresh=true` bypasses the server cache to re-mint preview urls — the client sends it to retry a
// preview whose signed url expired while the readout sat open (Deezer's tokens live ~15 minutes).
api.MapGet("/deezer/artist", async (string artist, DeezerArtistResolver resolver, bool? fresh) =>
    {
        var info = await resolver.ResolvePlayInfo(artist, fresh ?? false);
        return info is null ? Results.NotFound() : Results.Ok(info);
    })
    .WithName("ResolveDeezerArtist");

// Deezer play info for a specific album id: its previewable tracks plus the deezer.com album link.
// Used to sample "missing album" cards. Public Deezer metadata, so no auth; cached server-side.
api.MapGet("/deezer/album", async (long id, DeezerArtistResolver resolver, bool? fresh) =>
        Results.Ok(await resolver.ResolveAlbumPlayInfo(id, fresh ?? false)))
    .WithName("ResolveDeezerAlbum");

// The missing-album sync job (Deezer discography diff per owned artist). Heavy, so it's a dev-only
// manual trigger; in production it runs on the daily AlbumSyncService schedule.
api.MapPost("/albums/missing/refresh", (MissingAlbumRefresher refresher) =>
    {
        return refresher.Refresh();
    })
    .WithName("RefreshMissingAlbums");

// --- Discovery: the per-user feed + ratings over the similarity graph (DiscoveryEngine) ---
// All require an authenticated user. artist/album are query params (handles '/' in names),
// pageSize is clamped to keep paging sane.

// A paged feed section for one category: RecommendedArtist | LibraryArtist | MissingAlbum.
api.MapGet("/discovery", async (HttpContext http, DiscoveryEngine engine, string? kind, int? page, int? pageSize) =>
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
api.MapGet("/discovery/mixed", async (
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
            requested = new[]
            {
                FeedKind.RecommendedArtist, FeedKind.MissingAlbum,
                FeedKind.RecommendedLibraryArtist, FeedKind.SeedLibraryArtist,
            };
        }
        return Results.Ok(await engine.GetMixedFeed(
            http.User.GetSubject()!, requested,
            Math.Max(page ?? 0, 0), Math.Clamp(pageSize ?? 20, 1, 100), seed ?? 0));
    })
    .RequireAuthorization()
    .WithName("GetMixedDiscoveryFeed");

// Rebuild the pending recommendations from the current liked artists (keeps ratings).
api.MapPost("/discovery/refresh", async (HttpContext http, DiscoveryEngine engine) =>
    {
        await engine.Rebuild(http.User.GetSubject()!);
        return Results.NoContent();
    })
    .RequireAuthorization()
    .WithName("RefreshDiscoveryQueue");

// Rate an artist or (when album is supplied) a missing album. verdict = "up" (Liked) | "down" (Disliked).
api.MapPost("/discovery/rate", async (
        string artist, string? album, string? albumArt, string verdict,
        HttpContext http, DiscoveryEngine engine, IArtistTagger tagger) =>
    {
        var status = verdict.Equals("up", StringComparison.OrdinalIgnoreCase)
            ? DiscoveryStatus.Liked
            : DiscoveryStatus.Disliked;
        var userId = http.User.GetSubject()!;
        if (string.IsNullOrEmpty(album))
        {
            await engine.RateArtist(userId, artist, status);
            // Mirror the verdict into Plex as a per-user collection ("<username>_liked"/"_disliked"),
            // which a music smart playlist can filter on via "Artist Collection". Stamp the new verdict
            // and strip the opposite so the latest rating is the only tag left (a like→dislike flip drops
            // "_liked"). Best-effort (never throws), so a Plex hiccup can't fail the rating.
            var username = http.User.FindFirst("preferred_username")?.Value;
            var tag = ArtistTag.For(username, status);
            if (tag != null)
            {
                var opposite = status == DiscoveryStatus.Liked ? DiscoveryStatus.Disliked : DiscoveryStatus.Liked;
                var oppositeTag = ArtistTag.For(username, opposite);
                var remove = oppositeTag != null ? new[] { oppositeTag } : Array.Empty<string>();
                await tagger.SetTags(artist, tag, remove);
            }
        }
        else
        {
            await engine.RateAlbum(userId, artist, album, albumArt, status);
        }
        return Results.NoContent();
    })
    .RequireAuthorization()
    .WithName("RateCandidate");

// A liked non-owned artist's acquirable albums (their Deezer discography minus anything already
// owned), surfaced inline under the just-rated card so a fresh discovery can be acted on. Fetched
// on demand (one Deezer call) only when an artist is liked — not precomputed per feed card.
api.MapGet("/discovery/artist-albums", async (string artist, HttpContext http, DiscoveryEngine engine) =>
        Results.Ok(await engine.ArtistAlbums(http.User.GetSubject()!, artist)))
    .RequireAuthorization()
    .WithName("GetArtistAlbums");

// An owned artist's full Deezer discography for the Artists-page drill-down: every LP flagged owned
// vs. missing, missing ones overlaid with the user's verdict. One Deezer call per expand.
api.MapGet("/discovery/artist-discography", async (string artist, HttpContext http, DiscoveryEngine engine) =>
        Results.Ok(await engine.ArtistDiscography(http.User.GetSubject()!, artist)))
    .RequireAuthorization()
    .WithName("GetArtistDiscography");

// Snooze a recommendation: hide it for the chosen duration; it resurfaces when the window lapses.
// Snoozes an artist, or — when album is supplied — a missing album. duration = week | month | year
// (mapped server-side to 7 / 30 / 365 days).
api.MapPost("/discovery/snooze", async (
        string artist, string? album, string? albumArt, string duration, HttpContext http, DiscoveryEngine engine) =>
    {
        var window = duration.ToLowerInvariant() switch
        {
            "week" => TimeSpan.FromDays(7),
            "month" => TimeSpan.FromDays(30),
            "year" => TimeSpan.FromDays(365),
            _ => (TimeSpan?)null,
        };
        if (window is null)
        {
            return Results.Problem("duration must be week, month, or year.", statusCode: 400);
        }
        var userId = http.User.GetSubject()!;
        if (string.IsNullOrEmpty(album))
        {
            await engine.SnoozeArtist(userId, artist, window.Value);
        }
        else
        {
            await engine.SnoozeAlbum(userId, artist, album, albumArt, window.Value);
        }
        return Results.NoContent();
    })
    .RequireAuthorization()
    .WithName("SnoozeCandidate");

// Clear a rating, returning the artist/album to the feed.
api.MapDelete("/discovery/rate", async (
        string artist, string? album, HttpContext http, DiscoveryEngine engine, IArtistTagger tagger) =>
    {
        var userId = http.User.GetSubject()!;
        if (string.IsNullOrEmpty(album))
        {
            await engine.ClearArtistRating(userId, artist);
            // Undo the Plex collection too — a cleared verdict shouldn't leave its "<username>_liked"/
            // "_disliked" tag behind. We don't know which verdict it was, so strip both (the user holds
            // at most one); best-effort, so a Plex hiccup can't fail the clear.
            var username = http.User.FindFirst("preferred_username")?.Value;
            var tags = new[] { DiscoveryStatus.Liked, DiscoveryStatus.Disliked }
                .Select(s => ArtistTag.For(username, s))
                .OfType<string>()
                .ToArray();
            if (tags.Length > 0)
            {
                await tagger.SetTags(artist, add: null, remove: tags);
            }
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
api.MapGet("/discovery/ratings", async (HttpContext http, DiscoveryEngine engine) =>
        Results.Ok(await engine.GetRatings(http.User.GetSubject()!)))
    .RequireAuthorization()
    .WithName("GetRatings");

// --- Dev panel: Plex tag maintenance ---
// Wipe and/or rebuild the per-user like/dislike labels so we can iterate on the tagging logic
// without leaving orphaned tags scattered across the library. Gated by the "DevUser" policy
// (DEV_USERNAMES) — the clear is destructive (it nukes every "_liked"/"_disliked" label), so these
// stay restricted to dev users rather than any signed-in user.
var dev = api.MapGroup("/dev/plex-tags").RequireAuthorization("DevUser");

// Strip every managed tag from every artist (clean slate).
dev.MapPost("/clear", async (PlexTagMaintenance maint) =>
        Results.Ok(new { cleared = await maint.ClearManagedTags() }))
    .WithName("DevClearPlexTags");

// Reapply tags from every user's stored ratings (additive; doesn't remove stale ones).
dev.MapPost("/reapply", async (PlexTagMaintenance maint) =>
        Results.Ok(new { applied = await maint.ReapplyFromRatings() }))
    .WithName("DevReapplyPlexTags");

// Nuke then reapply — the full reset.
dev.MapPost("/rebuild", async (PlexTagMaintenance maint) =>
    {
        var result = await maint.Rebuild();
        return Results.Ok(new { cleared = result.Cleared, applied = result.Applied });
    })
    .WithName("DevRebuildPlexTags");

// Whole-library similarity warm: force-populate every source's edges (Deezer + ListenBrainz) across
// the entire catalog, instead of waiting for the lazy, usage-driven path. Long-running (bounded by
// MusicBrainz's ~1 req/s), so it runs as a single-flight background job: POST kicks it off and
// returns the live status; GET polls progress. Same DevUser gate as the tag tools.
var devSim = api.MapGroup("/dev/similarity").RequireAuthorization("DevUser");

devSim.MapPost("/warm", (SimilarityGraphWarmer warmer, bool? force) =>
        Results.Ok(warmer.Start(force ?? false)))
    .WithName("DevWarmSimilarity");

devSim.MapGet("/warm", (SimilarityGraphWarmer warmer) =>
        Results.Ok(warmer.GetStatus()))
    .WithName("DevSimilarityWarmStatus");

// The shared "to buy" list: every user's liked non-owned artists + liked albums not yet acquired,
// persisted with a status (pending → sent → in-library). Reconciles on read so it's always current.
// Auth-gated, but not scoped to the caller — this is the library maintainer's unified queue.
api.MapGet("/purchases", async (PurchaseService purchases) =>
        Results.Ok(await purchases.GetActive()))
    .RequireAuthorization()
    .WithName("GetPurchases");

// A live snapshot of the download subsystem for the monitoring panel (backend, throttle, counts,
// what's downloading now). Cheap; polled by the UI.
api.MapGet("/purchases/status", async (PurchaseService purchases) =>
        Results.Ok(await purchases.GetDownloadSnapshot()))
    .RequireAuthorization()
    .WithName("DownloadStatus");

// Manually queue an item for download now (the "Download now"/"Retry" button). Non-blocking — the
// drainer does the fetch; returns immediately. Works whether or not automatic downloads are on.
api.MapPost("/purchases/download", async (string id, DownloadService downloads) =>
        await downloads.RequestDownload(id)
            ? Results.NoContent()
            : Results.Problem("Item isn't a downloadable Deezer album.", statusCode: 409))
    .RequireAuthorization()
    .WithName("DownloadPurchase");

// Undo — move a downloaded/queued item back to "pending".
api.MapPost("/purchases/unsend", async (string id, PurchaseService purchases) =>
        await purchases.Unsend(id) ? Results.NoContent() : Results.NotFound())
    .RequireAuthorization()
    .WithName("UnsendPurchase");

app.MapDefaultEndpoints();

// Any unmatched, non-API route serves the SPA shell so client-side deep links work.
app.MapFallbackToFile("index.html");

app.Run();