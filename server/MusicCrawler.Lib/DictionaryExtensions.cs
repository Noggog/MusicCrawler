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

        foreach (var kv in originalDictionary)
        {
            foreach (var value in kv.Value)
            {
                inverseDictionary.GetOrAdd(value)
                    .Add(kv.Key);
            }
        }

        return inverseDictionary;
    }
}