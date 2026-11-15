namespace StackAuthExample;

/// <summary>
/// Everything you change to connect this example to YOUR application.
/// Get the values from https://auth.stackworkshop.com/dashboard → your app → Overview.
///
/// The defaults connect to the public "StackWorkShop" demo application, so the example works
/// the moment you press Start. That demo secret is public on purpose. Your own app secret ships
/// inside your build, so protect your build (obfuscation, the startup hash check, the dependency
/// manifest) and rotate the secret from the dashboard if it ever leaks.
/// </summary>
internal static class AppConfig
{
    public const string AppId = "7770843c930bbead";
    public const string AppSecret = "sk_VHVv_EtoXxz6K_OTLgOgVJ95KUBEdqL3jnucjEvtMbQ";

    /// <summary>Must match "Current version" in Settings → Version &amp; updates, or init() reports an update.</summary>
    public const string Version = "1.0.0";

    public const string BaseUrl = "https://auth.stackworkshop.com";

    // ---- Demo-only helpers. Delete these when you switch to your own app.

    /// <summary>Shared demo logins on the StackWorkShop demo app (anyone can use them, nobody can change them).</summary>
    public static readonly (string Label, string Username, string Password)[] DemoLogins =
    {
        ("Demo member", "demo_member", "DemoMember!2026"),
        ("Demo admin", "demo_admin", "DemoAdmin!2026"),
    };

    /// <summary>Variables that exist on the demo app (Variables tab in the dashboard).</summary>
    public static readonly string[] Variables = { "welcome_message", "website_url", "latest_news", "vip_message", "api_endpoint" };

    /// <summary>A secured variable with Fake mode on: clients without a valid login get its fake value.</summary>
    public const string FakeModeVariable = "api_endpoint";

    /// <summary>A protected file on the demo app (Files tab), also with a fake twin.</summary>
    public const string DemoFile = "demo-plugin.txt";
}
