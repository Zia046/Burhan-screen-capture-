namespace BurhanScreenCapture.Licensing;

/// <summary>Only two editions exist: Free forever, and Lifetime Pro (one-time purchase, never expires).</summary>
public enum Edition
{
    Free,
    LifetimePro
}

public enum LicenseType : byte
{
    Lifetime = 1
}

public static class EditionInfo
{
    public static string Name(Edition e) => e == Edition.LifetimePro ? "Lifetime Pro" : "Free Edition";
    public static string LicenseText(Edition e) => e == Edition.LifetimePro ? "Permanently Activated" : "Free Forever";
}
