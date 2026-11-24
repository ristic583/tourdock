using AwesomeAssertions;
using TourDock.Application.Catalog;
using TourDock.Domain.Tours;

namespace TourDock.Application.UnitTests;

public sealed class TourDocumentTests
{
    private static readonly TourDocumentInput Tour = new(
        " Sunrise fishing trip ", "Rods and bait included.", "Split", "HR", TimeSpan.FromMinutes(150), BookingMode.Shared);

    [Fact]
    public void Describes_what_the_tour_is_like_not_what_changes_daily()
    {
        var text = TourDocument.Build(Tour);

        text.Should().StartWith("Sunrise fishing trip")
            .And.Contain("Location: Split, Croatia")
            .And.Contain("Duration: 2.5 hours")
            .And.Contain("Shared tour")
            .And.EndWith("Rods and bait included.");
    }

    [Theory]
    [InlineData(45, "45 minutes")]
    [InlineData(180, "3 hours")]
    [InlineData(2 * 24 * 60, "2 days")]
    public void Duration_reads_naturally(int minutes, string expected) =>
        TourDocument.Build(Tour with { Duration = TimeSpan.FromMinutes(minutes) }).Should().Contain($"Duration: {expected}");

    [Fact]
    public void The_hash_changes_with_the_text_or_the_model()
    {
        var text = TourDocument.Build(Tour);
        var hash = TourDocument.Hash("voyage-4", text);

        hash.Should().HaveLength(64).And.Be(TourDocument.Hash("voyage-4", text));
        TourDocument.Hash("voyage-4-large", text).Should().NotBe(hash);
        TourDocument.Hash("voyage-4", text + " ").Should().NotBe(hash);
    }
}
