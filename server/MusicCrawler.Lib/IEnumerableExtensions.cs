namespace MusicCrawler.Lib;

public static class IEnumerableExtensions
{
    public static string ToLogStr<T>(this IEnumerable<T> enumerable)
    {
        var x = enumerable.Select(x => x?.ToString());
        return "[(" + String.Join("), (", x) + ")]";
    }

    // TODO: This might be unnecessary because it might already exist under a different name.
    public static string JoinToStr<T>(this IEnumerable<T> enumerable, string delimiter)
    {
        var x = enumerable.Select(x => x?.ToString());
        return String.Join(delimiter, x);
    }

    public static IEnumerable<T> TakeRandomly<T>(this IEnumerable<T> list, int count)
    {
        Random random = new Random();
        return list.OrderBy(x => random.Next())
            .Take(count);
    }

    public static Dictionary<ArtistKey, ArtistKey[]> ToMap(this IEnumerable<Recommendation> list)
    {
        return list
            .ToDictionary(
                keySelector: x => x.Key,
                elementSelector: x => x.SourceArtists);
    }
}