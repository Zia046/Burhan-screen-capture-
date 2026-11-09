namespace BurhanScreenCapture.Licensing;

/// <summary>
/// Deployment configuration for licensing. Edit these values before a commercial release (see docs/LICENSING.md).
/// </summary>
public static class LicensingConfig
{
    /// <summary>
    /// HTTPS base URL of the Burhan Nexus licensing server, e.g. "https://license.burhannexus.com/api/".
    /// Empty = online activation disabled; customers activate with a license file instead.
    /// </summary>
    public const string ServerUrl = "";

    public const string PurchaseUrl = "https://burhannexus.com/screen-capture-pro";   // placeholder
    public const string SupportEmail = "support@burhannexus.com";                       // placeholder
    public const string Website = "https://burhannexus.com";                              // placeholder

    public static readonly (LicensePlan Plan, string Price, string Note)[] Plans =
    {
        (LicensePlan.Monthly, "Monthly", "Billed every month • cancel any time"),
        (LicensePlan.Yearly, "Yearly", "Best value • 2 months free"),
        (LicensePlan.Lifetime, "Lifetime", "Pay once • all 1.x updates"),
    };
}
