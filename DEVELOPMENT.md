# Development Notes

This file contains helpful information for developers and AI assistants working on this codebase.

## Project Architecture

MusicCrawler is a .NET 9.0 Aspire distributed application that crawls music libraries (Plex) and provides recommendations using external services (Spotify). The application follows a modular architecture with separate projects for each concern.

### Core Components

- **AppHost**: Aspire orchestration host that manages the distributed application lifecycle and configures Redis cache and MongoDB
- **MusicCrawler.Backend**: ASP.NET Core Web API that serves artist data via REST endpoints
- **MusicCrawler.Frontend**: Blazor web application for the user interface
- **MusicCrawler.Interfaces**: Shared contracts and data models used across all modules
- **ServiceDefaults**: Aspire shared project containing common telemetry and service discovery configuration

### Integration Modules

- **MusicCrawler.Plex**: Integrates with Plex media server for music library access
- **MusicCrawler.Spotify**: Integrates with Spotify API for music recommendations
- **MusicCrawler.MongoDB**: Provides MongoDB data persistence layer

### Dependency Injection

The application uses Autofac for dependency injection. Each module registers its services via Autofac modules:
- Services are registered as SingleInstance by default
- Interface implementations are auto-registered using assembly scanning
- Configuration objects are registered as instances (e.g., SpotifyClientInfo, PlexEndpointInfo)

### Key Interfaces

- `IRecommendationProvider`: Provides music recommendations based on artist data
- `ILibraryQuery`: Queries music library metadata
- `ILibraryProvider`: Provides access to artist metadata from music libraries

## Development Commands

### Building and Running
```bash
# Build the entire solution
dotnet build src/MusicCrawler.sln

# Run the application (starts all Aspire services)
dotnet run --project src/AppHost

# Run individual projects
dotnet run --project src/MusicCrawler.Backend
dotnet run --project src/MusicCrawler.Frontend
```

### Testing
```bash
# Run all tests
dotnet test src/MusicCrawler.Tests

# Run tests with verbose output
dotnet test src/MusicCrawler.Tests --verbosity normal
```

## Configuration

The application requires several environment variables:
- `PLEX_TOKEN`: Plex authentication token (set in AppHost configuration)
- Plex endpoint and preferred library are configured in AppHost/Program.cs
- Spotify client credentials are hardcoded in MainModule (should be externalized)

## Infrastructure Dependencies

- **Redis**: Used for distributed caching
- **MongoDB**: Primary data storage
- **Plex Media Server**: Source music library
- **Spotify API**: Music recommendation service

The Aspire AppHost automatically provisions Redis and MongoDB containers with persistent storage during development.