using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithRedisInsight();

var backend = builder.AddProject<MusicCrawler_Backend>("backend")
    .WithReference(cache)
    .WaitFor(cache)
    .WithEnvironment("plexEndpoint", "https://plex.noggog.ing")
    .WithEnvironment("plexClientSecret", Environment.GetEnvironmentVariable("PLEX_TOKEN"));

builder.AddProject<MusicCrawler_Frontend>("frontend")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(backend)
    .WaitFor(backend);

builder.Build().Run();
