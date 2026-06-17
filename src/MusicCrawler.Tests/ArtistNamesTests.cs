using FluentAssertions;
using MusicCrawler.Plex.Services;
using Xunit;

namespace MusicCrawler.Tests;

public class ArtistNamesTests
{
    [Fact]
    public void Plain_name_yields_itself()
    {
        ArtistNames.Split("Nina Simone").Should().Equal("Nina Simone");
    }

    [Fact]
    public void Semicolon_joined_name_splits_into_each_artist()
    {
        ArtistNames.Split("Nina Simone;Hot Chip").Should().Equal("Nina Simone", "Hot Chip");
    }

    [Fact]
    public void Surrounding_whitespace_on_each_part_is_trimmed()
    {
        ArtistNames.Split("Nina Simone ; Hot Chip ;  LCD Soundsystem")
            .Should().Equal("Nina Simone", "Hot Chip", "LCD Soundsystem");
    }

    [Fact]
    public void Empty_parts_from_stray_or_trailing_separators_are_dropped()
    {
        ArtistNames.Split("Nina Simone;;Hot Chip;").Should().Equal("Nina Simone", "Hot Chip");
    }

    [Fact]
    public void Case_insensitive_duplicates_are_collapsed_preserving_first_seen()
    {
        ArtistNames.Split("Hot Chip;hot chip;HOT CHIP").Should().Equal("Hot Chip");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(";")]
    [InlineData("  ;  ; ")]
    public void Blank_or_separator_only_input_yields_nothing(string? raw)
    {
        ArtistNames.Split(raw).Should().BeEmpty();
    }

    [Theory]
    [InlineData("AC/DC")]
    [InlineData("Florence + the Machine")]
    [InlineData("Tyler, the Creator")]
    [InlineData("Earth, Wind & Fire")]
    [InlineData("Simon & Garfunkel feat. Someone")]
    public void Other_separators_inside_legitimate_names_are_left_intact(string name)
    {
        // Only ';' splits — '/', '+', ',', '&', and "feat" are all parts of real artist names.
        ArtistNames.Split(name).Should().Equal(name);
    }
}
