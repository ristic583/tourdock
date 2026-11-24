using AwesomeAssertions;
using TourDock.Application.Catalog;

namespace TourDock.Application.UnitTests;

public sealed class PlaceSearchTests
{
    [Theory]
    [InlineData("Šibenik", "sibenik")]
    [InlineData("ĐAKOVO", "dakovo")]
    [InlineData("  Herceg   Novi ", "herceg novi")]
    [InlineData("Durrës", "durres")]
    [InlineData("Plitvička Jezera", "plitvicka jezera")]
    [InlineData("Ниш", "nis")]
    [InlineData("Београд", "beograd")]
    [InlineData("Чачак", "cacak")]
    [InlineData("Љубљана", "ljubljana")]
    [InlineData("Охрид", "ohrid")]
    public void Names_compare_without_case_accents_or_extra_spaces(string name, string expected) =>
        PlaceSearch.Normalize(name).Should().Be(expected);
}
