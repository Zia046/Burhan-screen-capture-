using System.Text.Json.Serialization;

namespace BurhanScreenCapture.Licensing;

/// <summary>The signed content of a license. Every field is covered by the signature.</summary>
public sealed class LicenseData
{
    public const string ProductCode = "BSC-PRO";

    [JsonPropertyName("v")] public int FormatVersion { get; set; } = 1;
    [JsonPropertyName("product")] public string Product { get; set; } = ProductCode;
    [JsonPropertyName("licenseId")] public string LicenseId { get; set; } = "";
    [JsonPropertyName("licenseKey")] public string LicenseKey { get; set; } = "";
    [JsonPropertyName("plan")] public LicensePlan Plan { get; set; }
    [JsonPropertyName("licensedTo")] public string LicensedTo { get; set; } = "";
    [JsonPropertyName("deviceId")] public string DeviceId { get; set; } = "";
    [JsonPropertyName("issuedUtc")] public DateTime IssuedUtc { get; set; }
    /// <summary>Null means the license never expires (Lifetime).</summary>
    [JsonPropertyName("expiresUtc")] public DateTime? ExpiresUtc { get; set; }
}
