using System.Collections.Concurrent;
using System.Globalization;

internal enum CardBrand
{
    Unknown,
    Visa,
    Mastercard,
    Amex,
}

/// <summary>
/// A card kept in a sandbox wallet: brand, last four digits and expiry only, never the full number or CVC.
/// <paramref name="DeclineReason"/> is how the sandbox will answer when it is charged (null = approved).
/// </summary>
internal sealed record SavedCard(string Id, CardBrand Brand, string Last4, int ExpiryMonth, int ExpiryYear, string Holder, string? DeclineReason)
{
    public string Expiry => $"{ExpiryMonth:00}/{ExpiryYear % 100:00}";
}

/// <summary>A card typed into the checkout, after validation.</summary>
internal sealed record EnteredCard(CardBrand Brand, string Last4, int ExpiryMonth, int ExpiryYear, string Holder, string? DeclineReason);

/// <summary>
/// The sandbox's test cards, like a real provider's: specific numbers trigger specific answers, any other number that
/// passes the Luhn check is approved. Nothing here can charge a real card.
/// </summary>
internal static class TestCards
{
    public const string Approved = "4242 4242 4242 4242";

    public static readonly IReadOnlyList<(string Number, string Result)> Documented =
    [
        ("4242 4242 4242 4242", "Approved (Visa)"),
        ("5555 5555 5555 4444", "Approved (Mastercard)"),
        ("4000 0000 0000 0002", "Declined: card declined"),
        ("4000 0000 0000 9995", "Declined: insufficient funds"),
        ("4000 0000 0000 0069", "Declined: expired card"),
    ];

    private static readonly Dictionary<string, string> Declines = new()
    {
        ["4000000000000002"] = "Card declined (sandbox)",
        ["4000000000009995"] = "Insufficient funds (sandbox)",
        ["4000000000000069"] = "Expired card (sandbox)",
    };

    public static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    public static CardBrand BrandOf(string digits) => digits switch
    {
        ['4', ..] => CardBrand.Visa,
        ['3', '4' or '7', ..] => CardBrand.Amex,
        ['5', >= '1' and <= '5', ..] => CardBrand.Mastercard,
        ['2', ..] when digits.Length >= 4 && int.Parse(digits[..4], CultureInfo.InvariantCulture) is >= 2221 and <= 2720 => CardBrand.Mastercard,
        _ => CardBrand.Unknown,
    };

    public static bool PassesLuhn(string digits)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[digits.Length - 1 - i] - '0';
            if (i % 2 == 1)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        return digits.Length >= 12 && sum % 10 == 0;
    }

    /// <summary>Checks the form fields; returns the card, or the errors keyed by field name.</summary>
    public static (EnteredCard? Card, Dictionary<string, string> Errors) Validate(
        string? number, string? expiry, string? cvc, string? holder, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string>();
        var digits = Digits(number);
        var brand = BrandOf(digits);
        if (digits.Length is < 12 or > 19 || !PassesLuhn(digits))
        {
            errors["number"] = "That card number isn't valid. Try a test card from the list below.";
        }

        var (month, year) = ParseExpiry(expiry);
        if (month is null || year is null)
        {
            errors["expiry"] = "Use MM / YY, e.g. 12 / 30.";
        }
        else if (new DateOnly(year.Value, month.Value, 1).AddMonths(1) <= DateOnly.FromDateTime(now.UtcDateTime))
        {
            errors["expiry"] = "This card has expired.";
        }

        var cvcDigits = Digits(cvc);
        if (cvcDigits.Length != (brand == CardBrand.Amex ? 4 : 3) || cvcDigits.Length != (cvc ?? string.Empty).Trim().Length)
        {
            errors["cvc"] = brand == CardBrand.Amex ? "4 digits on the front of the card." : "3 digits on the back of the card.";
        }

        if (string.IsNullOrWhiteSpace(holder) || holder.Trim().Length > 60)
        {
            errors["holder"] = "Enter the name on the card.";
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        return (new EnteredCard(brand, digits[^4..], month!.Value, year!.Value, holder!.Trim(), Declines.GetValueOrDefault(digits)), errors);
    }

    private static (int? Month, int? Year) ParseExpiry(string? value)
    {
        var digits = Digits(value);
        if (digits.Length is not (4 or 6))
        {
            return (null, null);
        }

        var month = int.Parse(digits[..2], CultureInfo.InvariantCulture);
        var year = int.Parse(digits[2..], CultureInfo.InvariantCulture);
        year = digits.Length == 4 ? 2000 + year : year;
        return month is >= 1 and <= 12 ? (month, year) : (null, null);
    }
}

/// <summary>
/// Saved cards per browser (a wallet cookie), in memory like the rest of the sandbox. A new wallet starts with demo
/// cards, so the checkout looks like a returning customer's.
/// </summary>
internal sealed class Wallets
{
    public const string CookieName = "sbx_wallet";

    private readonly ConcurrentDictionary<string, List<SavedCard>> _wallets = new();

    public IReadOnlyList<SavedCard> Get(string walletId)
    {
        var cards = _wallets.GetOrAdd(walletId, _ => DemoCards());
        lock (cards)
        {
            return [.. cards];
        }
    }

    public void Add(string walletId, EnteredCard card)
    {
        var cards = _wallets.GetOrAdd(walletId, _ => DemoCards());
        lock (cards)
        {
            if (!cards.Any(c => c.Brand == card.Brand && c.Last4 == card.Last4 && c.ExpiryMonth == card.ExpiryMonth && c.ExpiryYear == card.ExpiryYear))
            {
                cards.Add(new SavedCard($"card_{Guid.NewGuid():N}", card.Brand, card.Last4, card.ExpiryMonth, card.ExpiryYear, card.Holder, card.DeclineReason));
            }
        }
    }

    private static List<SavedCard> DemoCards() =>
    [
        new("card_demo_visa", CardBrand.Visa, "4242", 12, 2029, "Demo Traveler", null),
        new("card_demo_mc", CardBrand.Mastercard, "4444", 8, 2028, "Demo Traveler", null),
        new("card_demo_decline", CardBrand.Visa, "0002", 3, 2030, "Demo Traveler", "Card declined (sandbox)"),
    ];
}
