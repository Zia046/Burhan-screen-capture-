namespace BurhanScreenCapture.Licensing;

public enum LicenseState
{
    None,
    Active,
    Expired,
    InvalidSignature,
    WrongDevice,
    WrongProduct,
    ClockTampered,
    Corrupted
}

public sealed record LicenseStatus(
    LicenseState State,
    Edition Edition,
    LicensePlan Plan,
    string Message,
    DateTime? ExpiresUtc = null,
    string? LicensedTo = null,
    string? LicenseKey = null)
{
    public bool IsPro => Edition is Edition.Pro or Edition.ProTrial;

    public int? DaysLeft(DateTime nowUtc) =>
        ExpiresUtc is { } e ? Math.Max(0, (int)Math.Ceiling((e - nowUtc).TotalDays)) : null;

    public static LicenseStatus Free(string message = "Free Edition") =>
        new(LicenseState.None, Edition.Free, LicensePlan.Free, message);
}

/// <summary>Pure, testable evaluation of a stored license against this device and the current time.</summary>
public static class LicenseEvaluator
{
    /// <summary>Tolerance for clocks moving backwards (time zone/DST/sync corrections).</summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromHours(36);

    public static LicenseStatus Evaluate(
        SignedLicense? license,
        IEnumerable<string> trustedPublicKeys,
        string currentDeviceId,
        DateTime nowUtc,
        DateTime? lastSeenUtc)
    {
        if (license is null) return LicenseStatus.Free();

        if (!LicenseCrypto.Verify(license, trustedPublicKeys))
            return new(LicenseState.InvalidSignature, Edition.Free, LicensePlan.Free,
                "The license signature is not valid. Please activate again or contact Burhan Nexus support.");

        LicenseData data;
        try { data = license.ReadPayload(); }
        catch (Exception) { return new(LicenseState.Corrupted, Edition.Free, LicensePlan.Free, "The license data is corrupted."); }

        if (data.Product != LicenseData.ProductCode)
            return new(LicenseState.WrongProduct, Edition.Free, LicensePlan.Free, "This license belongs to a different product.");

        if (!string.Equals(data.DeviceId, currentDeviceId, StringComparison.OrdinalIgnoreCase))
            return new(LicenseState.WrongDevice, Edition.Free, data.Plan,
                "This license is activated for a different computer. Deactivate it there or request a device transfer.",
                data.ExpiresUtc, data.LicensedTo, data.LicenseKey);

        if (lastSeenUtc is { } seen && nowUtc + ClockTolerance < seen)
            return new(LicenseState.ClockTampered, Edition.Free, data.Plan,
                "The system clock appears to have been moved backwards. Please correct the date and time.",
                data.ExpiresUtc, data.LicensedTo, data.LicenseKey);

        if (data.ExpiresUtc is { } exp && nowUtc >= exp)
            return new(LicenseState.Expired, Edition.Free, data.Plan,
                data.Plan == LicensePlan.Trial
                    ? "Your 7-day Pro trial has ended. Free features remain available."
                    : $"Your {LicensePlanInfo.DisplayName(data.Plan)} license expired on {exp.ToLocalTime():d}. Free features remain available.",
                exp, data.LicensedTo, data.LicenseKey);

        var edition = data.Plan == LicensePlan.Trial ? Edition.ProTrial : Edition.Pro;
        return new(LicenseState.Active, edition, data.Plan,
            $"{LicensePlanInfo.DisplayName(data.Plan)} — active", data.ExpiresUtc, data.LicensedTo, data.LicenseKey);
    }
}
