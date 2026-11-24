using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TourDock.PaymentSandbox.Tests;

public sealed class SandboxFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "sandbox-tests-api-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Sandbox:ApiKey", ApiKey);
        builder.UseSetting("Sandbox:WebhookSecret", "sandbox-tests-webhook-secret-0123456789");
        builder.UseSetting("Sandbox:WebhookUrl", "http://merchant.invalid/webhooks"); // deliveries fail and retry quietly
        builder.UseSetting("Sandbox:PublicBaseUrl", "http://sandbox.test");
    }
}

/// <summary>The hosted checkout page: saved and new cards, validation, test-card outcomes and the trip back.</summary>
public sealed class CheckoutTests(SandboxFactory factory) : IClassFixture<SandboxFactory>
{
    private const string ReturnUrl = "http://merchant.test/bookings/b1/checkout";

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    // One client per test: it keeps the wallet cookie, and doesn't follow the redirect back to the merchant.
    private HttpClient Browser() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    [Fact]
    public async Task The_page_shows_the_amount_saved_demo_cards_and_the_test_cards()
    {
        var browser = Browser();
        var id = await CreatePaymentAsync();

        var page = await browser.GetAsync($"/checkout/{id}", Ct);
        var html = await page.Content.ReadAsStringAsync(Ct);

        page.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith("sbx_wallet=", StringComparison.Ordinal) && c.Contains("httponly"));
        html.Should().Contain("Pay 80.00 EUR").And.Contain("Visa &bull;&bull;&bull;&bull; 4242").And.Contain("Add a new card")
            .And.Contain("4000 0000 0000 0002").And.Contain("autocomplete=\"off\"");
    }

    [Theory]
    [InlineData("card_demo_visa", "succeeded")]
    [InlineData("card_demo_decline", "failed")]
    public async Task Paying_with_a_saved_card_returns_to_the_merchant_with_the_outcome(string card, string outcome)
    {
        var browser = Browser();
        var id = await CreatePaymentAsync();
        await browser.GetAsync($"/checkout/{id}", Ct);

        var response = await PostAsync(browser, id, new() { ["action"] = "pay", ["card"] = card });

        response.StatusCode.Should().Be(HttpStatusCode.SeeOther);
        response.Headers.Location!.ToString().Should().Be($"/checkout/{id}");
        (await ReturnUrlAsync(browser, response)).Should().Be($"{ReturnUrl}?payment={outcome}");
    }

    [Theory]
    [InlineData("card_demo_visa", "Payment approved", "approved")]
    [InlineData("card_demo_decline", "Declined by your bank", "declined")]
    public async Task The_bank_step_shows_a_spinner_then_the_outcome_and_returns_without_a_code(string card, string title, string outcome)
    {
        var browser = Browser();
        var id = await CreatePaymentAsync();
        await browser.GetAsync($"/checkout/{id}", Ct);
        await PostAsync(browser, id, new() { ["action"] = "pay", ["card"] = card });

        var step = await browser.GetAsync($"/checkout/{id}", Ct);
        var html = await step.Content.ReadAsStringAsync(Ct);

        step.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Confirming with your bank").And.Contain(title).And.Contain($"data-outcome=\"{outcome}\"")
            .And.Contain("Return now").And.Contain("http-equiv=\"refresh\"").And.NotContain("<input");
    }

    [Fact]
    public async Task The_bank_step_stays_on_the_checkout_address_the_proxy_forwards()
    {
        var browser = Browser();
        var id = await CreatePaymentAsync();
        await browser.GetAsync($"/checkout/{id}", Ct);

        var paid = await PostAsync(browser, id, new() { ["action"] = "pay", ["card"] = "card_demo_visa" });

        // The public proxy forwards only ^/checkout/[^/]+(/complete)?$ to the sandbox (deploy/README.md).
        paid.Headers.Location!.ToString().Should().MatchRegex("^/checkout/[^/]+(/complete)?$");
        (await browser.GetAsync($"/checkout/{id}/authenticate", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_new_card_is_validated_and_only_its_last_four_digits_are_kept()
    {
        var browser = Browser();
        var id = await CreatePaymentAsync();
        await browser.GetAsync($"/checkout/{id}", Ct);

        var invalid = await PostAsync(browser, id, new() { ["action"] = "pay", ["card"] = "new", ["number"] = "4242 4242 4242 4241", ["expiry"] = "01 / 20", ["cvc"] = "1", ["holder"] = "" });
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await invalid.Content.ReadAsStringAsync(Ct)).Should().Contain("That card number isn&#x27;t valid").And.Contain("This card has expired.")
            .And.Contain("3 digits on the back of the card.").And.Contain("Enter the name on the card.");

        var paid = await PostAsync(browser, id, new()
        {
            ["action"] = "pay",
            ["card"] = "new",
            ["number"] = "5555 5555 5555 4444",
            ["expiry"] = "12 / 35",
            ["cvc"] = "123",
            ["holder"] = "Ana Horvat",
            ["save"] = "on",
        });
        (await ReturnUrlAsync(browser, paid)).Should().EndWith("payment=succeeded");

        var next = await (await browser.GetAsync($"/checkout/{await CreatePaymentAsync()}", Ct)).Content.ReadAsStringAsync(Ct);
        next.Should().Contain("Mastercard &bull;&bull;&bull;&bull; 4444").And.Contain("Expires 12/35")
            .And.NotContain("5555555555554444").And.NotContain("value=\"5555"); // the full number is neither kept nor echoed back
    }

    [Fact]
    public async Task Test_card_numbers_decide_the_outcome()
    {
        var browser = Browser();
        var id = await CreatePaymentAsync();
        await browser.GetAsync($"/checkout/{id}", Ct);

        var declined = await PostAsync(browser, id, new()
        {
            ["action"] = "pay",
            ["card"] = "new",
            ["number"] = "4000000000009995",
            ["expiry"] = "1235",
            ["cvc"] = "123",
            ["holder"] = "Ana Horvat",
        });

        (await ReturnUrlAsync(browser, declined)).Should().EndWith("payment=failed");
    }

    [Fact]
    public async Task Cancelling_goes_back_and_leaves_the_payment_open()
    {
        var browser = Browser();
        var id = await CreatePaymentAsync();
        await browser.GetAsync($"/checkout/{id}", Ct);

        (await PostAsync(browser, id, new() { ["action"] = "cancel" })).Headers.Location!.ToString().Should().EndWith("payment=cancelled");
        (await ReturnUrlAsync(browser, await PostAsync(browser, id, new() { ["action"] = "pay", ["card"] = "card_demo_visa" }))).Should().EndWith("payment=succeeded");
    }

    [Theory]
    [InlineData("4242424242424242", true)]
    [InlineData("4242424242424241", false)]
    [InlineData("378282246310005", true)]
    public void Luhn(string number, bool valid) => TestCards.PassesLuhn(number).Should().Be(valid);

    [Theory]
    [InlineData("4242", "Visa")]
    [InlineData("5555", "Mastercard")]
    [InlineData("2223", "Mastercard")]
    [InlineData("3782", "Amex")]
    [InlineData("6011", "Unknown")]
    public void Brands(string prefix, string brand) => TestCards.BrandOf(prefix).ToString().Should().Be(brand);

    private async Task<string> CreatePaymentAsync()
    {
        using var api = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/payments")
        {
            Content = JsonContent.Create(new { reference = Guid.NewGuid(), amount = 80m, currency = "EUR", description = "TourDock booking", returnUrl = ReturnUrl }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SandboxFactory.ApiKey);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await api.SendAsync(request, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
    }

    /// <summary>Follows a paid checkout through the bank step and returns where it sends the customer back to.</summary>
    private async Task<string> ReturnUrlAsync(HttpClient browser, HttpResponseMessage paid)
    {
        paid.StatusCode.Should().Be(HttpStatusCode.SeeOther);
        var html = await (await browser.GetAsync(paid.Headers.Location, Ct)).Content.ReadAsStringAsync(Ct);
        var match = System.Text.RegularExpressions.Regex.Match(html, "data-return=\"([^\"]+)\"");
        match.Success.Should().BeTrue();
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private Task<HttpResponseMessage> PostAsync(HttpClient browser, string id, Dictionary<string, string> fields) =>
        browser.PostAsync($"/checkout/{id}/complete", new FormUrlEncodedContent(fields), Ct);
}
