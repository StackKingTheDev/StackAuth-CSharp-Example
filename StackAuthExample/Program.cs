namespace StackAuthExample;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // One client for the whole app. It signs every request with the app secret and checks the
        // signature on every answer, so a fake "login OK" from a proxy or a patched DNS is rejected.
        var auth = new StackAuth(
            AppConfig.AppId,
            AppConfig.AppSecret,
            version: AppConfig.Version,
            fileHash: Hwid.BuildHash(),
            hwid: Hwid.Get(),
            baseUrl: AppConfig.BaseUrl,
            device: Environment.MachineName);

        // Sign in → home page → (sign out) → sign in again ...
        while (true)
        {
            using (var login = new LoginForm(auth))
            {
                if (login.ShowDialog() != DialogResult.OK) return;
            }
            using var home = new MainForm(auth);
            home.ShowDialog();
            if (!home.SignedOut) return;
        }
    }
}
