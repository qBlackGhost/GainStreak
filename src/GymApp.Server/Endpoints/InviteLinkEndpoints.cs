using System.Text.Json;
using GymApp.Server.Friends;
using GymApp.Social;
using QRCoder;

namespace GymApp.Server.Endpoints;

/// <summary>
/// The web side of invite links (Phase 21), no sign-in. A phone with the app installed never gets here: iOS and Android
/// open <c>/add/&lt;code&gt;</c> in the app, as the two <c>.well-known</c> files allow. Without the app, the link lands on
/// <c>/add/&lt;code&gt;</c>, which sends phones to their store and shows everyone else the QR code with both stores.
/// The page never looks the code up, so it says nothing about whose it is.
/// </summary>
public static class InviteLinkEndpoints
{
    public static void MapInviteLinks(this IEndpointRouteBuilder app)
    {
        app.MapGet("/add/{code}", Landing).AllowAnonymous();
        app.MapGet("/.well-known/apple-app-site-association", AppleAppSiteAssociation).AllowAnonymous();
        app.MapGet("/.well-known/assetlinks.json", AssetLinks).AllowAnonymous();
    }

    /// <summary>Which store a browser's user agent belongs to. iPadOS Safari says it's a Mac, so iPads see the page.</summary>
    public enum Store { None, AppStore, Play }

    public static Store StoreFor(string? userAgent) =>
        userAgent is null ? Store.None
        : userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? Store.Play
        : userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
          || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase)
          || userAgent.Contains("iPod", StringComparison.OrdinalIgnoreCase) ? Store.AppStore
        : Store.None;

    private static IResult Landing(string code, HttpRequest request, InviteSettings settings)
    {
        if (FriendInvites.Normalize(code) is not { } normalized) return Results.NotFound();

        var store = StoreFor(request.Headers.UserAgent.ToString()) switch
        {
            Store.Play => settings.PlayStoreUrl,
            Store.AppStore => settings.AppStoreUrl,
            _ => null
        };
        if (store is not null) return Results.Redirect(store);

        var link = settings.LinkFor(normalized, request);
        return Results.Content(Page(link, settings), "text/html; charset=utf-8");
    }

    private static IResult AppleAppSiteAssociation(InviteSettings settings)
    {
        if (settings.AppleTeamId is null) return Results.NotFound();
        var body = new
        {
            applinks = new
            {
                details = new[]
                {
                    new
                    {
                        appIDs = new[] { $"{settings.AppleTeamId}.{InviteSettings.AppId}" },
                        components = new[] { new Dictionary<string, string> { ["/"] = $"{FriendInvites.LinkPath}*" } }
                    }
                }
            }
        };
        return Results.Text(JsonSerializer.Serialize(body), "application/json");
    }

    private static IResult AssetLinks(InviteSettings settings)
    {
        if (settings.AndroidCertFingerprints.Count == 0) return Results.NotFound();
        var body = new[]
        {
            new
            {
                relation = new[] { "delegate_permission/common.handle_all_urls" },
                target = new
                {
                    @namespace = "android_app",
                    package_name = InviteSettings.AppId,
                    sha256_cert_fingerprints = settings.AndroidCertFingerprints
                }
            }
        };
        return Results.Text(JsonSerializer.Serialize(body), "application/json");
    }

    /// <summary>Where the privacy policy and terms live (<c>site/</c>, published with GitHub Pages).</summary>
    public const string PublicPages = "https://qblackghost.github.io/GainStreak-releases/";

    /// <summary>The page for computers (and iPads that say they're a Mac), after the GainStreak Mobile canvas's
    /// "Invite link on a computer": the QR code to scan with a phone, the link, both stores. System fonts and no
    /// outside requests, like <c>site/</c>; dark unless the browser asks for light.</summary>
    private static string Page(string link, InviteSettings settings)
    {
        string qr;
        using (var data = QRCodeGenerator.GenerateQrCode(link, QRCodeGenerator.ECCLevel.M))
            qr = new SvgQRCode(data).GetGraphic(8, "#000000", "#ffffff", drawQuietZones: true, sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);

        static string Html(string v) => System.Net.WebUtility.HtmlEncode(v);
        var shownLink = Html(link.Replace("https://", "").Replace("http://", ""));
        var appStore = settings.AppStoreUrl is { } a
            ? $"""<a class="store" href="{Html(a)}"><span class="small">Download on the</span><span class="big">App Store</span></a>"""
            : """<span class="store soon">App Store: coming soon</span>""";
        var play = $"""<a class="store" href="{Html(settings.PlayStoreUrl)}"><span class="small">Get it on</span><span class="big">Google Play</span></a>""";

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="robots" content="noindex">
            <title>Add a friend on GainStreak</title>
            <style>
              :root { color-scheme: dark; --bg: #000000; --card: #0B0B0B; --line: #222222; --border: #333333; --ink: #FFFFFF; --soft: #B5B5B5;
                      --muted: #9A9A9A; --inv: #FFFFFF; --on-inv: #000000; --accent: #C6F432; --accent-text: #C6F432; --tint: #10140A; --tint-line: #3A4A14; }
              @media (prefers-color-scheme: light) {
                :root { color-scheme: light; --bg: #F4F4F1; --card: #FFFFFF; --line: #E2E2DD; --border: #D0D0CB; --ink: #0B0B0B; --soft: #4A4A47;
                        --muted: #62625E; --inv: #0B0B0B; --on-inv: #FFFFFF; --accent: #4D7C0F; --accent-text: #3F6A0A; --tint: #EEF5E0; --tint-line: #B9D48A; }
              }
              * { box-sizing: border-box; }
              body { margin: 0; min-height: 100vh; display: flex; flex-direction: column; background: var(--bg); color: var(--ink);
                     font: 16px/1.5 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
              header { display: flex; align-items: center; gap: 10px; padding: 20px 24px; border-bottom: 1px solid var(--line); }
              .wordmark { font-weight: 800; font-size: 19px; letter-spacing: 0.04em; }
              .wordmark span { color: var(--accent); }
              main { flex: 1; width: 100%; max-width: 1040px; margin: 0 auto; padding: 64px 24px; display: flex; flex-wrap: wrap;
                     align-items: center; justify-content: center; gap: 56px; }
              .text { flex: 1 1 380px; min-width: 0; max-width: 480px; display: flex; flex-direction: column; gap: 20px; }
              .chip { align-self: flex-start; padding: 6px 12px; border-radius: 999px; background: var(--tint); border: 1px solid var(--tint-line);
                      color: var(--accent-text); font-size: 12px; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; }
              h1 { margin: 0; font-weight: 800; font-size: clamp(32px, 4vw, 48px); line-height: 1.05; letter-spacing: -0.02em; }
              p { margin: 0; font-size: 17px; line-height: 1.55; color: var(--soft); }
              .stores { display: flex; flex-wrap: wrap; gap: 12px; padding-top: 4px; }
              .store { display: flex; flex-direction: column; justify-content: center; min-height: 52px; padding: 0 18px; border-radius: 14px;
                       background: var(--inv); color: var(--on-inv); text-decoration: none; line-height: 1.1; }
              .store .small { font-size: 11px; } .store .big { font-weight: 700; font-size: 16px; }
              .store.soon { background: transparent; border: 1px dashed var(--border); color: var(--muted); font-size: 14px; }
              .note { font-size: 13px; color: var(--muted); }
              figure { flex: 0 1 320px; margin: 0; display: flex; flex-direction: column; align-items: center; gap: 14px; }
              .qr { width: 300px; max-width: 100%; padding: 18px; border-radius: 24px; background: #FFFFFF; box-shadow: 0 20px 60px rgba(0, 0, 0, 0.25); }
              .qr svg { display: block; width: 100%; height: auto; }
              figcaption { font-family: ui-monospace, "SF Mono", Menlo, monospace; font-size: 13px; color: var(--muted); word-break: break-all; text-align: center; }
              footer { display: flex; flex-wrap: wrap; justify-content: center; gap: 20px; padding: 20px 24px 28px; border-top: 1px solid var(--line); font-size: 13px; }
              footer a { color: var(--muted); }
            </style>
            </head>
            <body>
            <header><span class="wordmark">GAIN<span>STREAK</span></span></header>
            <main>
              <div class="text">
                <span class="chip">Friend invite</span>
                <h1>You've been invited to GainStreak</h1>
                <p>Scan the code with your phone's camera to add your friend. Get the app first if you don't have it, then scan again.</p>
                <div class="stores">{{appStore}}{{play}}</div>
                <span class="note">On your phone already? Open this link there instead: it goes straight to the app, or to the store if you don't have it yet.</span>
              </div>
              <figure>
                <div class="qr" role="img" aria-label="QR code for this invite link">{{qr}}</div>
                <figcaption>{{shownLink}}</figcaption>
              </figure>
            </main>
            <footer><a href="{{PublicPages}}privacy.html">Privacy</a><a href="{{PublicPages}}terms.html">Terms</a></footer>
            </body>
            </html>
            """;
    }
}
