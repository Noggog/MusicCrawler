using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var backend = builder.AddProject<MusicCrawler_Backend>("backend")
    .WithEnvironment("plexEndpoint", "https://plex.noggog.ing")
    .WithEnvironment("plexClientSecret", Environment.GetEnvironmentVariable("PLEX_TOKEN"));

builder.Build().Run();
