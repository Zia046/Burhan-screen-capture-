using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BurhanScreenCapture.Services;

namespace BurhanScreenCapture.Licensing;

/// <summary>
/// Free / Lifetime Pro licensing on the customer's computer.
/// Pro is granted only by a license key whose ECDSA signature verifies against the embedded public key, which
/// matches this device (when device-bound) and is not on a signed revocation list. The activation record is stored
/// with Windows DPAPI for the current user; there is no Boolean "isPro" flag anywhere on disk.
/// </summary>
public static class LicenseService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BurhanNexus.ScreenCapturePro.v2");

    private sealed class ActivationRecord
    {
        public string LicenseKey { get; set; } = "";
        public DateTime ActivatedUtc { get; set; }
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
    }

    private static ActivationRecord? _record;
    private static HashSet<string> _revoked = new();

    public static LicenseStatus Status { get; private set; } = LicenseStatus.Free();
    public static DateTime? ActivatedLocal => Status.IsPro ? _record?.ActivatedUtc.ToLocalTime() : null;
    public static string? ActivatedDeviceName => Status.IsPro ? _record?.DeviceName : null;
    public static event Action? StatusChanged;

    public static ILicenseServerClient Server { get; } = new HttpLicenseServerClient(LicensingConfig.ServerUrl);
    public static IReadOnlyList<string> TrustedKeys { get; } = LoadTrustedKeys();
    public static bool IsReleaseKeyConfigured => TrustedKeys.Count > 0;

    private static string RevocationCacheFile => Path.Combine(AppPaths.LocalData, "revocations.bnxrev");

    private static List<string> LoadTrustedKeys()
    {
        var keys = new List<string>();
        foreach (var name in new[] { "ProductionPublicKey.txt", "DevPublicKey.txt", "TestPublicKey.txt" })
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (s is null) continue; // DevPublicKey: Debug builds only; TestPublicKey: CI test builds only
            var text = new StreamReader(s).ReadToEnd().Trim();
            if (text.Length > 0) keys.Add(text);
        }
        return keys;
    }

    public static void Initialize()
    {
        LoadRevocations();
        _record = ReadRecord();
        Reevaluate();
    }

    public static void Reevaluate()
    {
        var dev = DeviceIdentity.DeviceId;
        LicenseStatus st;
        if (_record is null) st = LicenseStatus.Free();
        else if (!string.Equals(_record.DeviceId, dev, StringComparison.OrdinalIgnoreCase))
            st = new LicenseStatus(LicenseState.WrongDevice, Edition.Free, "The saved activation belongs to another computer. Please enter your license key again.");
        else st = LicenseEvaluator.Evaluate(_record.LicenseKey, TrustedKeys, dev, _revoked);
        if (_record is not null && st.State != LicenseState.Active)
            Log.Warn($"Stored license not accepted: {st.State} — {st.Message}");
        Status = st;
        Log.Info($"License: {st.State} {st.Edition} {st.Data?.LicenseId}");
        StatusChanged?.Invoke();
    }

    // ---------------- Activation

    /// <summary>Extracts a BNX-SC-PRO key from pasted text or a license file's contents.</summary>
    public static string? ExtractKey(string text)
    {
        var m = Regex.Match(text ?? "", @"BNX-SC-PRO-[0-9A-Za-z\-\s]+", RegexOptions.IgnoreCase);
        return m.Success ? m.Value : null;
    }

    public static async Task<(bool ok, string message)> ActivateAsync(string input)
    {
        if (!IsReleaseKeyConfigured)
            return (false, "This copy of the app has no licensing key built in, so it cannot activate licenses. Please install the official build from Burhan Nexus.");
        var key = ExtractKey(input) ?? input;
        var dev = DeviceIdentity.DeviceId;
        var st = LicenseEvaluator.Evaluate(key, TrustedKeys, dev, _revoked);
        if (st.State != LicenseState.Active) return (false, st.Message);

        if (Status.IsPro && Status.Data?.LicenseId == st.Data!.LicenseId)
            return (true, "This license is already activated on this computer.");

        // Optional activation server: authoritative for device limits and revocation when reachable.
        if (Server.IsConfigured)
        {
            var r = await Server.ActivateAsync(st.LicenseKey!, dev, DeviceIdentity.DeviceName);
            if (r.Success && r.LicenseKey is not null)
            {
                var bound = LicenseEvaluator.Evaluate(r.LicenseKey, TrustedKeys, dev, _revoked);
                if (bound.State == LicenseState.Active) { key = r.LicenseKey; st = bound; }
            }
            else if (!r.Message.StartsWith("Could not reach") && !r.Message.StartsWith("The licensing server did not respond"))
                return (false, r.Message); // server refused (already activated elsewhere, revoked, ...)
            else Log.Warn("Activation server unreachable; activated offline. " + r.Message);
        }

        var rec = new ActivationRecord
        {
            LicenseKey = st.LicenseKey!,
            ActivatedUtc = DateTime.UtcNow,
            DeviceId = dev,
            DeviceName = DeviceIdentity.DeviceName
        };
        try { WriteRecord(rec); }
        catch (Exception ex)
        {
            Log.Error("Saving activation failed", ex);
            return (false, "The license is valid but could not be saved on this computer: " + ex.Message);
        }
        _record = rec;
        Reevaluate();
        return (true, "LICENSE STATUS: LIFETIME PRO ACTIVATED");
    }

    public static async Task<(bool ok, string message)> ImportLicenseFileAsync(string path)
    {
        string text;
        try { text = await File.ReadAllTextAsync(path); }
        catch (Exception ex) { return (false, "The license file could not be opened: " + ex.Message); }
        var key = ExtractKey(text);
        if (key is null) return (false, "This file does not contain a Burhan Screen Capture Pro license key.");
        return await ActivateAsync(key);
    }

    public static async Task<(bool ok, string message)> DeactivateAsync()
    {
        if (_record is null) return (false, "There is no license activated on this computer.");
        var key = _record.LicenseKey;
        var id = Status.Data?.LicenseId ?? "";
        if (Server.IsConfigured) await Server.DeactivateAsync(key, DeviceIdentity.DeviceId);
        try { File.Delete(AppPaths.LicenseFile); } catch { }
        _record = null;
        Reevaluate();
        return (true, $"Lifetime Pro was removed from this computer. To use it on another computer, send your License ID ({id}) and the new computer's Device ID to {LicensingConfig.SupportEmail} for a device transfer.");
    }

    // ---------------- Revocation

    private static void LoadRevocations()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("RevokedLicenses.txt"))
        {
            if (s is not null)
                foreach (var line in new StreamReader(s).ReadToEnd().Split('\n'))
                {
                    var t = line.Trim();
                    if (t.StartsWith("LIC-", StringComparison.OrdinalIgnoreCase)) set.Add(t);
                }
        }
        try
        {
            if (File.Exists(RevocationCacheFile) && RevocationList.ParseVerified(File.ReadAllText(RevocationCacheFile), TrustedKeys) is { } body)
                set.UnionWith(body.RevokedLicenseIds);
        }
        catch { }
        _revoked = set;
    }

    /// <summary>Applies a signed revocation list (from the web, the server or a file). Unsigned/forged lists are ignored.</summary>
    public static bool ApplyRevocationList(string text)
    {
        var body = RevocationList.ParseVerified(text, TrustedKeys);
        if (body is null) { Log.Warn("Revocation list rejected (bad signature or format)."); return false; }
        try { File.WriteAllText(RevocationCacheFile, text); } catch { }
        LoadRevocations();
        Reevaluate();
        return true;
    }

    /// <summary>Background check at startup. Offline or failing → the last known list is kept and Pro keeps working.</summary>
    public static async Task RefreshRevocationsAsync()
    {
        try
        {
            string? text = null;
            if (!string.IsNullOrWhiteSpace(LicensingConfig.RevocationListUrl))
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                text = await http.GetStringAsync(LicensingConfig.RevocationListUrl);
            }
            else if (Server.IsConfigured) text = await Server.GetRevocationListAsync();
            if (text is not null) ApplyRevocationList(text);
        }
        catch (Exception ex) { Log.Info("Revocation list not refreshed (offline?): " + ex.Message); }
    }

    // ---------------- Storage (DPAPI, current user)

    private static ActivationRecord? ReadRecord()
    {
        try
        {
            if (!File.Exists(AppPaths.LicenseFile)) return null;
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(AppPaths.LicenseFile), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<ActivationRecord>(bytes);
        }
        catch (Exception ex)
        {
            Log.Warn("Stored activation unreadable: " + ex.Message);
            return null;
        }
    }

    private static void WriteRecord(ActivationRecord rec)
    {
        var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(rec), Entropy, DataProtectionScope.CurrentUser);
        var tmp = AppPaths.LicenseFile + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, AppPaths.LicenseFile, true);
    }
}
