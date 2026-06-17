using MusicCrawler.Backend;
using MusicCrawler.Backend.Services.Singletons;
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

app.MapDefaultEndpoints();

app.Run();