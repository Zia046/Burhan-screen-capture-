using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace BurhanScreenCapture.Licensing;

/// <summary>
/// Stable, privacy-friendly device identifier: a hash of the Windows installation GUID and the system volume serial.
/// No personal data leaves the computer — only this hash is used for device binding.
/// </summary>
public static class DeviceIdentity
{
    private static string? _cached;

    public static string DeviceId => _cached ??= Compute();

    public static string DeviceName => Environment.MachineName;

    private static string Compute()
    {
        string machineGuid = "";
        try
        {
            using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            machineGuid = k?.GetValue("MachineGuid") as string ?? "";
        }
        catch { }

        uint serial = 0;
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            GetVolumeInformation(root, null, 0, out serial, out _, out _, null, 0);
        }
        catch { }

        if (machineGuid.Length == 0 && serial == 0) machineGuid = Environment.MachineName;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"BSC|{machineGuid.ToUpperInvariant()}|{serial:X8}"));
        var hex = Convert.ToHexString(hash, 0, 8);
        return $"{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}";
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformation(string root, StringBuilder? volName, int volNameSize,
        out uint serial, out uint maxComponentLen, out uint flags, StringBuilder? fsName, int fsNameSize);
}
