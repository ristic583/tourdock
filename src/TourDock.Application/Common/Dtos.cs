using TourDock.Domain.Common;

namespace TourDock.Application.Common;

public sealed record MoneyDto(decimal Amount, string Currency);

public static class DtoMapping
{
    public static MoneyDto? ToDto(this Money? money) => money is null ? null : new MoneyDto(money.Amount, money.Currency);

    public static Money? ToMoney(this decimal? amount) => amount is null ? null : Money.Eur(amount.Value);
}
