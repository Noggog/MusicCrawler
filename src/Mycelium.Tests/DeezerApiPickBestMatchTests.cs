using FluentAssertions;
using Mycelium.Deezer.Models;
using Mycelium.Deezer.Services;
using Xunit;

namespace Mycelium.Tests;

public class DeezerApiPickBestMatchTests
{
    // The real Deezer relevance order for "RJD2": a collaboration outranks the canonical artist, and
    // there are several exact-name (case-insensitive) entries with wildly different fan counts.
    private static DeezerArtist[] Rjd2Results() => new[]
    {
        new DeezerArtist { id = 358108972, name = "RJD2 & Supastition", nb_fan = 9 },
        new DeezerArtist { id = 256599212, name = "Rjd2", nb_fan = 4 },
        new DeezerArtist { id = 256599222, name = "Rjd2", nb_fan = 5 },
        new DeezerArtist { id = 3227, name = "RJD2", nb_fan = 98492 },
        new DeezerArtist { id = 4144366, name = "Aaron Livingston, RJD2", nb_fan = 168 },
    };

    [Fact]
    public void Prefers_exact_name_match_over_deezers_top_relevance_hit()
    {
        var best = DeezerApi.PickBestMatch(Rjd2Results(), "RJD2");

        // Not 358108972 ("RJD2 & Supastition"), which Deezer ranked first.
        best!.id.Should().Be(3227);
    }

    [Fact]
    public void Breaks_exact_name_ties_by_follower_count()
    {
        // Case-insensitive "rjd2" matches ids 256599212/256599222/3227 — the most-followed wins.
        var best = DeezerApi.PickBestMatch(Rjd2Results(), "rjd2");

        best!.id.Should().Be(3227);
    }

    [Fact]
    public void Falls_back_to_relevance_order_when_no_exact_match()
    {
        // "ALEX" has no literal "ALEX" in the results, so we defer to Deezer's first (strongest) guess.
        var results = new[]
        {
            new DeezerArtist { id = 541784, name = "Alex Warren", nb_fan = 146474 },
            new DeezerArtist { id = 72639412, name = "ALEX G", nb_fan = 200000 },
        };

        var best = DeezerApi.PickBestMatch(results, "ALEX");

        best!.id.Should().Be(541784);
    }

    [Fact]
    public void Returns_null_when_there_are_no_candidates()
    {
        DeezerApi.PickBestMatch(System.Array.Empty<DeezerArtist>(), "RJD2").Should().BeNull();
    }
}
