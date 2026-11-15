// StackWorkShop Auth client for .NET 6+ (no packages needed).
// HTTPS certificate and hostname validation is provided by HttpClient.
// https://auth.stackworkshop.com/docs/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public sealed class StackAuthException : Exception
{
    public string Code { get; }
    public JsonElement? Details { get; }
    public StackAuthException(string code, string message, JsonElement? details = null) : base($"{code}: {message}")
    {
        Code = code;
        Details = details;
    }
}

/// <summary>A protected file or update package: the signed SHA-256 plus a short-lived download ticket.</summary>
public sealed record StackAuthFile(string Name, string MimeType, long Size, string Sha256, string DownloadPath);

public sealed class StackAuth
{
    private static readonly HashSet<string> RefreshableErrors = new() { "SESSION_INVALID", "SESSION_REPLACED" };
    private readonly HttpClient _http;
    private readonly HttpClient _downloads;
    private readonly string _appId;
    private readonly byte[] _secret;
    public string Version { get; }
    public string FileHash { get; }
    public IReadOnlyDictionary<string, string> Files { get; }
    public string Hwid { get; }
    /// <summary>Friendly name shown in the member's device list, e.g. Environment.MachineName.</summary>
    public string Device { get; set; }
    public string? InitToken { get; private set; }
    public string? SessionToken { get; set; }
    /// <summary>Store this (encrypted, e.g. with DPAPI) to stay signed in; call RefreshAsync() at the next start.</summary>
    public string? RefreshToken { get; set; }
    public string? SessionId { get; private set; }
    public JsonElement App { get; private set; }
    public JsonElement Member { get; private set; }
    /// <summary>Raised after login, register, license login and every refresh.</summary>
    public event Action<string, string?>? TokensChanged;

    public StackAuth(string appId, string appSecret, string version = "", string fileHash = "",
        IReadOnlyDictionary<string, string>? files = null, string hwid = "", string baseUrl = "https://auth.stackworkshop.com",
        string device = "")
    {
        _appId = appId;
        _secret = Encoding.UTF8.GetBytes(appSecret);
        Version = version;
        FileHash = fileHash;
        Files = files ?? new Dictionary<string, string>();
        Hwid = hwid;
        Device = device;
        var service = new Uri(baseUrl);
        if (service.Scheme != Uri.UriSchemeHttps && service.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("baseUrl must use HTTPS so its certificate and hostname are verified.", nameof(baseUrl));
        _http = new HttpClient { BaseAddress = service, Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("StackAuthClient/1.5");
        _downloads = new HttpClient { BaseAddress = service, Timeout = TimeSpan.FromMinutes(30) };
        _downloads.DefaultRequestHeaders.UserAgent.ParseAdd("StackAuthClient/1.5");
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
    private string Sign(string text) => Hex(HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(text)));

    /// <summary>Sends one signed request and verifies the signed response.</summary>
    public async Task<JsonElement> CallAsync(string action, Dictionary<string, object?> payload)
    {
        var path = $"/api/v1/client/{action}";
        var clean = payload.Where(p => p.Value is not null && (!(p.Value is string s) || s.Length > 0))
            .ToDictionary(p => p.Key, p => p.Value);
        var body = JsonSerializer.Serialize(clean);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Hex(RandomNumberGenerator.GetBytes(16));
        var bodyHash = Hex(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-App-Id", _appId);
        request.Headers.Add("X-Timestamp", timestamp);
        request.Headers.Add("X-Nonce", nonce);
        request.Headers.Add("X-Signature", Sign($"{timestamp}\n{nonce}\nPOST\n{path}\n{bodyHash}"));

        using var response = await _http.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        JsonElement data;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            data = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new StackAuthException($"HTTP_{(int)response.StatusCode}", "The service returned an unreadable response.");
        }

        if (!response.Headers.TryGetValues("X-Signature", out var signatures))
            throw new StackAuthException(data.TryGetProperty("code", out var c) ? c.GetString()! : $"HTTP_{(int)response.StatusCode}",
                data.TryGetProperty("message", out var m) ? m.GetString()! : "Request failed");
        var responseTime = response.Headers.GetValues("X-Timestamp").First();
        var expected = Sign($"{responseTime}\n{nonce}\n{(int)response.StatusCode}\n{Hex(SHA256.HashData(bytes))}");
        var given = signatures.First();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(given), Encoding.ASCII.GetBytes(expected))
            || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - long.Parse(responseTime)) > 120)
            throw new StackAuthException("BAD_SERVER_SIGNATURE", "The response was not signed by StackWorkShop Auth.");
        if (!data.GetProperty("ok").GetBoolean())
            throw new StackAuthException(data.GetProperty("code").GetString()!, data.GetProperty("message").GetString()!, data);
        return data;
    }

    /// <summary>Session call that refreshes once and retries when the access token ran out or was replaced.</summary>
    private async Task<JsonElement> SessionCallAsync(string action, Dictionary<string, object?>? payload = null)
    {
        payload ??= new();
        try
        {
            payload["session_token"] = SessionToken;
            return await CallAsync(action, payload);
        }
        catch (StackAuthException error) when (RefreshableErrors.Contains(error.Code) && RefreshToken is not null)
        {
            await RefreshAsync();
            payload["session_token"] = SessionToken;
            return await CallAsync(action, payload);
        }
    }

    public async Task<JsonElement> InitAsync()
    {
        var data = await CallAsync("init", new() { ["version"] = Version, ["hash"] = FileHash, ["files"] = Files, ["hwid"] = Hwid });
        InitToken = data.GetProperty("init_token").GetString();
        App = data.GetProperty("app");
        return data;
    }

    private JsonElement Session(JsonElement data)
    {
        SessionToken = data.GetProperty("session_token").GetString();
        if (data.TryGetProperty("refresh_token", out var refresh) && refresh.ValueKind == JsonValueKind.String) RefreshToken = refresh.GetString();
        if (data.TryGetProperty("session_id", out var sid) && sid.ValueKind == JsonValueKind.String) SessionId = sid.GetString();
        if (data.TryGetProperty("member", out var member)) Member = member;
        TokensChanged?.Invoke(SessionToken!, RefreshToken);
        return data;
    }

    public async Task<JsonElement> LoginAsync(string username, string password, string? captchaToken = null, string? totp = null) =>
        Session(await CallAsync("login", new() { ["init_token"] = InitToken, ["username"] = username, ["password"] = password,
            ["captcha_token"] = captchaToken, ["totp"] = totp, ["hwid"] = Hwid, ["device"] = Device }));

    public async Task<JsonElement> RegisterAsync(string username, string password, string? license = null,
        string? email = null, string? captchaToken = null) =>
        Session(await CallAsync("register", new() { ["init_token"] = InitToken, ["username"] = username, ["password"] = password,
            ["license"] = license, ["email"] = email, ["captcha_token"] = captchaToken, ["hwid"] = Hwid, ["device"] = Device }));

    public async Task<JsonElement> LicenseAsync(string key, string? totp = null) =>
        Session(await CallAsync("license", new() { ["init_token"] = InitToken, ["license"] = key, ["totp"] = totp, ["hwid"] = Hwid, ["device"] = Device }));

    /// <summary>Swaps the refresh token for a new access token and a new refresh token. The old ones stop working.</summary>
    public async Task<JsonElement> RefreshAsync()
    {
        if (RefreshToken is null) throw new StackAuthException("REFRESH_INVALID", "No refresh token. Log in again.");
        return Session(await CallAsync("refresh", new() { ["refresh_token"] = RefreshToken, ["hwid"] = Hwid }));
    }

    public async Task<JsonElement> RedeemAsync(string key)
    {
        var data = await SessionCallAsync("redeem", new() { ["license"] = key });
        Member = data.GetProperty("member");
        return data;
    }

    public Task<JsonElement> UpgradeAsync(string key) => RedeemAsync(key);

    // ---- devices

    public async Task<JsonElement> SessionsAsync() => (await SessionCallAsync("sessions")).GetProperty("sessions");
    public Task<JsonElement> RevokeSessionAsync(string sessionId) => SessionCallAsync("sessions/revoke", new() { ["session_id"] = sessionId });
    public Task<JsonElement> RevokeOtherSessionsAsync() => SessionCallAsync("sessions/revoke-others");

    // ---- two-step verification (when the app allows it)

    /// <summary>Returns { secret, otpauth_url }. Show the secret (or a QR of the URL) and ask for a code.</summary>
    public Task<JsonElement> Setup2faAsync() => SessionCallAsync("2fa/setup");
    public Task<JsonElement> Enable2faAsync(string code) => SessionCallAsync("2fa/enable", new() { ["totp"] = code });
    public Task<JsonElement> Disable2faAsync(string password, string code) => SessionCallAsync("2fa/disable", new() { ["password"] = password, ["totp"] = code });

    // ---- account

    public Task<JsonElement> ResetPasswordAsync(string currentPassword, string newPassword) =>
        SessionCallAsync("password/reset", new() { ["current_password"] = currentPassword, ["new_password"] = newPassword });

    public Task<JsonElement> ResetHwidAsync(string username, string password, string? totp = null) =>
        CallAsync("hwid/reset", new() { ["init_token"] = InitToken, ["username"] = username, ["password"] = password, ["totp"] = totp });

    public Task<JsonElement> ResetIpAsync(string username, string password, string? totp = null) =>
        CallAsync("ip/reset", new() { ["init_token"] = InitToken, ["username"] = username, ["password"] = password, ["totp"] = totp });

    public Task<JsonElement> SetAvatarAsync(string avatarUrl) => SessionCallAsync("profile/avatar", new() { ["avatar_url"] = avatarUrl });

    public async Task<JsonElement> CheckAsync()
    {
        var data = await SessionCallAsync("check", new() { ["hwid"] = Hwid });
        Member = data.GetProperty("member");
        return data;
    }

    // ---- variables

    public async Task<string?> VariableAsync(string name)
    {
        var data = SessionToken is null
            ? await CallAsync("var", new() { ["name"] = name, ["init_token"] = InitToken })
            : await SessionCallAsync("var", new() { ["name"] = name });
        return data.GetProperty("value").GetString();
    }

    public async Task<string?> GetUserVarAsync(string name) => (await SessionCallAsync("uservar/get", new() { ["name"] = name })).GetProperty("value").GetString();
    public Task<JsonElement> SetUserVarAsync(string name, string value) => SessionCallAsync("uservar/set", new() { ["name"] = name, ["value"] = value });

    // ---- licensed string vault (independent of session tokens)

    public async Task<string> EncryptAsync(string username, string password, string plaintext, string? totp = null) =>
        (await CallAsync("crypto/encrypt", new() { ["init_token"] = InitToken, ["username"] = username, ["password"] = password, ["totp"] = totp, ["plaintext"] = plaintext }))
            .GetProperty("ciphertext").GetString()!;

    public async Task<string> DecryptAsync(string username, string password, string ciphertext, string? totp = null) =>
        (await CallAsync("crypto/decrypt", new() { ["init_token"] = InitToken, ["username"] = username, ["password"] = password, ["totp"] = totp, ["ciphertext"] = ciphertext }))
            .GetProperty("plaintext").GetString()!;

    public Task<JsonElement> RewrapAsync(string username, string password, string ciphertext, string? totp = null) =>
        CallAsync("crypto/rewrap", new() { ["init_token"] = InitToken, ["username"] = username, ["password"] = password, ["totp"] = totp, ["ciphertext"] = ciphertext });

    public Task<JsonElement> SendWebhookAsync(string content, string? eventName = null)
    {
        var payload = new Dictionary<string, object?> { ["init_token"] = InitToken, ["content"] = content, ["event"] = eventName };
        return SessionToken is null ? CallAsync("webhook", payload) : SessionCallAsync("webhook", payload);
    }

    // ---- files and updates

    private static StackAuthFile FileFrom(JsonElement data) => new(
        data.GetProperty("name").GetString()!,
        data.TryGetProperty("mime_type", out var mime) ? mime.GetString() ?? "application/octet-stream" : "application/octet-stream",
        data.GetProperty("size").GetInt64(),
        data.GetProperty("sha256").GetString()!,
        data.GetProperty("download_path").GetString()!);

    /// <summary>Signed metadata and a 5-minute download ticket for a protected file.</summary>
    public async Task<StackAuthFile> FileInfoAsync(string name)
    {
        var payload = new Dictionary<string, object?> { ["name"] = name, ["init_token"] = InitToken };
        return FileFrom(SessionToken is null ? await CallAsync("file", payload) : await SessionCallAsync("file", payload));
    }

    private async Task<HttpResponseMessage> OpenDownloadAsync(StackAuthFile file)
    {
        var response = await _downloads.GetAsync(file.DownloadPath, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new StackAuthException($"HTTP_{(int)response.StatusCode}", "The download failed. Ask for the file again.");
        }
        return response;
    }

    /// <summary>Downloads a protected file into memory and verifies its SHA-256 (e.g. for Assembly.Load).</summary>
    public async Task<byte[]> FileBytesAsync(string name)
    {
        var file = await FileInfoAsync(name);
        using var response = await OpenDownloadAsync(file);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.LongLength != file.Size || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hex(SHA256.HashData(bytes))), Encoding.ASCII.GetBytes(file.Sha256)))
            throw new StackAuthException("FILE_HASH_MISMATCH", "The downloaded file does not match its signed SHA-256.");
        return bytes;
    }

    private async Task<StackAuthFile> StreamToAsync(StackAuthFile file, string destination)
    {
        var partial = destination + ".part";
        using (var response = await OpenDownloadAsync(file))
        await using (var input = await response.Content.ReadAsStreamAsync())
        await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read));
                total += read;
            }
            if (total != file.Size || Hex(hash.GetHashAndReset()) != file.Sha256)
            {
                output.Close();
                File.Delete(partial);
                throw new StackAuthException("FILE_HASH_MISMATCH", "The downloaded file does not match its signed SHA-256.");
            }
        }
        File.Move(partial, destination, overwrite: true);
        return file;
    }

    /// <summary>Streams a protected file to disk (written as .part, verified, then renamed).</summary>
    public async Task<StackAuthFile> DownloadFileAsync(string name, string destination) => await StreamToAsync(await FileInfoAsync(name), destination);

    /// <summary>Update info. Works even after UPDATE_REQUIRED, because it only needs a signed request.</summary>
    public Task<JsonElement> UpdateAsync() => CallAsync("update", new());

    /// <summary>Downloads the uploaded update package and verifies it. Returns null when the app uses a download link.</summary>
    public async Task<StackAuthFile?> DownloadUpdateAsync(string destination)
    {
        var info = await UpdateAsync();
        if (info.GetProperty("mode").GetString() != "file") return null;
        return await StreamToAsync(FileFrom(info), destination);
    }

    // ---- in-app staff panel (Member.staff is true; Settings → Staff level)

    public Task<JsonElement> StaffOverviewAsync() => SessionCallAsync("staff/overview");

    /// <summary>status: active, banned or expired.</summary>
    public Task<JsonElement> StaffMembersAsync(int page = 1, string? search = null, string? status = null) =>
        SessionCallAsync("staff/members", new() { ["page"] = page, ["search"] = search, ["status"] = status });

    /// <summary>status: unused, used or banned.</summary>
    public Task<JsonElement> StaffLicensesAsync(int page = 1, string? search = null, string? status = null) =>
        SessionCallAsync("staff/licenses", new() { ["page"] = page, ["search"] = search, ["status"] = status });

    public Task<JsonElement> StaffLogsAsync(int page = 1, string? kind = null, string? search = null) =>
        SessionCallAsync("staff/logs", new() { ["page"] = page, ["kind"] = kind, ["search"] = search });

    /// <summary>Up to 25 keys below your own level. days 0 = lifetime.</summary>
    public Task<JsonElement> StaffCreateLicensesAsync(int count, int level, int days) =>
        SessionCallAsync("staff/licenses/create", new() { ["count"] = count, ["level"] = level, ["days"] = days });

    /// <summary>action: ban (reason), unban, reset_hwid or add_days (days).</summary>
    public Task<JsonElement> StaffUpdateMemberAsync(string username, string action, string? reason = null, int? days = null) =>
        SessionCallAsync("staff/members/update", new() { ["username"] = username, ["action"] = action, ["reason"] = reason, ["days"] = days });

    // ---- reporting

    public Task<JsonElement> LogAsync(string message) => SessionCallAsync("log", new() { ["message"] = message });
    public Task<JsonElement> BanAsync(string reason) => SessionCallAsync("ban", new() { ["reason"] = reason });

    public async Task<JsonElement> LogoutAsync()
    {
        var data = await CallAsync("logout", new() { ["session_token"] = SessionToken });
        SessionToken = null;
        RefreshToken = null;
        return data;
    }
}
