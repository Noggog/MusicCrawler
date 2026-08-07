using FluentAssertions;
using Mycelium.Backend.Services.Singletons;
using Xunit;

namespace Mycelium.Tests;

public class AlbumTitleMatcherTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_normalizes_to_empty(string? title)
    {
        AlbumTitleMatcher.Normalize(title).Should().BeEmpty();
    }

    [Fact]
    public void Casing_and_surrounding_whitespace_are_folded_away()
    {
        AlbumTitleMatcher.Normalize("  Radiance  ").Should().Be("radiance");
    }

    [Fact]
    public void Internal_whitespace_is_collapsed_to_single_spaces()
    {
        AlbumTitleMatcher.Normalize("Radiance \t and\n\nSubmission").Should().Be("radiance and submission");
    }

    [Theory]
    [InlineData("Don’t Look Now")]  // curly apostrophe
    [InlineData("Donʼt Look Now")]  // modifier letter apostrophe
    [InlineData("Don′t Look Now")]  // prime
    public void Apostrophe_variants_fold_to_a_straight_quote(string title)
    {
        AlbumTitleMatcher.Normalize(title).Should().Be("don't look now");
    }

    [Fact]
    public void Curly_double_quotes_fold_to_straight_quotes()
    {
        AlbumTitleMatcher.Normalize("“Heroes”").Should().Be("\"heroes\"");
    }

    [Theory]
    [InlineData("Live – 1975")]
    [InlineData("Live — 1975")]
    public void En_and_em_dashes_fold_to_a_hyphen(string title)
    {
        AlbumTitleMatcher.Normalize(title).Should().Be("live - 1975");
    }

    [Fact]
    public void Zero_width_characters_are_stripped()
    {
        AlbumTitleMatcher.Normalize("﻿Rad​iance‍").Should().Be("radiance");
    }

    // The CFCF case: Plex and Deezer disagree on the ampersand convention, which used to make an
    // owned album look missing.
    [Theory]
    [InlineData("Radiance & Submission")]
    [InlineData("Radiance and Submission")]
    [InlineData("Radiance And Submission")]
    [InlineData("Radiance&Submission")]
    [InlineData("Radiance ＆ Submission")]
    public void Ampersand_and_the_word_and_normalize_to_the_same_title(string title)
    {
        AlbumTitleMatcher.Normalize(title).Should().Be("radiance and submission");
    }

    [Theory]
    [InlineData("R&B Classics")]
    [InlineData("R & B Classics")]
    [InlineData("R and B Classics")]
    public void Ampersand_inside_a_word_is_padded_so_both_conventions_agree(string title)
    {
        AlbumTitleMatcher.Normalize(title).Should().Be("r and b classics");
    }

    [Fact]
    public void A_leading_ampersand_gains_no_leading_space()
    {
        AlbumTitleMatcher.Normalize("& Then There Were Two").Should().Be("and then there were two");
    }

    [Fact]
    public void A_trailing_ampersand_gains_no_trailing_space()
    {
        AlbumTitleMatcher.Normalize("Me &").Should().Be("me and");
    }

    [Fact]
    public void Distinct_titles_still_normalize_differently()
    {
        AlbumTitleMatcher.Normalize("Radiance")
            .Should().NotBe(AlbumTitleMatcher.Normalize("Radiance & Submission"));
    }

    [Fact]
    public void Override_keys_agree_across_the_ampersand_swap()
    {
        // The purchase reconcile and the missing-album diff key off the same normalized form, so a
        // merge recorded under one convention has to be honoured under the other.
        AlbumOverrideKey.For("CFCF", "Radiance & Submission")
            .Should().Be(AlbumOverrideKey.For("cfcf", "Radiance and Submission"));
    }
}
