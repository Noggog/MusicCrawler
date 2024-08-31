using Newtonsoft.Json;

namespace MusicCrawler.Lib;

public static class ObjectExtensions
{
    public static string ToJson(this object obj)
    {
        return JsonConvert.SerializeObject(obj);
    }

    public static T Also<T>(this T obj, Action<T> action)
    {
        action(obj);
        return obj;
    }
    
    public static TResult Let<TInput, TResult>(this TInput obj, Func<TInput, TResult> func)
        where TInput : class
    {
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));

        return func(obj);
    }
}