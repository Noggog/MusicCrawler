using FluentAssertions;
using MusicCrawler.Interfaces;
using Xunit;

namespace MusicCrawler.Tests;

public class ArtistTagTests
{
    [Fact]
    public void Like_and_dislike_get_their_verdict_suffix()
    {
        ArtistTag.For("noggog", DiscoveryStatus.Liked).Should().Be("noggog_liked");
        ArtistTag.For("noggog", DiscoveryStatus.Disliked).Should().Be("noggog_disliked");
    }

    [Fact]
    public void Username_is_lowercased()
    {
        ArtistTag.For("Noggog", DiscoveryStatus.Liked).Should().Be("noggog_liked");
    }

    [Fact]
    public void Email_style_username_trims_to_the_local_part()
    {
        ArtistTag.For("noggog@gmail.com", DiscoveryStatus.Liked).Should().Be("noggog_liked");
    }

    [Fact]
    public void Non_alphanumeric_characters_are_stripped_but_underscores_kept()
    {
        ArtistTag.For("Justin C. Swanson", DiscoveryStatus.Liked).Should().Be("justincswanson_liked");
        ArtistTag.For("a_b-c", DiscoveryStatus.Liked).Should().Be("a_bc_liked");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@example.com")]
    [InlineData("!!!")]
    public void No_usable_username_yields_null_so_the_caller_skips_tagging(string? username)
    {
        ArtistTag.For(username, DiscoveryStatus.Liked).Should().BeNull();
    }

    [Theory]
    [InlineData("noggog_liked")]
    [InlineData("noggog_disliked")]
    [InlineData("NOGGOG_LIKED")]
    public void Managed_tags_are_recognized(string label)
    {
        ArtistTag.IsManaged(label).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Pop/Rock")]
    [InlineData("noggog")]
    [InlineData("favorite")]
    public void Non_managed_labels_are_left_alone(string? label)
    {
        ArtistTag.IsManaged(label).Should().BeFalse();
    }

    [Fact]
    public void What_For_produces_is_recognized_as_managed()
    {
        ArtistTag.IsManaged(ArtistTag.For("noggog", DiscoveryStatus.Liked)).Should().BeTrue();
        ArtistTag.IsManaged(ArtistTag.For("noggog", DiscoveryStatus.Disliked)).Should().BeTrue();
    }
}
