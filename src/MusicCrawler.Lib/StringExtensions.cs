using Newtonsoft.Json;

namespace MusicCrawler.Lib;

public static class StringExtensions
{
    public static T? ToDto<T>(this string jsonString)
    {
        return JsonConvert.DeserializeObject<T>(jsonString);
    }

    public static string Truncate(this string str, int maxLength)
    {
        if (str == null)
            throw new ArgumentNullException(nameof(str));

        return str.Length > maxLength ? str.Substring(0, maxLength) : str;
    }
}