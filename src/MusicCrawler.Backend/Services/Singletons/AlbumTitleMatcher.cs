using System.Text;

namespace MusicCrawler.Backend.Services.Singletons;

/// <summary>
/// Canonical matching for album titles across sources (Plex, Deezer). The shared definition of
/// "same album by title" — used by both the missing-album diff and the purchase reconcile so an
/// album can't be considered owned by one and missing by the other.
/// </summary>
public static class AlbumTitleMatcher
{
    /// <summary>
    /// Canonical form for matching album titles across sources: trimmed, lower-cased, with curly
    /// quotes/apostrophes and en/em dashes folded to ASCII, zero-width characters stripped,
    /// ampersands spelled out as "and", and internal whitespace collapsed — so a title that differs
    /// only in typography (Plex's "Don't" vs. Deezer's "Don't") or in the ampersand convention
    /// (Plex's "Radiance &amp; Submission" vs. Deezer's "Radiance and Submission") still matches.
    /// </summary>
    public static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(title.Length);
        // A separator is owed before the next character we emit. Starts false so leading whitespace
        // is dropped, and is never flushed at the end so trailing whitespace is too.
        var pendingSpace = false;
        foreach (var ch in title)
        {
            switch (ch)
            {
                // Zero-width and BOM characters: drop entirely (often pasted/copied invisibly).
                case '​' or '‌' or '‍' or '﻿':
                    continue;
            }

            var c = ch switch
            {
                '‘' or '’' or 'ʼ' or '′' => '\'', // curly/modifier apostrophes, prime
                '“' or '”' => '"',                          // curly double quotes
                '–' or '—' => '-',                          // en/em dash
                _ => char.ToLowerInvariant(ch),
            };

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (c is '&' or '＆')
            {
                // Spelled out and space-padded on both sides, so "R&B", "R & B" and "R and B" all
                // land on the same form regardless of which side of the swap each source wrote.
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }
                sb.Append("and");
                pendingSpace = true;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }
            sb.Append(c);
        }

        return sb.ToString();
    }
}
