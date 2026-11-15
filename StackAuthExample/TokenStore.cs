using System.Security.Cryptography;
using System.Text;

namespace StackAuthExample;

/// <summary>
/// "Remember me": keeps the refresh token in %LocalAppData%, encrypted with Windows DPAPI for the
/// current user. At the next start the app swaps it for a new session (and a new refresh token).
/// </summary>
internal static class TokenStore
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StackAuthExample");
    private static string FilePath => Path.Combine(Folder, AppConfig.AppId + ".session");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("StackAuthExample:" + AppConfig.AppId);

    public static void Save(string? refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            Clear();
            return;
        }
        Directory.CreateDirectory(Folder);
        var sealedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(refreshToken), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, sealedBytes);
    }

    public static string? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser));
        }
        catch
        {
            Clear();
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch
        {
            // nothing to do
        }
    }
}
