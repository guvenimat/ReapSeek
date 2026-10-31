using System.Net;

namespace ReapSeek.Sandbox.Payments;

public static class SandboxPages
{
    const string Styles = """
        <style>
        *{box-sizing:border-box}body{margin:0;font-family:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;background:#eef1f6;color:#1b2333}
        .shell{max-width:440px;margin:48px auto;padding:0 16px}
        .bank{display:flex;align-items:center;gap:10px;margin-bottom:16px;font-weight:700;color:#0b3d91}
        .bank span{display:inline-grid;place-items:center;width:32px;height:32px;border-radius:8px;background:#0b3d91;color:#fff;font-size:14px}
        .card{background:#fff;border-radius:14px;box-shadow:0 10px 30px rgba(15,30,60,.12);padding:24px}
        h1{font-size:20px;margin:0 0 4px}.muted{color:#5d6679;font-size:14px;margin:0}
        .amount{font-size:28px;font-weight:800;margin:16px 0;color:#0b3d91}
        label{display:grid;gap:6px;font-size:13px;font-weight:600;margin-top:14px}
        input{width:100%;min-width:0;box-sizing:border-box;height:44px;padding:0 12px;border:1.5px solid #c9d0dd;border-radius:8px;font-size:16px}
        input:focus{outline:none;border-color:#0b3d91;box-shadow:0 0 0 3px #dbe5f8}
        .row{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:12px}
        button{width:100%;height:46px;margin-top:20px;border:0;border-radius:8px;background:#0b3d91;color:#fff;font-size:16px;font-weight:700;cursor:pointer}
        button.secondary{background:#fff;color:#0b3d91;border:1.5px solid #c9d0dd;margin-top:10px}
        .error{margin-top:14px;padding:10px 12px;border-radius:8px;background:#fdecee;color:#b3261e;font-size:14px}
        .hint{margin-top:18px;padding:12px;border-radius:8px;background:#f5f7fb;font-size:12px;color:#5d6679;line-height:1.6}
        .status{display:inline-block;padding:4px 10px;border-radius:999px;font-weight:700;font-size:13px}
        .ok{background:#e3f4ea;color:#1a7f4b}.bad{background:#fdecee;color:#b3261e}
        a.button{display:block;text-align:center;line-height:46px;text-decoration:none;margin-top:20px;border-radius:8px;background:#0b3d91;color:#fff;font-weight:700}
        footer{margin-top:16px;text-align:center;font-size:12px;color:#7a8396}
        </style>
        """;

    static string Page(string title, string body) => $"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="robots" content="noindex"><title>{WebUtility.HtmlEncode(title)}</title>{Styles}</head>
        <body><main class="shell"><div class="bank"><span>SB</span>Sandbox Bank</div>{body}
        <footer>Simulated payment provider. No real card is charged.</footer></main></body></html>
        """;

    public static string CardForm(PaymentRecord p, string? error, string? holder) => Page("Pay securely", $"""
        <section class="card" data-testid="card-form">
          <h1>Pay securely</h1>
          <p class="muted">{WebUtility.HtmlEncode(p.Description)}</p>
          <div class="amount">{SandboxEndpoints.Money(p.Amount, p.Currency)}</div>
          <form method="post" action="/pay/{p.Id}" autocomplete="off">
            <label>Name on card<input name="holder" required maxlength="100" value="{WebUtility.HtmlEncode(holder ?? "")}"></label>
            <label>Card number<input name="card_number" required inputmode="numeric" maxlength="23" placeholder="4242 4242 4242 4242"></label>
            <div class="row">
              <label>Expiry<input name="expiry" required placeholder="MM/YY" maxlength="7"></label>
              <label>Security code<input name="cvc" required inputmode="numeric" maxlength="4" placeholder="123"></label>
            </div>
            {(error is null ? "" : $"<p class=\"error\" role=\"alert\">{WebUtility.HtmlEncode(error)}</p>")}
            <button type="submit">Pay {SandboxEndpoints.Money(p.Amount, p.Currency)}</button>
          </form>
          <div class="hint">Test cards: <b>4242 4242 4242 4242</b> succeeds after 3-D Secure (code <b>{CardRules.AuthenticationCode}</b>),
            <b>4000 0000 0000 0002</b> is declined, <b>4000 0000 0000 9995</b> has insufficient funds. Use any future expiry and any 3 digits.</div>
        </section>
        """);

    public static string Authentication(PaymentRecord p, string? error) => Page("3-D Secure", $"""
        <section class="card" data-testid="three-ds">
          <h1>Confirm this payment</h1>
          <p class="muted">Your bank sent a code to the phone number on file for the {WebUtility.HtmlEncode(p.CardBrand)} card ending in {WebUtility.HtmlEncode(p.CardLast4)}.</p>
          <div class="amount">{SandboxEndpoints.Money(p.Amount, p.Currency)}</div>
          <form method="post" action="/pay/{p.Id}/3ds" autocomplete="off">
            <label>Verification code<input name="code" inputmode="numeric" maxlength="6" placeholder="{CardRules.AuthenticationCode}" autofocus></label>
            {(error is null ? "" : $"<p class=\"error\" role=\"alert\">{WebUtility.HtmlEncode(error)}</p>")}
            <button type="submit" name="action" value="confirm">Confirm</button>
            <button type="submit" name="action" value="cancel" class="secondary" formnovalidate>Cancel payment</button>
          </form>
        </section>
        """);

    public static string Result(PaymentRecord p)
    {
        var ok = p.Status == PaymentStatus.Succeeded;
        var message = ok ? "Payment approved" : p.FailureReason switch
        {
            "card_declined" => "Your card was declined",
            "insufficient_funds" => "Insufficient funds",
            "authentication_failed" => "Verification failed",
            "authentication_cancelled" => "Payment cancelled",
            _ => "Payment failed",
        };
        return Page(message, $"""
            <section class="card" data-testid="payment-result">
              <span class="status {(ok ? "ok" : "bad")}">{WebUtility.HtmlEncode(p.Status)}</span>
              <h1 style="margin-top:12px">{WebUtility.HtmlEncode(message)}</h1>
              <p class="muted">{WebUtility.HtmlEncode(p.Description)} · {SandboxEndpoints.Money(p.Amount, p.Currency)}</p>
              <a class="button" href="{WebUtility.HtmlEncode(SandboxEndpoints.ReturnUrl(p))}">Return to the store</a>
            </section>
            """);
    }

    public static string NotFound() => Page("Payment not found", """
        <section class="card"><h1>Payment not found</h1><p class="muted">This payment link is not valid or has expired.</p></section>
        """);
}
