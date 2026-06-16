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
    // All Plex settings come from env vars set at run time (override as needed).
    .WithEnvironment("plexEndpoint", Environment.GetEnvironmentVariable("plexEndpoint") ?? "https://plex.noggog.ing")
    .WithEnvironment("preferredPlexLibrary", Environment.GetEnvironmentVariable("preferredPlexLibrary") ?? "Music Hub")
    .WithEnvironment("plexClientSecret", Environment.GetEnvironmentVariable("PLEX_TOKEN"));

builder.AddNpmApp("web", "../MusicCrawler.Web", "dev")
    .WithReference(backend)
    .WaitFor(backend)
    .WithEnvironment("VITE_BACKEND_URL", backend.GetEndpoint("http"))
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();
