using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace StackAuthExample;

/// <summary>
/// A stable hardware id for this PC and Windows user. The server only ever stores a salted hash of it.
/// Use whatever is stable for your software; this example combines the Windows MachineGuid with the user name.
/// </summary>
internal static class Hwid
{
    public static string Get()
    {
        var machineGuid = "";
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            machineGuid = key?.GetValue("MachineGuid") as string ?? "";
        }
        catch
        {
            // Fall back to the machine name below.
        }
        var raw = $"{machineGuid}|{Environment.MachineName}|{Environment.UserName}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    /// <summary>SHA-256 of this build, sent at startup. Turn on "Startup hash check" in Settings to allow only your builds.</summary>
    public static string BuildHash()
    {
        try
        {
            var path = typeof(Hwid).Assembly.Location;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }
}
