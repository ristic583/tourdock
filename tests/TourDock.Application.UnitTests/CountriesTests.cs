using AwesomeAssertions;
using TourDock.Application.Catalog;

namespace TourDock.Application.UnitTests;

public sealed class CountriesTests
{
    [Theory]
    [InlineData("ser", "RS")]
    [InlineData("SRB", "RS")]
    [InlineData("Србија", "RS")]
    [InlineData("crna g", "ME")]
    [InlineData("hrv", "HR")]
    [InlineData("espa", "ES")]
    [InlineData("maced", "MK")]
    [InlineData("shqip", "AL")]
    public void Matches_english_and_local_names_ignoring_case_accents_and_script(string typed, string expected) =>
        Countries.Matching(typed).Select(c => c.Code).Should().Contain(expected);

    [Fact]
    public void Ignores_a_single_letter_and_unknown_text()
    {
        Countries.Matching("s").Should().BeEmpty();
        Countries.Matching("qq").Should().BeEmpty();
    }

    [Fact]
    public void Puts_an_exact_name_before_longer_ones()
    {
        // "italia" is Italy's whole local name; nothing else starts with it.
        Countries.Matching("italia").Select(c => c.Code).Should().Equal("IT");
        Countries.Matching("sl").Select(c => c.Code).Should().Equal("SI");
    }

    [Theory]
    [InlineData("Serbia", "RS")]
    [InlineData("  srbija ", "RS")]
    [InlineData("Bosnia and Herzegovina", "BA")]
    [InlineData("Norge", "NO")]
    public void Names_a_country_only_by_a_whole_name(string typed, string expected)
    {
        Countries.Named(typed)!.Code.Should().Be(expected);
        Countries.Named(typed[..^2]).Should().BeNull();
    }
}
