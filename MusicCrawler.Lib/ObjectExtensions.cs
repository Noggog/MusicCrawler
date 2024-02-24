using Newtonsoft.Json;

namespace MusicCrawler.Lib;

public static class ObjectExtensions
{
    public static string ToJson(this object obj)
    {
        return JsonConvert.SerializeObject(obj);
    }
}