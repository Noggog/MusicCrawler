# Development Notes

This file contains helpful information for developers and AI assistants working on this codebase.

## Project Architecture

MusicCrawler is a .NET 9.0 Aspire distributed application that crawls music libraries (Plex) and provides recommendations using external services (Spotify). The application follows a modular architecture with separate projects for each concern.

### Core Components

- **AppHost**: Aspire orchestration host that manages the distributed application lifecycle and configures Redis cache and MongoDB
- **MusicCrawler.Backend**: ASP.NET Core Web API that serves artist data via REST endpoints
- **MusicCrawler.Web**: Vite + React + TypeScript single-page app for the user interface (replaced the former Blazor frontend). Talks to the backend's REST endpoints; see "Frontend (React)" below.
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
```

### Frontend (React)

The UI lives in `src/MusicCrawler.Web` (Vite + React + TypeScript).

```bash
# First time: install dependencies
cd src/MusicCrawler.Web
npm install

# Normally the Aspire AppHost launches the Vite dev server for you
# (registered via AddNpmApp("web", ...) in src/AppHost/Program.cs).

# To run the SPA standalone (backend must be running separately):
npm run dev      # dev server with hot reload
npm run build    # type-check + production build to dist/
```

The dev server proxies `/api/*` to the backend. The backend URL comes from the
`VITE_BACKEND_URL` env var (injected by the AppHost), falling back to the backend's default dev
HTTP endpoint when run standalone — see `src/MusicCrawler.Web/vite.config.ts`.

### Testing
```bash
# Run all tests
dotnet test src/MusicCrawler.Tests

# Run tests with verbose output
dotnet test src/MusicCrawler.Tests --verbosity normal
```

## Configuration

Set these in the shell before `dotnet run --project src/AppHost`. The AppHost forwards
them to the backend explicitly via `WithEnvironment` (Aspire does **not** auto-propagate
AppHost env vars to child services).

| Env var | Required? | Purpose |
|---|---|---|
| `PLEX_TOKEN` | **Yes** | Plex authentication token |
| `PLEX_ENDPOINT` | **Yes** | Plex server base URL (backend throws if unset) |
| `PLEX_LIBRARY` | No | Which Plex library to crawl; if unset, falls back to the first artist-type library |
| `MONGO_URI` | No (auto) | Mongo connection string |

There are no hardcoded defaults — every value comes from the environment.

Notes:
- `MONGO_URI` is supplied by the AppHost from the provisioned MongoDB resource's
  connection string — you don't set it yourself when running via the AppHost.
- Spotify client credentials are still hardcoded in `MainModule` (should be externalized;
  the Spotify path is deprecated anyway — see Known Issues).

## Infrastructure Dependencies

- **Redis**: Used for distributed caching
- **MongoDB**: Primary data storage
- **Plex Media Server**: Source music library
- **Spotify API**: Music recommendation service (see deprecation note below)

The Aspire AppHost automatically provisions Redis and MongoDB containers with persistent storage during development.

## Known Issues / TODO

### Spotify recommendations API is deprecated (blocks the discovery feature)

On 2024-11-27 Spotify deprecated `/v1/recommendations`, `/v1/artists/{id}/related-artists`, and
`/v1/audio-features`. Apps that did **not** already have extended quota access before that date now
receive `403 Forbidden` on these endpoints, with no waitlist or replacement. The current
recommendation path (`SpotifyApi.Recommendations` → `SpotifyProvider` → `RecommendationInteractor`)
relies on `/v1/recommendations` and will not work for a newly-registered Spotify client.

**Before building out the recommendation feature**, swap the similarity source.

**Decision: use the Deezer API** (chosen 2026-06-15). Rationale: keyless (no API key/OAuth to
manage), free, and its `/artist/{id}/related` endpoint returns related artists *plus* artist
images — which also backfills the `ArtistImageUrl` the Plex path currently leaves `null`. Flow:
resolve a Plex artist name via `GET https://api.deezer.com/search/artist?q={name}` → take the
artist `id` → `GET https://api.deezer.com/artist/{id}/related`. No auth required.

Implementation note: keep the existing `IRecommendationProvider` interface and add a
`DeezerProvider` implementation alongside / replacing `SpotifyProvider`; register it in
`MainModule`. (Spotify remains usable for search/metadata if ever needed, just not similarity.)

Alternatives considered: Last.fm `artist.getSimilar` (name-based, but commercial-use license
friction) and ListenBrainz (CC0 data, but requires MusicBrainz ID resolution).

References:
- https://developer.spotify.com/blog/2024-11-27-changes-to-the-web-api
- https://developers.deezer.com/api/artist (see `/artist/{id}/related`)