namespace BurhanScreenCapture.Licensing;

/// <summary>
/// Deployment configuration for licensing. Edit these values before a commercial release (see docs/LICENSING.md).
/// </summary>
public static class LicensingConfig
{
    /// <summary>
    /// Optional HTTPS base URL of a Burhan Nexus activation server (see docs/LICENSING.md, "Online activation").
    /// Empty = keys are validated offline only (signature + device binding + revocation list).
    /// </summary>
    public const string ServerUrl = "";

    /// <summary>
    /// Optional HTTPS address of the signed revocation list exported by the License Key Generator
    /// (for example a raw GitHub file). Checked in the background at startup; empty = only the list built into the app.
    /// </summary>
    public const string RevocationListUrl = "";

    // Contact details shown to customers — replace with your real details.
    public const string PurchaseUrl = "https://burhannexus.com/screen-capture-pro";
    public const string SupportEmail = "support@burhannexus.com";
    public const string Website = "https://burhannexus.com";
}
