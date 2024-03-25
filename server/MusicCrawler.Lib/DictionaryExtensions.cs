using Newtonsoft.Json;
using Noggog;

namespace MusicCrawler.Lib;

public static class DictionaryExtensions
{
    public static Dictionary<TKey, List<TValue>> Inverse<TKey, TValue>(
        this Dictionary<TValue, TKey[]> originalDictionary)
        where TKey : notnull
        where TValue : notnull
    {
        var inverseDictionary = new Dictionary<TKey, List<TValue>>();

        foreach (var key in originalDictionary.Keys)
        {
            if (originalDictionary.TryGetValue(key, out var values))
            {
                foreach (var value in values)
                {
                    inverseDictionary.GetOrAdd(value)
                        .Add(key);
                }
            }
        }

        return inverseDictionary;
    }

    public static string ToLogStr(this Dictionary<ArtistKey, ArtistKey[]> dictionary)
    {
        return JsonConvert.SerializeObject(dictionary, Formatting.Indented);
    }


    public static IEnumerable<Recommendation> ToRecommendations(this Dictionary<ArtistKey, ArtistKey[]> dictionary)
    {
        return dictionary
            .Select(x =>
                new Recommendation(
                    Key: x.Key,
                    SourceArtists: x.Value)
            );
    }
}