using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace TourDock.AssistantEval;

/// <summary>What a good answer must do. Every check that fails adds a short reason.</summary>
internal static class Checks
{
    public static async Task<List<string>> RunAsync(EvalCase c, Answer reply, TourLookup tours)
    {
        var problems = new List<string>();
        var titles = reply.Tours.Select(t => t.Title.ToLowerInvariant()).ToList();
        var text = reply.Reply;

        if (c.NoTours && reply.Tours.Count > 0)
        {
            problems.Add($"cited tours but shouldn't: {string.Join(", ", reply.Tours.Select(t => t.Title))}");
        }

        if (c.CitesAny is { } wanted && !titles.Any(t => wanted.Any(w => t.Contains(w, StringComparison.OrdinalIgnoreCase))))
        {
            var cited = string.Join(", ", reply.Tours.Select(t => t.Title));
            problems.Add($"no cited tour matches [{string.Join(", ", wanted)}]; cited: [{cited}]");
        }

        if (c.ReplyAnyOf is { Count: > 0 } any && !any.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add($"reply has none of [{string.Join(", ", any)}]");
        }

        problems.AddRange((c.ReplyNoneOf ?? [])
            .Where(p => text.Contains(p, StringComparison.OrdinalIgnoreCase))
            .Select(p => $"reply contains \"{p.Trim()}\""));

        foreach (var cited in reply.Tours)
        {
            var tour = await tours.GetAsync(cited.Id);
            if (c.Countries is { } countries && !countries.Contains(tour.CountryCode))
            {
                problems.Add($"{cited.Title} is in {tour.CountryCode}, not {string.Join("/", countries)}");
            }

            if (c.MaxPrice is { } max && tour.PricePerPerson is { } p && p > max)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"{cited.Title} costs {p} per person, over {max}"));
            }

            if (c.NotMode is { } mode && tour.BookingMode == mode)
            {
                problems.Add($"{cited.Title} is {mode} only");
            }
        }

        return problems;
    }
}

internal sealed record TourFacts(string CountryCode, decimal? PricePerPerson, string BookingMode);

/// <summary>Public tour details, to check what the answer cites (country, price, booking type).</summary>
internal sealed class TourLookup(HttpClient http)
{
    private readonly Dictionary<Guid, TourFacts> _cache = [];

    public async Task<TourFacts> GetAsync(Guid id)
    {
        if (!_cache.TryGetValue(id, out var facts))
        {
            var tour = await http.GetFromJsonAsync<JsonElement>($"/api/v1/tours/{id}");
            var price = tour.GetProperty("pricePerPerson");
            facts = new TourFacts(
                tour.GetProperty("location").GetProperty("countryCode").GetString()!,
                price.ValueKind == JsonValueKind.Object ? price.GetProperty("amount").GetDecimal() : null,
                tour.GetProperty("bookingMode").GetString()!);
            _cache[id] = facts;
        }

        return facts;
    }
}

/// <summary>Price per million tokens (Claude API list prices, prompts up to 100K tokens).</summary>
internal sealed record Prices(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite)
{
    public static Prices For(string model) => model switch
    {
        "claude-opus-5-5" => new(4.00m, 20.00m, 0.20m, 5.00m),
        "claude-sonnet-5-5" => new(2.00m, 10.00m, 0.10m, 2.50m),
        _ => new(0.10m, 0.50m, 0.01m, 0.125m), // claude-haiku-5-5
    };
}

internal sealed record Usage(int ModelCalls, long Input, long Output, long CacheRead, long CacheWrite)
{
    public static readonly Usage Zero = new(0, 0, 0, 0, 0);

    public static Usage operator +(Usage a, Usage b) =>
        new(a.ModelCalls + b.ModelCalls, a.Input + b.Input, a.Output + b.Output, a.CacheRead + b.CacheRead, a.CacheWrite + b.CacheWrite);

    public decimal Cost(Prices p) =>
        (Input * p.Input + Output * p.Output + CacheRead * p.CacheRead + CacheWrite * p.CacheWrite) / 1_000_000m;

    /// <summary>"model_calls=2;input=..;output=..;cache_read=..;cache_write=.." from the X-Assistant-Usage header.</summary>
    public static Usage Parse(string? header)
    {
        if (header is null)
        {
            return Zero;
        }

        var parts = header.Split(';').Select(p => p.Split('='))
            .ToDictionary(p => p[0], p => long.Parse(p[1], CultureInfo.InvariantCulture));
        return new((int)parts["model_calls"], parts["input"], parts["output"], parts["cache_read"], parts["cache_write"]);
    }
}
