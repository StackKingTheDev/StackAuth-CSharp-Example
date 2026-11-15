using System.Text.Json;

namespace StackAuthExample;

/// <summary>
/// In-app staff panel. Shown only when member.staff is true (Settings → In-app staff panel → Staff level).
/// The server enforces every rule: staff only manage members below their own level, never see IPs or
/// HWIDs, and a shared demo admin is read-only with masked emails and keys.
/// </summary>
internal sealed class AdminPanel : UserControl
{
    private readonly StackAuth _auth;
    private readonly Action<string, Color?> _status;
    private readonly Label _stats = Ui.Text("Loading…", 10, false, null, 640);
    private readonly DataGridView _members = Ui.Grid(660, 190);
    private readonly DataGridView _licenses = Ui.Grid(660, 150);
    private readonly DataGridView _logs = Ui.Grid(660, 170);
    private readonly TextBox _memberSearch = Ui.Input("search members", width: 200);
    private readonly ComboBox _memberStatus = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Font = Ui.Body, Margin = new Padding(0, 2, 8, 6) };
    private readonly ComboBox _licenseStatus = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Font = Ui.Body, Margin = new Padding(0, 2, 8, 6) };
    private readonly TextBox _reason = Ui.Input("ban reason", width: 180);
    private readonly NumericUpDown _days = Ui.Number(7, 1, 3650, 70);
    private readonly NumericUpDown _keyCount = Ui.Number(5, 1, 25, 60);
    private readonly NumericUpDown _keyLevel = Ui.Number(1, 0, 1000, 60);
    private readonly NumericUpDown _keyDays = Ui.Number(30, 0, 3650, 70);
    private readonly TextBox _output = Ui.Output(660, 60);

    public AdminPanel(StackAuth auth, Action<string, Color?> status, bool readOnly)
    {
        _auth = auth;
        _status = status;
        Dock = DockStyle.Fill;
        _memberStatus.Items.AddRange(new object[] { "all", "active", "banned", "expired" });
        _memberStatus.SelectedIndex = 0;
        _licenseStatus.Items.AddRange(new object[] { "all", "unused", "used", "banned" });
        _licenseStatus.SelectedIndex = 0;

        var banner = readOnly
            ? Ui.Text("Demo admin: you can see the whole panel, but changes are turned off and emails and keys are masked. In your own app, a staff account can make these changes.", 9.5f, false, Ui.Warning, 660)
            : null;

        Controls.Add(Ui.Column(
            Ui.Section("Overview"),
            banner,
            _stats,
            Ui.Button("Reload everything", async (_, _) => await LoadAllAsync()),
            Ui.Section("Members"),
            Ui.Row(_memberSearch, _memberStatus, Ui.Button("Search", async (_, _) => await Run(LoadMembersAsync))),
            _members,
            Ui.Row(
                Ui.Button("Ban", async (_, _) => await UpdateSelected("ban")),
                _reason,
                Ui.Button("Unban", async (_, _) => await UpdateSelected("unban")),
                Ui.Button("Reset HWID", async (_, _) => await UpdateSelected("reset_hwid")),
                _days,
                Ui.Button("Add days", async (_, _) => await UpdateSelected("add_days"))),
            Ui.Section("License keys"),
            Ui.Row(_licenseStatus, Ui.Button("Show", async (_, _) => await Run(LoadLicensesAsync))),
            _licenses,
            Ui.Row(Ui.Text("Create", 9.5f), _keyCount, Ui.Text("keys, level", 9.5f), _keyLevel, Ui.Text("days (0 = lifetime)", 9.5f), _keyDays,
                Ui.Button("Create", async (_, _) => await Run(async () =>
                {
                    var data = await _auth.StaffCreateLicensesAsync((int)_keyCount.Value, (int)_keyLevel.Value, (int)_keyDays.Value);
                    _output.Text = "New keys:" + Environment.NewLine + string.Join(Environment.NewLine, data.GetProperty("keys").EnumerateArray().Select(k => k.GetString()));
                    await LoadLicensesAsync();
                }), primary: true)),
            _output,
            Ui.Section("Recent activity"),
            _logs));

        Load += async (_, _) => await LoadAllAsync();
    }

    private async Task LoadAllAsync() => await Run(async () =>
    {
        var overview = await _auth.StaffOverviewAsync();
        var stats = overview.GetProperty("stats");
        var members = stats.GetProperty("members");
        var licenses = stats.GetProperty("licenses");
        _stats.Text =
            $"Members: {members.GetProperty("total")} total · {members.GetProperty("active")} active · {members.GetProperty("banned")} banned     " +
            $"Online now: {stats.GetProperty("online_members")}     Logins (24 h): {stats.GetProperty("logins_24h")}{Environment.NewLine}" +
            $"License keys: {licenses.GetProperty("total")} total · {licenses.GetProperty("unused")} unused · {licenses.GetProperty("used")} used     " +
            $"Staff level: {overview.GetProperty("staff_level")}";
        await LoadMembersAsync();
        await LoadLicensesAsync();
        var logs = await _auth.StaffLogsAsync();
        Ui.Fill(_logs, logs.GetProperty("logs"), ("Time", "at"), ("Event", "kind"), ("Who", "actor"), ("Details", "message"));
    });

    private async Task LoadMembersAsync()
    {
        var status = _memberStatus.SelectedItem as string;
        var data = await _auth.StaffMembersAsync(1, _memberSearch.Text.Trim(), status == "all" ? null : status);
        Ui.Fill(_members, data.GetProperty("members"),
            ("Username", "username"), ("Email", "email"), ("Level", "level"), ("Expires", "expires_at"),
            ("Banned", "banned"), ("Staff", "staff"), ("2FA", "totp_enabled"), ("Last sign-in", "last_login_at"));
    }

    private async Task LoadLicensesAsync()
    {
        var status = _licenseStatus.SelectedItem as string;
        var data = await _auth.StaffLicensesAsync(1, null, status == "all" ? null : status);
        Ui.Fill(_licenses, data.GetProperty("licenses"),
            ("Key", "key"), ("Level", "level"), ("Days", "days"), ("Status", "status"), ("Used by", "used_by"), ("Created", "created_at"));
    }

    private async Task UpdateSelected(string action) => await Run(async () =>
    {
        if (_members.CurrentRow?.Cells["username"].Value is not string username)
        {
            _status("Pick a member in the list first.", Ui.Warning);
            return;
        }
        JsonElement result = action switch
        {
            "ban" => await _auth.StaffUpdateMemberAsync(username, "ban", reason: _reason.Text.Trim()),
            "add_days" => await _auth.StaffUpdateMemberAsync(username, "add_days", days: (int)_days.Value),
            _ => await _auth.StaffUpdateMemberAsync(username, action),
        };
        _output.Text = $"{username}: {action.Replace('_', ' ')} done. Access {Ui.Expiry(result.GetProperty("member"))}.";
        await LoadMembersAsync();
    });

    private async Task Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception error)
        {
            _output.Text = "✖ " + Ui.Error(error);
            _output.ForeColor = Ui.Danger;
            _status(Ui.Error(error), Ui.Danger);
            return;
        }
        _output.ForeColor = Color.Black;
    }
}
