// Assistant eval: asks a running TourDock the questions in cases.json and checks each answer.
//
//   ASSISTANT_EXPOSE_USAGE=true ASSISTANT_PER_MINUTE=100 docker compose up -d     (the API must expose token usage)
//   dotnet run --project tools/TourDock.AssistantEval -- http://localhost:8080 claude-haiku-5-5 [seconds between cases]
//
// A Voyage key without a payment method allows only a few requests a minute; leave ~40 s between cases then, or
// searches fall back to date order and the scores measure the throttling, not the model.
//
// It calls the real model through the API, so it costs a little (prices below, per million tokens). Never run in CI.
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TourDock.AssistantEval;

var baseUrl = args.Length > 0 ? args[0].TrimEnd('/') : "http://localhost:8080";
var model = args.Length > 1 ? args[1] : "claude-haiku-5-5";
var price = Prices.For(model);
var pause = TimeSpan.FromSeconds(args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 0);
var cases = JsonSerializer.Deserialize<List<EvalCase>>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "cases.json")),
    JsonSerializerOptions.Web)!;
using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(60) };
var tours = new TourLookup(http);

var results = new List<EvalResult>();
foreach (var evalCase in cases)
{
    if (results.Count > 0)
    {
        await Task.Delay(pause);
    }

    var result = await Runner.RunAsync(http, tours, evalCase);
    results.Add(result);
    var verdict = result.Passed ? "PASS" : "FAIL";
    Console.WriteLine($"{verdict}  {evalCase.Id,-20} {result.Seconds,5:0.0}s  {string.Join("; ", result.Problems)}");
}

var passed = results.Count(r => r.Passed);
var usage = results.Aggregate(Usage.Zero, (total, r) => total + r.Usage);
var cost = usage.Cost(price);
var report = new StringBuilder()
    .AppendLine(CultureInfo.InvariantCulture, $"Model: {model}")
    .AppendLine(CultureInfo.InvariantCulture, $"Passed: {passed}/{results.Count} ({100.0 * passed / results.Count:0}%)")
    .AppendLine(CultureInfo.InvariantCulture, $"Turns: {results.Sum(r => r.Turns)}, model calls: {usage.ModelCalls}")
    .AppendLine(CultureInfo.InvariantCulture,
        $"Tokens: {usage.Input} input, {usage.Output} output, {usage.CacheRead} cache read, {usage.CacheWrite} cache write")
    .AppendLine(CultureInfo.InvariantCulture, $"Cost: ${cost:0.0000} in total, ${cost / results.Sum(r => r.Turns):0.0000} per message")
    .AppendLine(CultureInfo.InvariantCulture, $"Latency: {results.Average(r => r.Seconds / r.Turns):0.0}s per message on average")
    .ToString();
Console.WriteLine();
Console.Write(report);
return passed == results.Count ? 0 : 1;

namespace TourDock.AssistantEval
{
    internal sealed record EvalCase(
        string Id, List<string> Turns, List<string>? CitesAny, List<string>? Countries, decimal? MaxPrice, string? NotMode, bool NoTours,
        List<string>? ReplyAnyOf, List<string>? ReplyNoneOf);

    internal sealed record EvalResult(bool Passed, List<string> Problems, double Seconds, int Turns, Usage Usage);

    internal sealed record Answer(Guid ConversationId, string Reply, List<CitedTour> Tours);

    internal sealed record CitedTour(Guid Id, string Title);

    internal static class Runner
    {
        public static async Task<EvalResult> RunAsync(HttpClient http, TourLookup tours, EvalCase c)
        {
            var clock = Stopwatch.StartNew();
            var usage = Usage.Zero;
            Answer? reply = null;
            foreach (var turn in c.Turns)
            {
                var body = new { conversationId = reply?.ConversationId, message = turn };
                using var response = await http.PostAsJsonAsync("/api/v1/assistant/messages", body);
                if (!response.IsSuccessStatusCode)
                {
                    return new EvalResult(false, [$"HTTP {(int)response.StatusCode}"], clock.Elapsed.TotalSeconds, c.Turns.Count, usage);
                }

                usage += Usage.Parse(response.Headers.TryGetValues("X-Assistant-Usage", out var values) ? values.First() : null);
                reply = await response.Content.ReadFromJsonAsync<Answer>(JsonSerializerOptions.Web);
            }

            var problems = await Checks.RunAsync(c, reply!, tours);
            return new EvalResult(problems.Count == 0, problems, clock.Elapsed.TotalSeconds, c.Turns.Count, usage);
        }
    }
}
