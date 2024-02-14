namespace MusicCrawler.Lib;

public static class ListExtensions
{
    public static List<T> TakeRandomly<T>(this IReadOnlyList<T> list, int count)
    {
        Random random = new Random();
        return list.OrderBy(x => random.Next())
            .Take(count).ToList();
    }
}