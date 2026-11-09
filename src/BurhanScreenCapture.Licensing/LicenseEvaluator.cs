namespace BurhanScreenCapture.Licensing;

public enum LicenseState
{
    None,
    Active,
    InvalidKey,
    InvalidSignature,
    WrongDevice,
    WrongProduct,
    Revoked
}

public sealed record LicenseStatus(LicenseState State, Edition Edition, string Message, LicenseData? Data = null, string? LicenseKey = null)
{
    public bool IsPro => Edition == Edition.LifetimePro;
    public static LicenseStatus Free(string message = "Free Edition") => new(LicenseState.None, Edition.Free, message);
}

/// <summary>Pure, offline evaluation of a license key against this device and the known revocations.</summary>
public static class LicenseEvaluator
{
    public static LicenseStatus Evaluate(string? key, IEnumerable<string> trustedPublicKeys, string currentDeviceId, ICollection<string>? revokedIds = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return LicenseStatus.Free();
        byte[] payload, sig;
        try { (payload, sig) = LicenseKey.Parse(key); }
        catch (LicenseFormatException ex) { return new(LicenseState.InvalidKey, Edition.Free, ex.Message); }

        if (!LicenseCrypto.VerifyBytes(payload, sig, trustedPublicKeys))
            return new(LicenseState.InvalidSignature, Edition.Free,
                "This license key is not valid. Please check that you copied the complete key, or contact Burhan Nexus.");

        LicenseData data;
        try { data = LicenseKey.DecodePayload(payload); }
        catch (LicenseFormatException ex) { return new(LicenseState.InvalidKey, Edition.Free, ex.Message); }

        if (data.Product != LicenseData.ProductScreenCapturePro || data.Type != LicenseType.Lifetime)
            return new(LicenseState.WrongProduct, Edition.Free, "This license key belongs to a different Burhan Nexus product.", data);

        if (revokedIds is not null && revokedIds.Contains(data.LicenseId))
            return new(LicenseState.Revoked, Edition.Free,
                "This license has been revoked by Burhan Nexus. Free features remain available. Contact support if you think this is a mistake.", data);

        if (data.BoundDeviceId is { } dev && !string.Equals(dev, currentDeviceId, StringComparison.OrdinalIgnoreCase))
            return new(LicenseState.WrongDevice, Edition.Free,
                $"This license key is already activated for another computer (Device ID {dev}). Ask Burhan Nexus for a device transfer.", data);

        return new(LicenseState.Active, Edition.LifetimePro, "LIFETIME PRO ACTIVATED", data, LicenseKey.Normalize(key));
    }
}
