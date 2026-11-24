using AwesomeAssertions;
using NSubstitute;
using TourDock.Application.Abstractions;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Application.Tours;
using TourDock.Application.TravelInfo;
using TourDock.Domain.Tours;

namespace TourDock.Application.UnitTests.TravelInfo;

public sealed class ExchangeRateTests
{
    private static readonly DateOnly RatesDate = new(2026, 11, 6);

    private readonly ITourCatalog _catalog = Substitute.For<ITourCatalog>();
    private readonly IExchangeRateService _rates = Substitute.For<IExchangeRateService>();

    [Theory]
    [InlineData(50.00, 1.0825, 54.13)]   // 54.125 rounds away from zero, like Money
    [InlineData(400.00, 1.0825, 433.00)]
    [InlineData(19.99, 391.47, 7825.49)] // HUF
    [InlineData(0.00, 1.0825, 0.00)]
    public void Converts_and_rounds_to_two_decimals(decimal eur, decimal rate, decimal expected)
    {
        CurrencyConverter.Convert(new MoneyDto(eur, "EUR"), "USD", rate).Should().Be(new MoneyDto(expected, "USD"));
    }

    [Fact]
    public async Task Tour_detail_adds_converted_prices_for_a_supported_currency()
    {
        var tourId = ArrangeTour();
        _rates.GetLatestAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Rates(isStale: true));

        var result = await Handler().Handle(new GetPublicTourQuery(tourId, "USD"), TestContext.Current.CancellationToken);

        result.Value.PricePerPerson.Should().Be(new MoneyDto(50m, "EUR"), "the charged price stays in EUR");
        result.Value.ConvertedPrices.Should().BeEquivalentTo(new ConvertedPrices(
            "USD", 1.0825m, RatesDate, IsStale: true, new MoneyDto(54.13m, "USD"), new MoneyDto(433m, "USD")));
    }

    [Fact]
    public async Task Tour_detail_falls_back_to_eur_only_when_no_rates_are_available()
    {
        var tourId = ArrangeTour();
        _rates.GetLatestAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs((ExchangeRatesSnapshot?)null);

        var result = await Handler().Handle(new GetPublicTourQuery(tourId, "USD"), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.ConvertedPrices.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("EUR")]
    public async Task Tour_detail_does_not_call_the_rate_provider_without_a_foreign_currency(string? currency)
    {
        var tourId = ArrangeTour();

        var result = await Handler().Handle(new GetPublicTourQuery(tourId, currency), TestContext.Current.CancellationToken);

        result.Value.ConvertedPrices.Should().BeNull();
        await _rates.DidNotReceiveWithAnyArgs().GetLatestAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("USD", true)]
    [InlineData("EUR", true)]
    [InlineData("JPY", false)]
    [InlineData("usd", false)] // the endpoint upper-cases; the query itself is strict
    public void Validator_accepts_only_supported_currencies(string currency, bool valid)
    {
        new GetPublicTourValidator().Validate(new GetPublicTourQuery(Guid.NewGuid(), currency)).IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task Exchange_rates_report_unavailable_instead_of_failing()
    {
        _rates.GetLatestAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs((ExchangeRatesSnapshot?)null);

        var result = await new GetExchangeRatesHandler(_rates).Handle(new GetExchangeRatesQuery(), TestContext.Current.CancellationToken);

        result.Value.Should().BeEquivalentTo(new { BaseCurrency = "EUR", Available = false, Date = (DateOnly?)null, Rates = new Dictionary<string, decimal>() });
    }

    [Fact]
    public async Task Exchange_rates_only_expose_supported_currencies()
    {
        var rates = Rates() with { Rates = new Dictionary<string, decimal> { ["USD"] = 1.0825m, ["JPY"] = 165.2m } };
        _rates.GetLatestAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(rates);

        var result = await new GetExchangeRatesHandler(_rates).Handle(new GetExchangeRatesQuery(), TestContext.Current.CancellationToken);

        result.Value.Rates.Should().Equal(new Dictionary<string, decimal> { ["USD"] = 1.0825m });
        result.Value.Date.Should().Be(RatesDate);
    }

    private GetPublicTourHandler Handler() => new(_catalog, _rates);

    private Guid ArrangeTour()
    {
        var tour = new PublicTourResponse(Guid.NewGuid(), "Sunset Cruise", "Coast", "Blue Bay Tours",
            new LocationDto("HR", "Split", "Riva 1", 43.5, 16.44, "Europe/Zagreb"), 180, 10, BookingMode.Both,
            new MoneyDto(50m, "EUR"), new MoneyDto(400m, "EUR"), [], [], null, 0, []);
        _catalog.GetTourAsync(tour.Id, Arg.Any<CancellationToken>()).Returns(tour);
        return tour.Id;
    }

    private static ExchangeRatesSnapshot Rates(bool isStale = false) =>
        new("EUR", RatesDate, new Dictionary<string, decimal> { ["USD"] = 1.0825m, ["GBP"] = 0.8412m }, isStale);
}
