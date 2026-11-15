using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace StackAuthExample;

/// <summary>
/// The page a member sees after signing in: profile on the left, every feature on the tabs.
/// Each button is one SDK call, so you can copy the handler you need into your own software.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly StackAuth _auth;
    private readonly PictureBox _avatar = new() { Size = new Size(96, 96), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 4, 0, 8) };
    private readonly FlowLayoutPanel _profile = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Ui.Surface };
    private readonly Label _status = Ui.Text("", 9, false, Ui.Muted, 220);
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Font = Ui.Body };
    private JsonElement _member;

    public bool SignedOut { get; private set; }

    public MainForm(StackAuth auth)
    {
        _auth = auth;
        _member = auth.Member;
        Text = $"{auth.App.GetProperty("name").GetString()} · StackWorkShop Auth example";
        ClientSize = new Size(1040, 680);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = Ui.Body;
        BackColor = Color.White;

        var side = new Panel { Dock = DockStyle.Left, Width = 260, BackColor = Ui.Surface, Padding = new Padding(18, 18, 14, 12) };
        var sideColumn = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Ui.Surface };
        sideColumn.Controls.Add(_avatar);
        sideColumn.Controls.Add(_profile);
        sideColumn.Controls.Add(Ui.Button("Refresh profile", async (_, _) => await Try(CheckAsync), width: 200));
        sideColumn.Controls.Add(Ui.Button("Sign out", async (_, _) => await SignOutAsync(), width: 200));
        sideColumn.Controls.Add(_status);
        side.Controls.Add(sideColumn);

        _tabs.TabPages.Add(Page("Overview", OverviewTab()));
        _tabs.TabPages.Add(Page("Variables", VariablesTab()));
        _tabs.TabPages.Add(Page("Files & updates", FilesTab()));
        _tabs.TabPages.Add(Page("Vault", VaultTab()));
        _tabs.TabPages.Add(Page("Account", AccountTab()));
        if (IsTrue(_member, "staff")) _tabs.TabPages.Add(Page("Admin", new AdminPanel(_auth, Status, IsTrue(_member, "demo"))));

        Controls.Add(_tabs);
        Controls.Add(side);
        RenderProfile();
        FormClosing += (_, _) => { if (!SignedOut) _ = SafeLogout(); };
    }

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { BackColor = Color.White };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    private static bool IsTrue(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    // ------------------------------------------------------------------ profile

    private void RenderProfile()
    {
        _profile.Controls.Clear();
        var name = Ui.Text(_member.GetProperty("username").GetString()!, 14, true);
        _profile.Controls.Add(name);
        var badges = new List<string>();
        if (IsTrue(_member, "staff")) badges.Add("STAFF");
        if (IsTrue(_member, "demo")) badges.Add("SHARED DEMO");
        if (IsTrue(_member, "license_only")) badges.Add("KEY-ONLY");
        if (badges.Count > 0) _profile.Controls.Add(Ui.Text(string.Join(" · ", badges), 8.5f, true, Ui.Accent));

        void Line(string label, string value)
        {
            _profile.Controls.Add(Ui.Text(label, 8.5f, false, Ui.Muted));
            _profile.Controls.Add(Ui.Text(value, 9.75f, false, null, 220));
        }
        Line("Email", _member.GetProperty("email").GetString() is { Length: > 0 } email ? email : "—");
        Line("Level", _member.GetProperty("level").ToString());
        Line("Access", Ui.Expiry(_member));
        Line("Member since", Ui.Date(_member.GetProperty("created_at")));
        Line("Last sign-in", Ui.Date(_member.GetProperty("last_login_at")));
        Line("Two-step verification", IsTrue(_member, "totp_enabled") ? "On" : "Off");
        _ = LoadAvatarAsync(_member.GetProperty("avatar_url").GetString());
    }

    private async Task LoadAvatarAsync(string? url)
    {
        _avatar.Image = Placeholder(_member.GetProperty("username").GetString()!);
        if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var bytes = await http.GetByteArrayAsync(url);
            if (bytes.Length > 2_000_000) return;
            using var stream = new MemoryStream(bytes);
            _avatar.Image = Image.FromStream(stream);
        }
        catch
        {
            // Keep the placeholder (the URL may be an SVG or unreachable).
        }
    }

    private static Bitmap Placeholder(string username)
    {
        var bitmap = new Bitmap(96, 96);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(Ui.Accent);
        g.FillEllipse(brush, 0, 0, 95, 95);
        using var font = new Font("Segoe UI Semibold", 34);
        var text = username[..1].ToUpperInvariant();
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, Brushes.White, (96 - size.Width) / 2, (96 - size.Height) / 2);
        return bitmap;
    }

    private async Task CheckAsync()
    {
        var data = await _auth.CheckAsync();
        _member = data.GetProperty("member");
        RenderProfile();
        Status($"Session OK until {Ui.Date(data.GetProperty("session_expires_at"))}.", Ui.Success);
    }

    private async Task SignOutAsync()
    {
        await SafeLogout();
        TokenStore.Clear();
        SignedOut = true;
        Close();
    }

    private async Task SafeLogout()
    {
        try { await _auth.LogoutAsync(); } catch { /* already ended */ }
    }

    // ------------------------------------------------------------------ tabs

    private Control OverviewTab()
    {
        var app = _auth.App;
        var features = new StringBuilder();
        void Flag(string label, string property) => features.AppendLine($"{label,-28} {(app.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.True ? "on" : "off")}");
        Flag("Free mode", "free_mode");
        Flag("HWID lock", "hwid_lock");
        Flag("Startup hash check", "hash_check");
        Flag("Dependency file check", "file_hash_check");
        Flag("Member 2FA", "member_totp");
        Flag("Refresh tokens", "refresh_tokens");
        Flag("In-app staff panel", "staff_panel");
        var info = Ui.Output(620, 150);
        info.Text = features.ToString().Replace("\n", Environment.NewLine);

        var demoNote = IsTrue(_member, "demo")
            ? Ui.Text("You are using a shared demo account. You can look at everything, but changes (password, 2FA, avatar, devices, user variables, redeeming keys) are turned off. Register your own account to try them.", 9.5f, false, Ui.Warning, 620)
            : null;
        var welcome = Ui.Output(620, 46);
        _ = Fill(welcome, async () => await _auth.VariableAsync("welcome_message") ?? "");

        return Ui.Column(
            Ui.Section($"Welcome, {_member.GetProperty("username").GetString()}!"),
            demoNote,
            Ui.Text("Announcement"),
            Ui.Text(app.GetProperty("announcement").GetString() is { Length: > 0 } a ? a : "—", 9.75f, false, null, 620),
            Ui.Text("welcome_message (a public variable)"),
            welcome,
            Ui.Section("This application's settings (from init)"),
            info,
            Ui.Text($"Session id {_auth.SessionId} · refresh token {(_auth.RefreshToken is null ? "off" : "stored (rotates on every refresh)")}", 9, false, Ui.Muted, 620),
            Ui.Button("Refresh the session now (rotates the refresh token)", async (_, _) => await Try(async () =>
            {
                await _auth.RefreshAsync();
                TokenStore.Save(_auth.RefreshToken);
                Status("New access token and refresh token received.", Ui.Success);
            })));
    }

    private Control VariablesTab()
    {
        var output = Ui.Output(620, 150);
        var name = Ui.Input("variable name", width: 220);
        var list = Ui.Row();
        foreach (var variable in AppConfig.Variables)
            list.Controls.Add(Ui.Button(variable, async (_, _) => await Fill(output, async () => $"{variable} = {await _auth.VariableAsync(variable)}", append: true)));

        var fakeTest = Ui.Button("Read it WITHOUT a login (fake mode)", async (_, _) => await Fill(output, async () =>
        {
            // A second client that never signed in, like a cracked build that skipped the login screen.
            var anonymous = new StackAuth(AppConfig.AppId, AppConfig.AppSecret, AppConfig.Version, hwid: Hwid.Get(), baseUrl: AppConfig.BaseUrl);
            await anonymous.InitAsync();
            return $"{AppConfig.FakeModeVariable} without login = {await anonymous.VariableAsync(AppConfig.FakeModeVariable)}";
        }, append: true));

        var userKey = Ui.Input("name", width: 160);
        var userValue = Ui.Input("value", width: 240);
        return Ui.Column(
            Ui.Section("App variables"),
            Ui.Text("Public variables need only init(). Secured ones need a session, and can require a level (vip_message needs level 5).", 9.5f, false, Ui.Muted, 620),
            list,
            Ui.Row(name, Ui.Button("Get", async (_, _) => await Fill(output, async () => $"{name.Text} = {await _auth.VariableAsync(name.Text.Trim())}", append: true))),
            Ui.Section("Fake mode"),
            Ui.Text($"\"{AppConfig.FakeModeVariable}\" has Fake mode on. Signed in you get the real value; a client without a valid login gets the fake one, with no error to tell it apart.", 9.5f, false, Ui.Muted, 620),
            Ui.Row(Ui.Button($"Read {AppConfig.FakeModeVariable} signed in", async (_, _) => await Fill(output, async () => $"{AppConfig.FakeModeVariable} signed in = {await _auth.VariableAsync(AppConfig.FakeModeVariable)}", append: true)), fakeTest),
            output,
            Ui.Section("User variables (per member)"),
            Ui.Row(userKey, userValue),
            Ui.Row(
                Ui.Button("Get", async (_, _) => await Fill(output, async () => $"user.{userKey.Text} = {await _auth.GetUserVarAsync(userKey.Text.Trim())}", append: true)),
                Ui.Button("Set", async (_, _) => await Fill(output, async () => { await _auth.SetUserVarAsync(userKey.Text.Trim(), userValue.Text); return $"user.{userKey.Text} saved"; }, append: true))),
            Ui.Text("The demo accounts already have \"theme\" set; try reading it.", 9, false, Ui.Muted));
    }

    private Control FilesTab()
    {
        var output = Ui.Output(620, 170);
        var name = Ui.Input("file name", width: 220);
        name.Text = AppConfig.DemoFile;
        return Ui.Column(
            Ui.Section("Protected files"),
            Ui.Text("The signed response carries the file's SHA-256 and a 5-minute download ticket. The SDK checks the hash before handing you the bytes.", 9.5f, false, Ui.Muted, 620),
            Ui.Row(name,
                Ui.Button("Load into memory", async (_, _) => await Fill(output, async () =>
                {
                    var bytes = await _auth.FileBytesAsync(name.Text.Trim());
                    // For a DLL you would call System.Reflection.Assembly.Load(bytes) here.
                    return $"{bytes.Length} bytes, SHA-256 verified:{Environment.NewLine}{Encoding.UTF8.GetString(bytes)}";
                })),
                Ui.Button("Save to disk…", async (_, _) => await Try(async () =>
                {
                    using var dialog = new SaveFileDialog { FileName = name.Text.Trim() };
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    var file = await _auth.DownloadFileAsync(name.Text.Trim(), dialog.FileName);
                    output.Text = $"Saved {file.Size} bytes to {dialog.FileName}{Environment.NewLine}SHA-256 {file.Sha256} (verified)";
                }))),
            output,
            Ui.Section("Updates"),
            Ui.Text("update() works even after UPDATE_REQUIRED. It returns a download link, or an uploaded file the SDK downloads and verifies.", 9.5f, false, Ui.Muted, 620),
            Ui.Button("Check for updates", async (_, _) => await Fill(output, async () =>
            {
                var info = await _auth.UpdateAsync();
                var mode = info.GetProperty("mode").GetString();
                var latest = info.GetProperty("version").GetString();
                if (mode == "link")
                {
                    var url = info.GetProperty("download_url").GetString();
                    return $"Latest version {latest} (you have {AppConfig.Version}).{Environment.NewLine}Download link: {url}";
                }
                return $"Latest version {latest}: file {info.GetProperty("name").GetString()} ({info.GetProperty("size")} bytes, SHA-256 {info.GetProperty("sha256").GetString()})";
            })));
    }

    private Control VaultTab()
    {
        var username = Ui.Input("username", width: 200);
        var password = Ui.Input("password", password: true, width: 200);
        username.Text = _member.GetProperty("username").GetString();
        var demo = AppConfig.DemoLogins.FirstOrDefault(d => d.Username == username.Text);
        if (demo.Password is not null) password.Text = demo.Password;
        var plain = Ui.Input("text to encrypt", width: 620);
        plain.Text = "My secret config string";
        var cipher = Ui.Output(620, 60);
        cipher.ReadOnly = false;
        var result = Ui.Output(620, 46);
        return Ui.Column(
            Ui.Section("Licensed string vault"),
            Ui.Text("Encrypt strings with a server-side key that is separate from sessions and tokens. Decrypting needs the account's username, password and an active license, and only the same account can decrypt. Ship the ciphertext in your tool; decrypt it at runtime.", 9.5f, false, Ui.Muted, 620),
            Ui.Row(username, password),
            plain,
            Ui.Button("Encrypt", async (_, _) => await Fill(cipher, () => WithInit(() => _auth.EncryptAsync(username.Text, password.Text, plain.Text)))),
            cipher,
            Ui.Button("Decrypt", async (_, _) => await Fill(result, async () => "Decrypted: " + await WithInit(() => _auth.DecryptAsync(username.Text, password.Text, cipher.Text.Trim())))),
            result);
    }

    private Control AccountTab()
    {
        var output = Ui.Output(620, 80);
        var key = Ui.Input("license key", width: 300);
        var current = Ui.Input("current password", password: true, width: 200);
        var next = Ui.Input("new password", password: true, width: 200);
        var hwidUser = Ui.Input("username", width: 200);
        var hwidPassword = Ui.Input("password", password: true, width: 200);
        hwidUser.Text = _member.GetProperty("username").GetString();
        var devices = Ui.Grid(620, 130);
        var message = Ui.Input("message", width: 400);

        async Task LoadDevices()
        {
            var sessions = await _auth.SessionsAsync();
            Ui.Fill(devices, sessions, ("Device", "device"), ("IP", "ip"), ("Method", "method"), ("Signed in", "created_at"), ("Last seen", "last_seen_at"), ("This one", "current"), ("Id", "id"));
        }
        _ = Try(LoadDevices);

        return Ui.Column(
            output,
            Ui.Section("Redeem a license key"),
            Ui.Text("Adds the key's days to your account and raises your level to the key's level.", 9.5f, false, Ui.Muted, 620),
            Ui.Row(key, Ui.Button("Redeem", async (_, _) => await Fill(output, async () =>
            {
                var data = await _auth.RedeemAsync(key.Text.Trim());
                _member = data.GetProperty("member");
                RenderProfile();
                return $"Redeemed. Level {_member.GetProperty("level")}, access {Ui.Expiry(_member)}.";
            }))),
            Ui.Section("Change password"),
            Ui.Row(current, next, Ui.Button("Change", async (_, _) => await Fill(output, async () =>
            {
                await _auth.ResetPasswordAsync(current.Text, next.Text);
                return "Password changed. Your other devices were signed out.";
            }))),
            Ui.Section("Two-step verification (authenticator app)"),
            Ui.Row(
                Ui.Button("Turn on 2FA…", async (_, _) => await Try(async () =>
                {
                    var setup = await _auth.Setup2faAsync();
                    var secret = setup.GetProperty("secret").GetString();
                    var code = Ui.Prompt(this, "Turn on 2FA", $"Add this key to your authenticator app, then enter the code:\n{secret}");
                    if (code is null) return;
                    await _auth.Enable2faAsync(code);
                    await CheckAsync();
                    output.Text = "2FA is on. You'll need a code at every sign-in.";
                })),
                Ui.Button("Turn off 2FA…", async (_, _) => await Try(async () =>
                {
                    var pass = Ui.Prompt(this, "Turn off 2FA", "Your password:", password: true);
                    var code = pass is null ? null : Ui.Prompt(this, "Turn off 2FA", "A code from your authenticator app:");
                    if (code is null) return;
                    await _auth.Disable2faAsync(pass!, code);
                    await CheckAsync();
                    output.Text = "2FA is off.";
                }))),
            Ui.Section("Self-service HWID reset"),
            Ui.Text("Moved to a new PC? Release the device lock with your username and password (limited per 30 days).", 9.5f, false, Ui.Muted, 620),
            Ui.Row(hwidUser, hwidPassword, Ui.Button("Reset HWID", async (_, _) => await Fill(output, async () =>
            {
                var data = await WithInit(() => _auth.ResetHwidAsync(hwidUser.Text, hwidPassword.Text));
                return $"HWID released. Resets left this window: {data.GetProperty("resets_remaining")}. Sign in again on the new PC.";
            }))),
            Ui.Section("Devices (sessions)"),
            devices,
            Ui.Row(
                Ui.Button("Reload", async (_, _) => await Try(LoadDevices)),
                Ui.Button("Sign out selected device", async (_, _) => await Try(async () =>
                {
                    if (devices.CurrentRow?.Cells["id"].Value is not string id) return;
                    await _auth.RevokeSessionAsync(id);
                    await LoadDevices();
                })),
                Ui.Button("Sign out all other devices", async (_, _) => await Try(async () =>
                {
                    var data = await _auth.RevokeOtherSessionsAsync();
                    output.Text = $"Signed out {data.GetProperty("revoked")} other device(s).";
                    await LoadDevices();
                }))),
            Ui.Section("Reporting"),
            Ui.Row(message,
                Ui.Button("Write to app log", async (_, _) => await Fill(output, async () => { await _auth.LogAsync(message.Text); return "Logged."; })),
                Ui.Button("Send to Discord", async (_, _) => await Fill(output, async () => { await WithInit(() => _auth.SendWebhookAsync(message.Text, "example")); return "Sent to the app's Discord webhook."; }))),
            Ui.Text("The Discord relay is off in the public demo app (it would let anyone post to our channel). Turn it on in your own app's settings.", 9, false, Ui.Muted, 620));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// The vault, HWID reset and the Discord relay check username + password (not the session), so they use
    /// the init token. It lasts 10 minutes: when it has run out, call init() again and retry once.
    /// </summary>
    private async Task<T> WithInit<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (StackAuthException error) when (error.Code == "INIT_REQUIRED")
        {
            await _auth.InitAsync();
            return await call();
        }
    }

    private async Task Fill(TextBox output, Func<Task<string>> action, bool append = false)
    {
        try
        {
            var text = await action();
            output.Text = append && output.TextLength > 0 ? output.Text + Environment.NewLine + text : text;
            output.ForeColor = Color.Black;
        }
        catch (Exception error)
        {
            var text = "✖ " + Ui.Error(error);
            output.Text = append && output.TextLength > 0 ? output.Text + Environment.NewLine + text : text;
            output.ForeColor = Ui.Danger;
            await HandleSessionLoss(error);
        }
        output.SelectionStart = output.TextLength;
        output.ScrollToCaret();
    }

    private async Task Try(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception error)
        {
            Status(Ui.Error(error), Ui.Danger);
            await HandleSessionLoss(error);
        }
    }

    /// <summary>Banned, expired or signed out elsewhere: back to the sign-in screen.</summary>
    private async Task HandleSessionLoss(Exception error)
    {
        if (error is StackAuthException { Code: "BANNED" or "SESSION_EXPIRED" or "SUBSCRIPTION_EXPIRED" or "REFRESH_REUSED" or "HWID_MISMATCH" } auth)
        {
            MessageBox.Show(this, Ui.Error(auth), "Signed out", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            TokenStore.Clear();
            SignedOut = true;
            await Task.Yield();
            Close();
        }
    }

    private void Status(string text, Color? color = null)
    {
        _status.Text = text;
        _status.ForeColor = color ?? Ui.Muted;
    }
}
