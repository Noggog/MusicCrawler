using MusicCrawler.Interfaces;

namespace MusicCrawler.Tests;

/// <summary>
/// In-memory <see cref="IAlbumMatchOverrideRepo"/> mirroring the Mongo upsert (one entry per
/// lower-cased match-artist + Deezer title) so a merge recorded in a test is honoured by later
/// reconciles the same way it is in production.
/// </summary>
internal sealed class FakeAlbumMatchOverrideRepo : IAlbumMatchOverrideRepo
{
    private readonly Dictionary<string, AlbumMatchOverride> _items = new();

    public IReadOnlyCollection<AlbumMatchOverride> Items => _items.Values;

    public Task<AlbumMatchOverride[]> GetAll() => Task.FromResult(_items.Values.ToArray());

    public Task Add(AlbumMatchOverride @override)
    {
        _items[$"{@override.MatchArtist.ToLowerInvariant()}|{@override.DeezerTitle.ToLowerInvariant()}"] = @override;
        return Task.CompletedTask;
    }
}
