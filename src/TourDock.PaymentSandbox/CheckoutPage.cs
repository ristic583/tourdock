using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

/// <summary>What the customer chose or typed, to re-render the page with errors.</summary>
internal sealed record CheckoutForm
{
    public string Method { get; init; } = "saved";

    public string? SavedCardId { get; init; }

    public string? Number { get; init; }

    public string? Expiry { get; init; }

    public string? Holder { get; init; }

    public bool Save { get; init; } = true;

    public IReadOnlyDictionary<string, string> Errors { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// The hosted checkout page of the fictional "Sandbox Pay" provider: order summary, saved cards, a new-card form and the
/// test cards. Plain server-rendered HTML; the new-card form opens with CSS (:has), and a little script only formats
/// the number and expiry as they're typed.
/// </summary>
internal static class CheckoutPage
{
    public static string Render(SandboxPayment payment, IReadOnlyList<SavedCard> cards, CheckoutForm form)
    {
        var amount = $"{payment.Amount.ToString("0.00", CultureInfo.InvariantCulture)} {payment.Currency}";
        var selected = form.Method == "new" ? null : form.SavedCardId ?? cards.FirstOrDefault(c => c.DeclineReason is null)?.Id;
        var body = new StringBuilder();

        body.Append($"""
            <header class="bar"><span class="logo">&#9670; Sandbox Pay</span><span class="test">Test mode</span></header>
            <main class="wrap">
              <section class="summary" aria-label="Order summary">
                <p class="eyebrow">Pay TourDock</p>
                <p class="amount">{H(amount)}</p>
                <p class="muted">{H(payment.Description)}</p>
                <ul class="facts">
                  <li><span>Merchant</span><strong>TourDock</strong></li>
                  <li><span>Payment</span><strong>{H(payment.Id[..12])}…</strong></li>
                </ul>
                <p class="note">You'll go back to TourDock after paying. Sandbox only: no real money moves.</p>
              </section>
              <section class="pay" aria-label="Payment">
                <form method="post" action="/checkout/{H(payment.Id)}/complete" autocomplete="off">
                  <h1>Pay with</h1>
                  {(form.Errors.TryGetValue("card", out var cardError) ? $"<p class=\"error\" role=\"alert\">{H(cardError)}</p>" : string.Empty)}
                  <fieldset class="methods"><legend class="sr">Payment method</legend>
            """);

        foreach (var card in cards)
        {
            var id = $"card-{H(card.Id)}";
            body.Append($"""
                <label class="method" for="{id}">
                  <input type="radio" id="{id}" name="card" value="{H(card.Id)}" {(card.Id == selected ? "checked" : string.Empty)}>
                  {Badge(card.Brand)}
                  <span class="grow"><strong>{BrandName(card.Brand)} &bull;&bull;&bull;&bull; {H(card.Last4)}</strong>
                    <span class="muted small">Expires {H(card.Expiry)}{(card.DeclineReason is null ? string.Empty : " &middot; test card that always declines")}</span></span>
                </label>
                """);
        }

        body.Append($"""
                <label class="method" for="card-new">
                  <input type="radio" id="card-new" name="card" value="new" {(selected is null ? "checked" : string.Empty)}>
                  <span class="badge plus" aria-hidden="true">+</span>
                  <span class="grow"><strong>Add a new card</strong></span>
                </label>
                </fieldset>
                <div class="newcard">
                  {Field("number", "Card number", form.Number, form.Errors, "inputmode=\"numeric\" placeholder=\"1234 1234 1234 1234\" maxlength=\"23\"", "wide")}
                  {Field("expiry", "Expiry", form.Expiry, form.Errors, "inputmode=\"numeric\" placeholder=\"MM / YY\" maxlength=\"7\"", string.Empty)}
                  {Field("cvc", "CVC", null, form.Errors, "inputmode=\"numeric\" placeholder=\"123\" maxlength=\"4\"", string.Empty)}
                  {Field("holder", "Name on card", form.Holder, form.Errors, "placeholder=\"Ana Horvat\" maxlength=\"60\"", "wide")}
                  <label class="check wide"><input type="checkbox" name="save" value="on" {(form.Save ? "checked" : string.Empty)}> Save this card for next time</label>
                </div>
                <button class="pay-button" type="submit" name="action" value="pay">Pay {H(amount)}</button>
                <button class="cancel" type="submit" name="action" value="cancel" formnovalidate>Cancel and return to TourDock</button>
                </form>
                <details class="testcards"><summary>Test cards</summary>
                  <p class="muted small">Any future expiry date and any 3-digit CVC. Other valid numbers are approved.</p>
                  <table>{string.Concat(TestCards.Documented.Select(t => $"<tr><td><code>{t.Number}</code></td><td>{t.Result}</td></tr>"))}</table>
                </details>
              </section>
            </main>
            <footer class="foot">Sandbox Pay is a stand-in provider for the TourDock demo. Never enter a real card number.</footer>
            """);

        return Layout("Pay TourDock · Sandbox Pay", body.ToString());
    }

    public static string Result(string status) => Layout("Payment " + status, $"""
        <header class="bar"><span class="logo">&#9670; Sandbox Pay</span><span class="test">Test mode</span></header>
        <main class="wrap single"><section class="pay"><h1>{(status == "succeeded" ? "Payment received" : status == "cancelled" ? "Payment cancelled" : "Payment declined")}</h1>
        <p class="muted">You can close this page and return to TourDock.</p></section></main>
        """);

    /// <summary>
    /// The bank confirmation step: a spinner for about two seconds, then a green check or a red cross, then back to the
    /// merchant about a second later. Works without JavaScript (meta refresh and a "Return now" link); honours reduced motion.
    /// </summary>
    public static string Authenticate(SandboxPayment payment, string returnUrl)
    {
        var approved = payment.Status == "succeeded";
        var amount = $"{payment.Amount.ToString("0.00", CultureInfo.InvariantCulture)} {payment.Currency}";
        var title = approved ? "Payment approved" : "Declined by your bank";
        var detail = approved ? "Taking you back to TourDock…" : "No money was taken. Taking you back to TourDock to try another card…";
        return Layout("Confirming with your bank · Sandbox Pay", $$"""
            <header class="bar"><span class="logo">&#9670; Sandbox Pay</span><span class="test">Test mode</span></header>
            <main class="wrap single">
              <section class="pay secure" data-outcome="{{(approved ? "approved" : "declined")}}" data-return="{{H(returnUrl)}}">
                <p class="eyebrow">Secure check &middot; {{H(amount)}}</p>
                <div class="seal" aria-hidden="true"><span class="spinner"></span><span class="mark ok">&#10003;</span><span class="mark no">&#10005;</span></div>
                <div role="status" aria-live="polite">
                  <h1 class="step-wait">Confirming with your bank…</h1>
                  <p class="muted step-wait">This takes a moment. Don't close this page.</p>
                  <h1 class="step-done">{{title}}</h1>
                  <p class="muted step-done">{{detail}}</p>
                </div>
                <a class="return" href="{{H(returnUrl)}}">Return now</a>
                <noscript><meta http-equiv="refresh" content="3;url={{H(returnUrl)}}"><style>.step-wait{display:none}.step-done,.mark{display:block}.spinner{display:none}.secure[data-outcome=approved] .no,.secure[data-outcome=declined] .ok{display:none}</style></noscript>
              </section>
            </main>
            <script>
            (function () {
              var box = document.querySelector(".secure");
              setTimeout(function () {
                box.classList.add("done");
                setTimeout(function () { window.location.replace(box.dataset.return); }, 1200);
              }, 2000);
            })();
            </script>
            """);
    }

    private static string Field(string name, string label, string? value, IReadOnlyDictionary<string, string> errors, string attributes, string css)
    {
        var error = errors.GetValueOrDefault(name);
        var described = error is null ? string.Empty : $" aria-invalid=\"true\" aria-describedby=\"{name}-error\"";
        return $"""
            <div class="field {css}"><label for="{name}">{label}</label>
              <input id="{name}" name="{name}" value="{H(value ?? string.Empty)}" {attributes}{described}>
              {(error is null ? string.Empty : $"<p id=\"{name}-error\" class=\"error\">{H(error)}</p>")}
            </div>
            """;
    }

    private static string BrandName(CardBrand brand) => brand switch
    {
        CardBrand.Visa => "Visa",
        CardBrand.Mastercard => "Mastercard",
        CardBrand.Amex => "Amex",
        _ => "Card",
    };

    private static string Badge(CardBrand brand) => brand switch
    {
        CardBrand.Visa => """<span class="badge visa" aria-hidden="true">VISA</span>""",
        CardBrand.Mastercard => """<span class="badge mc" aria-hidden="true"><i></i><i></i></span>""",
        CardBrand.Amex => """<span class="badge amex" aria-hidden="true">AMEX</span>""",
        _ => """<span class="badge plain" aria-hidden="true">&#9636;</span>""",
    };

    private static string H(string value) => HtmlEncoder.Default.Encode(value);

    private static string Layout(string title, string body) => $$"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="robots" content="noindex"><title>{{title}}</title><style>
        *{box-sizing:border-box}body{margin:0;font-family:system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;background:#f3f5f7;color:#16202a}
        .bar{display:flex;align-items:center;gap:12px;padding:14px 24px;background:#16202a;color:#fff}
        .logo{font-weight:700;letter-spacing:.2px}.test{font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.08em;background:#f59e0b;color:#3b2300;padding:3px 8px;border-radius:999px}
        .wrap{max-width:960px;margin:32px auto;padding:0 16px;display:grid;gap:24px;grid-template-columns:minmax(0,1fr)}
        @media(min-width:780px){.wrap{grid-template-columns:minmax(0,5fr) minmax(0,6fr)}.wrap.single{grid-template-columns:minmax(0,1fr);max-width:460px} }
        .summary{padding:8px 4px}.eyebrow{margin:0;color:#5b6773;font-weight:600}.amount{margin:6px 0 4px;font-size:38px;font-weight:750;letter-spacing:-.5px}
        .muted{color:#5b6773}.small{font-size:13px}.facts{list-style:none;padding:0;margin:20px 0;border-top:1px solid #dfe5ea}
        .facts li{display:flex;justify-content:space-between;padding:10px 0;border-bottom:1px solid #dfe5ea;font-size:14px}
        .note{font-size:13px;color:#5b6773}
        .pay{background:#fff;border:1px solid #dfe5ea;border-radius:16px;padding:24px;box-shadow:0 8px 30px rgb(22 32 42 / .06)}
        h1{font-size:20px;margin:0 0 14px}fieldset{border:0;padding:0;margin:0}.sr{position:absolute;left:-9999px}
        .method{display:flex;align-items:center;gap:12px;padding:12px 14px;border:1px solid #dfe5ea;border-radius:12px;margin-bottom:8px;cursor:pointer}
        .method:has(input:checked){border-color:#0a6e6a;box-shadow:0 0 0 1px #0a6e6a;background:#f2faf9}
        .method input{accent-color:#0a6e6a;width:18px;height:18px}.grow{flex:1;display:flex;flex-direction:column}
        .badge{display:inline-flex;align-items:center;justify-content:center;width:46px;height:30px;border-radius:6px;font-size:12px;font-weight:800;flex:none}
        .visa{background:#1a1f71;color:#fff;font-style:italic;letter-spacing:.5px}.amex{background:#2e77bb;color:#fff;font-size:10px}
        .mc{background:#111;gap:0}.mc i{width:16px;height:16px;border-radius:50%;background:#eb001b;display:block}.mc i+i{background:#f79e1b;margin-left:-6px;opacity:.9}
        .plus{background:#eef2f5;color:#16202a;font-size:18px}.plain{background:#eef2f5;color:#5b6773}
        .newcard{display:none;grid-template-columns:1fr 1fr;gap:12px;margin:6px 0 4px;padding:14px;border:1px dashed #cdd6dd;border-radius:12px}
        form:has(#card-new:checked) .newcard{display:grid}.wide{grid-column:1/-1}
        .field label{display:block;font-size:13px;font-weight:600;margin-bottom:6px}
        .field input{width:100%;height:44px;border:1px solid #cdd6dd;border-radius:10px;padding:0 12px;font-size:16px;font-variant-numeric:tabular-nums}
        .field input:focus{outline:2px solid #0a6e6a;border-color:#0a6e6a}.field input[aria-invalid]{border-color:#b42318}
        .error{color:#b42318;font-size:13px;margin:6px 0 0}.check{display:flex;gap:8px;align-items:center;font-size:14px}
        .pay-button{width:100%;margin-top:16px;padding:15px;border:0;border-radius:12px;background:#0a6e6a;color:#fff;font-size:16px;font-weight:700;cursor:pointer}
        .pay-button:hover{background:#085a57}.cancel{width:100%;margin-top:8px;padding:10px;border:0;background:none;color:#5b6773;font-size:14px;cursor:pointer;text-decoration:underline}
        .testcards{margin-top:18px;font-size:14px}.testcards summary{cursor:pointer;font-weight:600}.testcards table{border-collapse:collapse;width:100%;margin-top:6px}
        .testcards td{padding:6px 4px;border-top:1px solid #eef2f5}code{font-size:13px}
        .foot{text-align:center;font-size:12px;color:#5b6773;padding:8px 16px 32px}
        .secure{text-align:center;padding:36px 28px}.secure .eyebrow{font-size:13px;text-transform:uppercase;letter-spacing:.08em}
        .seal{position:relative;width:76px;height:76px;margin:22px auto 18px}
        .spinner{position:absolute;inset:0;border-radius:50%;border:5px solid #dfe5ea;border-top-color:#0a6e6a;animation:spin .9s linear infinite}
        .mark{display:none;position:absolute;inset:0;border-radius:50%;color:#fff;font-size:38px;line-height:76px;font-weight:700;animation:pop .35s ease-out}
        .ok{background:#0a7f4f}.no{background:#b42318}
        .step-done{display:none}.done .step-wait,.done .spinner{display:none}.done .step-done{display:block}
        .done[data-outcome=approved] .ok,.done[data-outcome=declined] .no{display:block}
        .secure h1{font-size:22px;margin:0 0 6px}.return{display:inline-block;margin-top:18px;color:#0a6e6a;font-weight:600;font-size:14px}
        @keyframes spin{to{transform:rotate(360deg)} }@keyframes pop{from{transform:scale(.6);opacity:0}to{transform:scale(1);opacity:1} }
        @media(prefers-reduced-motion:reduce){.spinner{animation-duration:2.4s}.mark{animation:none} }
        </style></head><body>{{body}}
        <script>
        (function () {
          var number = document.getElementById("number"), expiry = document.getElementById("expiry");
          if (number) number.addEventListener("input", function () {
            var d = number.value.replace(/\D/g, "").slice(0, 19), amex = /^3[47]/.test(d);
            number.value = amex ? d.replace(/^(\d{0,4})(\d{0,6})(\d{0,5}).*/, function (_, a, b, c) { return [a, b, c].filter(Boolean).join(" "); })
                                : d.replace(/(\d{4})(?=\d)/g, "$1 ");
          });
          if (expiry) expiry.addEventListener("input", function () {
            var d = expiry.value.replace(/\D/g, "").slice(0, 4);
            expiry.value = d.length > 2 ? d.slice(0, 2) + " / " + d.slice(2) : d;
          });
        })();
        </script></body></html>
        """;
}
