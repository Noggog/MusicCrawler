using Newtonsoft.Json;

namespace MusicCrawler.Lib;

public static class StringExtensions
{
    public static T? ToDto<T>(this string jsonString)
    {
        return JsonConvert.DeserializeObject<T>(jsonString);
    }
}