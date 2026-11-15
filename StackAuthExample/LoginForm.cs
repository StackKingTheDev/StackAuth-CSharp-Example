using System.Diagnostics;
using System.Text.Json;

namespace StackAuthExample;

/// <summary>
/// Startup and sign-in: init() first (status, version, announcement), then Sign in, Register or License key.
/// </summary>
internal sealed class LoginForm : Form
{
    private readonly StackAuth _auth;
    private readonly Label _title = Ui.Text("StackWorkShop Auth", 15, true, Color.White);
    private readonly Label _subtitle = Ui.Text("Connecting…", 9.5f, false, Color.FromArgb(199, 210, 254));
    private readonly Label _announcement = Ui.Text("", 9.5f, false, Color.FromArgb(30, 41, 59), 420);
    private readonly Label _status = Ui.Text("", 9.5f, false, Ui.Muted, 420);
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Font = Ui.Body };

    // Sign in
    private readonly TextBox _username = Ui.Input("Username");
    private readonly TextBox _password = Ui.Input("Password", password: true);
    private readonly CheckBox _remember = new() { Text = "Remember me on this PC", AutoSize = true, Checked = true, Margin = new Padding(0, 2, 0, 6) };
    // Register
    private readonly TextBox _regUsername = Ui.Input("Username (3-32 letters, digits, _ . -)");
    private readonly TextBox _regEmail = Ui.Input("Email (optional)");
    private readonly TextBox _regPassword = Ui.Input("Password (8+ characters)", password: true);
    private readonly TextBox _regLicense = Ui.Input("License key (optional in free mode)");
    // License key
    private readonly TextBox _licenseKey = Ui.Input("XXXXX-XXXXX-XXXXX-XXXXX");

    private readonly List<Control> _busyControls = new();

    public LoginForm(StackAuth auth)
    {
        _auth = auth;
        Text = "Sign in · StackWorkShop Auth example";
        ClientSize = new Size(470, 624);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = Ui.Body;
        BackColor = Color.White;

        var header = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Ui.Header, Padding = new Padding(18, 12, 18, 8) };
        var headerText = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, BackColor = Ui.Header };
        headerText.Controls.Add(_title);
        headerText.Controls.Add(_subtitle);
        header.Controls.Add(headerText);

        var announcementPanel = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Color.FromArgb(238, 242, 255), Padding = new Padding(18, 8, 18, 8) };
        announcementPanel.Controls.Add(_announcement);
        _announcement.AutoSize = false;
        _announcement.MaximumSize = Size.Empty;
        _announcement.Dock = DockStyle.Fill;

        _tabs.TabPages.Add(Page("Sign in", SignInPage()));
        _tabs.TabPages.Add(Page("Register", RegisterPage()));
        _tabs.TabPages.Add(Page("License key", LicensePage()));

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 132, Padding = new Padding(18, 6, 18, 8), BackColor = Ui.Surface };
        var footerColumn = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Ui.Surface };
        footerColumn.Controls.Add(Ui.Text("Try the demo (shared accounts, read-only):", 9, true, Ui.Muted));
        var demoRow = Ui.Row();
        foreach (var demo in AppConfig.DemoLogins)
        {
            var button = Ui.Button($"Sign in as {demo.Label}", async (_, _) => await SignInAsync(demo.Username, demo.Password));
            demoRow.Controls.Add(button);
            _busyControls.Add(button);
        }
        footerColumn.Controls.Add(demoRow);
        footerColumn.Controls.Add(_status);
        footer.Controls.Add(footerColumn);

        Controls.Add(_tabs);
        Controls.Add(footer);
        Controls.Add(announcementPanel);
        Controls.Add(header);
        SetBusy(true);
        Load += async (_, _) => await StartAsync();
    }

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { BackColor = Color.White };
        page.Controls.Add(content);
        return page;
    }

    private Control SignInPage()
    {
        var button = Ui.Button("Sign in", async (_, _) => await SignInAsync(_username.Text.Trim(), _password.Text), primary: true, width: 320);
        _busyControls.Add(button);
        AcceptButton = button;
        return Ui.Column(Ui.Text("Username"), _username, Ui.Text("Password"), _password, _remember, button);
    }

    private Control RegisterPage()
    {
        var button = Ui.Button("Create account", async (_, _) => await RegisterAsync(), primary: true, width: 320);
        _busyControls.Add(button);
        return Ui.Column(Ui.Text("Username"), _regUsername, Ui.Text("Email"), _regEmail, Ui.Text("Password"), _regPassword,
            Ui.Text("License key"), _regLicense, button);
    }

    private Control LicensePage()
    {
        var button = Ui.Button("Continue with key", async (_, _) => await LicenseAsync(), primary: true, width: 320);
        _busyControls.Add(button);
        return Ui.Column(
            Ui.Text("Sign in with only a license key. The first time a key is used it creates a key-only account; after that the same key signs you in.", 9.5f, false, Ui.Muted, 400),
            Ui.Text("License key"), _licenseKey, button);
    }

    // ------------------------------------------------------------------ startup

    private async Task StartAsync()
    {
        try
        {
            var data = await _auth.InitAsync();
            var app = data.GetProperty("app");
            _title.Text = app.GetProperty("name").GetString();
            _subtitle.Text = $"Version {AppConfig.Version} · server version {app.GetProperty("version").GetString()}";
            _announcement.Text = app.GetProperty("announcement").GetString() is { Length: > 0 } text ? text : "No announcement.";
            if (app.GetProperty("outdated").GetBoolean())
                Status($"A newer version ({app.GetProperty("version").GetString()}) is available.", Ui.Warning);
            if (!app.GetProperty("registration_open").GetBoolean()) _tabs.TabPages[1].Enabled = false;

            // "Remember me": swap the stored refresh token for a fresh session.
            if (TokenStore.Load() is { } refresh)
            {
                Status("Signing you back in…");
                _auth.RefreshToken = refresh;
                try
                {
                    await _auth.RefreshAsync();
                    TokenStore.Save(_auth.RefreshToken);
                    DialogResult = DialogResult.OK;
                    return;
                }
                catch (StackAuthException)
                {
                    TokenStore.Clear();
                    _auth.RefreshToken = null;
                }
            }
            Status("Ready. Sign in, register, or use a license key.");
            SetBusy(false);
        }
        catch (StackAuthException error) when (error.Code == "UPDATE_REQUIRED")
        {
            Status("This version is too old. Download the update to continue.", Ui.Danger);
            await OfferUpdateAsync();
        }
        catch (Exception error)
        {
            Status(Ui.Error(error), Ui.Danger);
            var retry = Ui.Button("Retry", async (_, _) => { SetBusy(true); await StartAsync(); });
            ((FlowLayoutPanel)_status.Parent!).Controls.Add(retry);
        }
    }

    private async Task OfferUpdateAsync()
    {
        var info = await _auth.UpdateAsync();
        if (info.GetProperty("mode").GetString() == "file")
        {
            var target = Path.Combine(Path.GetTempPath(), info.GetProperty("name").GetString()!);
            Status("Downloading and verifying the update…");
            await _auth.DownloadUpdateAsync(target);
            MessageBox.Show(this, $"The update was downloaded and its SHA-256 checked:\n{target}", "Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
        }
        else if (info.GetProperty("download_url").GetString() is { Length: > 0 } url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    // ------------------------------------------------------------------ actions

    private async Task SignInAsync(string username, string password)
    {
        if (username.Length == 0 || password.Length == 0)
        {
            Status("Enter your username and password.", Ui.Danger);
            return;
        }
        await RunAsync("Signing in…", async () =>
        {
            try
            {
                await _auth.LoginAsync(username, password);
            }
            catch (StackAuthException error) when (error.Code == "TOTP_REQUIRED")
            {
                var code = Ui.Prompt(this, "Two-step verification", "Enter the 6-digit code from your authenticator app:");
                if (code is null) throw;
                await _auth.InitAsync(); // a fresh init token for the second attempt
                await _auth.LoginAsync(username, password, totp: code);
            }
        });
    }

    private Task RegisterAsync() => RunAsync("Creating your account…", () =>
        _auth.RegisterAsync(_regUsername.Text.Trim(), _regPassword.Text, NullIfEmpty(_regLicense.Text), NullIfEmpty(_regEmail.Text)));

    private Task LicenseAsync() => RunAsync("Checking the key…", () => _auth.LicenseAsync(_licenseKey.Text.Trim()));

    private async Task RunAsync(string message, Func<Task> action)
    {
        SetBusy(true);
        Status(message);
        try
        {
            await action();
            if (_remember.Checked) TokenStore.Save(_auth.RefreshToken);
            else TokenStore.Clear();
            DialogResult = DialogResult.OK;
        }
        catch (StackAuthException error) when (error.Code is "INIT_REQUIRED")
        {
            await _auth.InitAsync(); // init tokens last 10 minutes; get a new one and let the user retry
            Status("The sign-in screen was open for a while. Please try again.", Ui.Warning);
        }
        catch (Exception error)
        {
            Status(Ui.Error(error), Ui.Danger);
        }
        finally
        {
            if (DialogResult != DialogResult.OK) SetBusy(false);
        }
    }

    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private void Status(string text, Color? color = null)
    {
        _status.Text = text;
        _status.ForeColor = color ?? Ui.Muted;
    }

    private void SetBusy(bool busy)
    {
        foreach (var control in _busyControls) control.Enabled = !busy;
        UseWaitCursor = busy;
    }
}
