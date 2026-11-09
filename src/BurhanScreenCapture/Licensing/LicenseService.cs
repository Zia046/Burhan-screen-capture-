using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BurhanScreenCapture.Services;
using Microsoft.Win32;

namespace BurhanScreenCapture.Licensing;

/// <summary>
/// Application-side licensing: device binding, secure local storage (DPAPI), signed offline validation,
/// 7-day trial, activation (online or license file), deactivation.
/// </summary>
public static class LicenseService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BurhanNexus.ScreenCapturePro.v1");
    private const string RegPath = @"Software\Burhan Nexus\Screen Capture Pro";

    private sealed class LocalState
    {
        public DateTime? TrialStartedUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
    }

    private static LocalState _state = new();
    public static LicenseStatus Status { get; private set; } = LicenseStatus.Free();
    public static event Action? StatusChanged;

    public static ILicenseServerClient Server { get; } = new HttpLicenseServerClient(LicensingConfig.ServerUrl);

    public static IReadOnlyList<string> TrustedKeys { get; } = LoadTrustedKeys();

    public static bool IsReleaseKeyConfigured => TrustedKeys.Count > 0;

    private static List<string> LoadTrustedKeys()
    {
        var keys = new List<string>();
        void Add(string resource)
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            if (s is null) return;
            var text = new StreamReader(s).ReadToEnd().Trim();
            if (text.Length > 0) keys.Add(text);
        }
        Add("ProductionPublicKey.txt");
#if LICENSE_DEV_KEYS
        Add("DevPublicKey.txt"); // Debug builds only — see tools/LicenseGenerator/dev-keys/README.md
#endif
        return keys;
    }

    public static void Initialize()
    {
        _state = ReadState();
        var now = DateTime.UtcNow;
        var lastSeen = _state.LastSeenUtc == default ? (DateTime?)null : _state.LastSeenUtc;
        Evaluate(lastSeen);
        if (now > _state.LastSeenUtc) { _state.LastSeenUtc = now; WriteState(); }
    }

    public static void Reevaluate() => Evaluate(_state.LastSeenUtc == default ? null : _state.LastSeenUtc);

    private static void Evaluate(DateTime? lastSeen)
    {
        var now = DateTime.UtcNow;
        SignedLicense? lic = null;
        string? loadError = null;
        try { lic = ReadLicense(); }
        catch (Exception ex) { loadError = ex.Message; }

        var status = LicenseEvaluator.Evaluate(lic, TrustedKeys, DeviceIdentity.DeviceId, now, lastSeen);
        if (status.State != LicenseState.Active)
        {
            var trial = EvaluateTrial(now, lastSeen);
            if (trial is not null && (status.State == LicenseState.None || trial.IsPro)) status = trial;
            else if (loadError is not null && status.State == LicenseState.None)
                status = LicenseStatus.Free("The stored license could not be read. Please activate again.");
        }
        Status = status;
        Log.Info($"License: {status.State} {status.Edition} {status.Plan}");
        StatusChanged?.Invoke();
    }

    // ---------------- Trial ----------------

    public static bool TrialUsed => GetTrialStart() is not null;

    private static DateTime? GetTrialStart()
    {
        DateTime? reg = null;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RegPath);
            if (k?.GetValue("ts") is string s && long.TryParse(Unscramble(s), out var ticks)) reg = new DateTime(ticks, DateTimeKind.Utc);
        }
        catch { }
        var file = _state.TrialStartedUtc;
        if (reg is null) return file;
        if (file is null) return reg;
        return reg < file ? reg : file;
    }

    private static LicenseStatus? EvaluateTrial(DateTime now, DateTime? lastSeen)
    {
        var start = GetTrialStart();
        if (start is null) return null;
        var end = start.Value.AddDays(LicensePlanInfo.TrialDays);
        if (lastSeen is { } seen && now + LicenseEvaluator.ClockTolerance < seen)
            return new LicenseStatus(LicenseState.ClockTampered, Edition.Free, LicensePlan.Trial,
                "The system clock appears to have been moved backwards. Please correct the date and time.", end);
        if (now >= end)
            return new LicenseStatus(LicenseState.Expired, Edition.Free, LicensePlan.Trial,
                "Your 7-day Pro trial has ended. Free features remain available.", end);
        return new LicenseStatus(LicenseState.Active, Edition.ProTrial, LicensePlan.Trial, "Pro Trial — active", end);
    }

    public static (bool ok, string message) StartTrial()
    {
        if (Status.Edition == Edition.Pro) return (false, "You already have an active Pro license.");
        if (TrialUsed) return (false, "The free trial has already been used on this computer.");
        var now = DateTime.UtcNow;
        _state.TrialStartedUtc = now;
        WriteState();
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RegPath);
            k.SetValue("ts", Scramble(now.Ticks.ToString()));
        }
        catch (Exception ex) { Log.Warn("Trial registry marker: " + ex.Message); }
        Reevaluate();
        return (true, "Your 7-day Pro trial has started. Enjoy all Pro features!");
    }

    // ---------------- Activation ----------------

    public static async Task<(bool ok, string message)> ActivateOnlineAsync(string licenseKeyInput)
    {
        var key = LicenseKey.Normalize(licenseKeyInput);
        if (!LicenseKey.IsWellFormed(key))
            return (false, "This license key is not valid. Check that you typed it exactly as shown in your purchase email (BSCP-XXXXX-XXXXX-XXXXX-XXXXX).");
        if (!Server.IsConfigured)
            return (false, "Online activation is not available yet. Send your Device ID and license key to " + LicensingConfig.SupportEmail +
                           " to receive a license file, then use \"Import license file\".");
        var result = await Server.ActivateAsync(key, DeviceIdentity.DeviceId, DeviceIdentity.DeviceName);
        if (!result.Success || result.License is null) return (false, result.Message);
        return InstallLicense(result.License, key);
    }

    public static (bool ok, string message) ImportLicenseFile(string path)
    {
        try
        {
            var lic = SignedLicense.Parse(File.ReadAllText(path));
            return InstallLicense(lic, null);
        }
        catch (LicenseFormatException ex) { return (false, ex.Message); }
        catch (IOException ex) { return (false, "The license file could not be opened: " + ex.Message); }
        catch (UnauthorizedAccessException) { return (false, "Windows did not allow reading the license file."); }
    }

    private static (bool ok, string message) InstallLicense(SignedLicense lic, string? expectedKey)
    {
        if (!IsReleaseKeyConfigured)
            return (false, "This build has no licensing key configured. Please download the official version from Burhan Nexus.");
        var st = LicenseEvaluator.Evaluate(lic, TrustedKeys, DeviceIdentity.DeviceId, DateTime.UtcNow, null);
        if (st.State != LicenseState.Active) return (false, st.Message);
        if (expectedKey is not null && !string.Equals(st.LicenseKey, expectedKey, StringComparison.OrdinalIgnoreCase))
            return (false, "The licensing server returned a license for a different key.");
        try { WriteLicense(lic); }
        catch (Exception ex)
        {
            Log.Error("Saving license failed", ex);
            return (false, "The license is valid but could not be saved on this computer: " + ex.Message);
        }
        Reevaluate();
        return (true, $"Thank you! {LicensePlanInfo.DisplayName(st.Plan)} is now active.");
    }

    public static async Task<(bool ok, string message)> DeactivateAsync()
    {
        SignedLicense? lic = null;
        try { lic = ReadLicense(); } catch { }
        if (lic is null) return (false, "There is no license on this computer to deactivate.");
        var data = lic.ReadPayload();
        bool released = false;
        if (Server.IsConfigured)
        {
            released = await Server.DeactivateAsync(data.LicenseKey, DeviceIdentity.DeviceId);
        }
        try { File.Delete(AppPaths.LicenseFile); } catch { }
        Reevaluate();
        return (true, released
            ? "The license was deactivated. You can now activate it on another computer."
            : $"The license was removed from this computer. To move it to another computer, email {LicensingConfig.SupportEmail} with your license key ({data.LicenseKey}) and the new computer's Device ID.");
    }

    /// <summary>Silently renews subscription licenses when online. Never blocks offline use.</summary>
    public static async Task RefreshIfDueAsync()
    {
        try
        {
            if (!Server.IsConfigured) return;
            var lic = ReadLicense();
            if (lic is null) return;
            var data = lic.ReadPayload();
            if (data.ExpiresUtc is not { } exp || exp - DateTime.UtcNow > TimeSpan.FromDays(5)) return;
            var r = await Server.RefreshAsync(data.LicenseKey, DeviceIdentity.DeviceId);
            if (r.Success && r.License is not null) InstallLicense(r.License, data.LicenseKey);
        }
        catch (Exception ex) { Log.Warn("License refresh skipped: " + ex.Message); }
    }

    // ---------------- Storage ----------------

    private static SignedLicense? ReadLicense()
    {
        if (!File.Exists(AppPaths.LicenseFile)) return null;
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(AppPaths.LicenseFile), Entropy, DataProtectionScope.CurrentUser);
        return SignedLicense.Parse(Encoding.UTF8.GetString(bytes));
    }

    private static void WriteLicense(SignedLicense lic)
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(lic.ToFileText()), Entropy, DataProtectionScope.CurrentUser);
        var tmp = AppPaths.LicenseFile + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, AppPaths.LicenseFile, true);
    }

    private static LocalState ReadState()
    {
        try
        {
            if (!File.Exists(AppPaths.TrialFile)) return new LocalState();
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(AppPaths.TrialFile), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<LocalState>(bytes) ?? new LocalState();
        }
        catch { return new LocalState(); }
    }

    private static void WriteState()
    {
        try
        {
            var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(_state), Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(AppPaths.TrialFile, bytes);
        }
        catch (Exception ex) { Log.Warn("State write failed: " + ex.Message); }
    }

    private static string Scramble(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s).Select((b, i) => (byte)(b ^ Entropy[i % Entropy.Length])).ToArray());
    private static string Unscramble(string s)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(s).Select((b, i) => (byte)(b ^ Entropy[i % Entropy.Length])).ToArray()); }
        catch { return ""; }
    }
}
