﻿namespace MusicCrawler.Lib.Services.Singletons;

public class RandomStringGenerator
{
    private readonly Random _random;
    
    public RandomStringGenerator(Random random)
    {
        _random = random;
    }
    
    public string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[_random.Next(s.Length)]).ToArray());
    }
}