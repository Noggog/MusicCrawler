namespace MusicCrawler.Lib;

public static class IEnumerableExtensions
{
    public static string ToLogStr<T>(this IEnumerable<T> enumerable)
    {
        var x = enumerable.Select(x => x?.ToString());
        return "[(" + String.Join("), (", x) + ")]";
    }
}