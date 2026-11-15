using System.Text.Json;

namespace StackAuthExample;

/// <summary>Small helpers so the forms stay readable. Plain WinForms, no designer files and no packages.</summary>
internal static class Ui
{
    public static readonly Color Accent = Color.FromArgb(79, 70, 229);
    public static readonly Color AccentDark = Color.FromArgb(55, 48, 163);
    public static readonly Color Header = Color.FromArgb(17, 24, 39);
    public static readonly Color Surface = Color.FromArgb(248, 250, 252);
    public static readonly Color Muted = Color.FromArgb(100, 116, 139);
    public static readonly Color Danger = Color.FromArgb(220, 38, 38);
    public static readonly Color Success = Color.FromArgb(22, 163, 74);
    public static readonly Color Warning = Color.FromArgb(217, 119, 6);
    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font Mono = new("Consolas", 9.5f);

    /// <summary>A label that grows to fit its text and wraps at <paramref name="width"/> pixels.</summary>
    public static Label Text(string text, float size = 9.75f, bool bold = false, Color? color = null, int width = 0) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(width == 0 ? 560 : width, 0),
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = color ?? Color.FromArgb(15, 23, 42),
        Margin = new Padding(0, 4, 0, 2),
    };

    public static TextBox Input(string placeholder = "", bool password = false, int width = 320) => new()
    {
        PlaceholderText = placeholder,
        UseSystemPasswordChar = password,
        Width = width,
        Font = Body,
        Margin = new Padding(0, 2, 0, 6),
    };

    public static NumericUpDown Number(int value, int min, int max, int width = 90) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = value,
        Width = width,
        Font = Body,
        Margin = new Padding(0, 2, 8, 6),
    };

    public static Button Button(string text, EventHandler onClick, bool primary = false, int width = 0)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = width == 0,
            Width = width == 0 ? 0 : width,
            Height = 32,
            Font = Body,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Color.White,
            ForeColor = primary ? Color.White : Color.FromArgb(15, 23, 42),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 4, 8, 4),
            Padding = new Padding(10, 0, 10, 0),
        };
        button.FlatAppearance.BorderColor = primary ? AccentDark : Color.FromArgb(203, 213, 225);
        button.Click += onClick;
        return button;
    }

    /// <summary>A vertical, scrollable column of controls.</summary>
    public static FlowLayoutPanel Column(params Control?[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 12, 16, 12),
            BackColor = Color.White,
        };
        foreach (var control in controls) if (control is not null) panel.Controls.Add(control);
        return panel;
    }

    /// <summary>A horizontal row of controls.</summary>
    public static FlowLayoutPanel Row(params Control?[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 2),
        };
        foreach (var control in controls) if (control is not null) panel.Controls.Add(control);
        return panel;
    }

    public static Label Section(string title) => new()
    {
        Text = title,
        AutoSize = true,
        Font = new Font("Segoe UI Semibold", 11.5f),
        ForeColor = Header,
        Margin = new Padding(0, 14, 0, 4),
    };

    public static TextBox Output(int width = 560, int height = 70) => new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Width = width,
        Height = height,
        Font = Mono,
        BackColor = Surface,
        Margin = new Padding(0, 2, 0, 8),
    };

    public static DataGridView Grid(int width = 620, int height = 220)
    {
        var grid = new DataGridView
        {
            Width = width,
            Height = height,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(0, 4, 0, 8),
        };
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9f);
        grid.EnableHeadersVisualStyles = true;
        return grid;
    }

    /// <summary>Fills a grid from a JSON array, showing the given properties as columns.</summary>
    public static void Fill(DataGridView grid, JsonElement rows, params (string Header, string Property)[] columns)
    {
        grid.Columns.Clear();
        grid.Rows.Clear();
        foreach (var (header, property) in columns) grid.Columns.Add(property, header);
        foreach (var row in rows.EnumerateArray())
        {
            var values = columns.Select(column => (object)Value(row, column.Property)).ToArray();
            grid.Rows.Add(values);
        }
    }

    /// <summary>Readable text for one JSON property (dates for *_at, Yes/No for booleans).</summary>
    public static string Value(JsonElement row, string property)
    {
        if (!row.TryGetProperty(property, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.Null => property == "expires_at" ? "Lifetime" : "—",
            JsonValueKind.True => "Yes",
            JsonValueKind.False => "No",
            JsonValueKind.Number when property.EndsWith("_at") => Date(value),
            JsonValueKind.String => value.GetString() ?? "",
            _ => value.ToString(),
        };
    }

    public static string Date(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(value.GetInt64()).LocalDateTime.ToString("g") : "—";

    public static string Expiry(JsonElement member) =>
        member.TryGetProperty("expires_at", out var expires) && expires.ValueKind == JsonValueKind.Number ? Date(expires) : "Lifetime";

    /// <summary>Shows a StackAuth error code and message in a friendly way.</summary>
    public static string Error(Exception error) => error switch
    {
        StackAuthException auth => $"{auth.Code}: {MessageOf(auth)}",
        HttpRequestException => "Could not reach StackWorkShop Auth. Check your internet connection.",
        TaskCanceledException => "StackWorkShop Auth did not answer in time. Try again.",
        _ => error.Message,
    };

    private static string MessageOf(StackAuthException error)
    {
        var text = error.Message;
        var prefix = error.Code + ": ";
        return text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : text;
    }

    /// <summary>A tiny input dialog (for 2FA codes and passwords).</summary>
    public static string? Prompt(IWin32Window owner, string title, string label, bool password = false)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(360, 130),
            Font = Body,
        };
        var input = Input("", password, 320);
        input.Location = new Point(20, 42);
        var caption = new Label { Text = label, AutoSize = true, Location = new Point(20, 16) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(176, 84), Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(260, 84), Width = 80 };
        form.Controls.AddRange(new Control[] { caption, input, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK && input.Text.Length > 0 ? input.Text : null;
    }
}
