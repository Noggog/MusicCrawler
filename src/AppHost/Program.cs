using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithRedisInsight();

var database = builder.AddMongoDB("db")
    .WithLifetime(ContainerLifetime.Persistent);

var backend = builder.AddProject<MusicCrawler_Backend>("backend")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(database)
    .WaitFor(database)
    // BFF auth against the existing Authentik instance. The issuer/client id/secret are secrets,
    // so they're forwarded from host env vars (set them in local.secrets.env) rather than committed.
    // BFF_PUBLIC_ORIGIN is the browser-facing SPA origin the OIDC callback returns through.
    .WithEnvironment("OIDC_AUTHORITY", Environment.GetEnvironmentVariable("OIDC_AUTHORITY"))
    .WithEnvironment("OIDC_CLIENT_ID", Environment.GetEnvironmentVariable("OIDC_CLIENT_ID"))
    .WithEnvironment("OIDC_CLIENT_SECRET", Environment.GetEnvironmentVariable("OIDC_CLIENT_SECRET"))
    .WithEnvironment("BFF_PUBLIC_ORIGIN",
        Environment.GetEnvironmentVariable("BFF_PUBLIC_ORIGIN") ?? "http://localhost:5173")
    // MongoDbProvider reads the connection string from the "MONGO_URI" env var; feed it
    // the Aspire-provisioned MongoDB connection string (otherwise it's null -> 500s).
    .WithEnvironment("MONGO_URI", database)
    // Plex settings are forwarded from same-named env vars at run time (override as needed).
    // Aspire does not auto-propagate AppHost env vars to children, so these are explicit.
    .WithEnvironment("PLEX_ENDPOINT", Environment.GetEnvironmentVariable("PLEX_ENDPOINT"))
    .WithEnvironment("PLEX_LIBRARY", Environment.GetEnvironmentVariable("PLEX_LIBRARY"))
    .WithEnvironment("PLEX_TOKEN", Environment.GetEnvironmentVariable("PLEX_TOKEN"))
    // Deezer similarity-graph knobs (both optional; the backend defaults them when unset):
    //   DEEZER_BASE_URI       override the public Deezer API endpoint
    //   RELATED_STALENESS_DAYS how long a stored edge set is considered fresh (default 30)
    .WithEnvironment("DEEZER_BASE_URI", Environment.GetEnvironmentVariable("DEEZER_BASE_URI"))
    .WithEnvironment("RELATED_STALENESS_DAYS", Environment.GetEnvironmentVariable("RELATED_STALENESS_DAYS"));

builder.AddNpmApp("web", "../MusicCrawler.Web", "dev")
    .WithReference(backend)
    .WaitFor(backend)
    .WithEnvironment("VITE_BACKEND_URL", backend.GetEndpoint("http"))
    // Pin the web app to a fixed port (Vite reads PORT) so the URL is stable across runs
    // instead of Aspire allocating a random one.
    .WithHttpEndpoint(port: 5173, env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();
