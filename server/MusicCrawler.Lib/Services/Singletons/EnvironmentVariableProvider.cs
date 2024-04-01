namespace MusicCrawler.Lib.Services.Singletons;

public class EnvironmentVariableProvider
{
    public string? DevEnv()
    {
        return Environment.GetEnvironmentVariable("DevEnv");
    }

    public string? PlexEndpoint()
    {
        return Environment.GetEnvironmentVariable("plexEndpoint");
    }

    public string? PlexClientSecret()
    {
        return Environment.GetEnvironmentVariable("plexClientSecret");
    }

    public string? MongoURI()
    {
        return Environment.GetEnvironmentVariable("mongoURI");
    }
}