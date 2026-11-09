using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BurhanScreenCapture.Licensing;

/// <summary>
/// Wire/storage format of a license file (*.bslic): the exact payload bytes plus an ECDSA P-256 / SHA-256 signature.
/// The payload is kept as the original bytes so verification never depends on re-serialisation.
/// </summary>
public sealed class SignedLicense
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [JsonPropertyName("payload")] public string PayloadBase64 { get; set; } = "";
    [JsonPropertyName("signature")] public string SignatureBase64 { get; set; } = "";

    public byte[] PayloadBytes => Convert.FromBase64String(PayloadBase64);
    public byte[] SignatureBytes => Convert.FromBase64String(SignatureBase64);

    public LicenseData ReadPayload()
    {
        var data = JsonSerializer.Deserialize<LicenseData>(PayloadBytes, JsonOptions);
        return data ?? throw new LicenseFormatException("License payload is empty.");
    }

    public static byte[] SerializePayload(LicenseData data) =>
        JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);

    public string ToFileText() => JsonSerializer.Serialize(this, JsonOptions);

    public static SignedLicense Parse(string fileText)
    {
        if (string.IsNullOrWhiteSpace(fileText))
            throw new LicenseFormatException("The license file is empty.");
        try
        {
            var lic = JsonSerializer.Deserialize<SignedLicense>(fileText, JsonOptions);
            if (lic is null || string.IsNullOrEmpty(lic.PayloadBase64) || string.IsNullOrEmpty(lic.SignatureBase64))
                throw new LicenseFormatException("The license file is incomplete.");
            _ = lic.PayloadBytes;
            _ = lic.SignatureBytes;
            return lic;
        }
        catch (JsonException ex)
        {
            throw new LicenseFormatException("The license file is not a valid Burhan Screen Capture license.", ex);
        }
        catch (FormatException ex)
        {
            throw new LicenseFormatException("The license file is corrupted.", ex);
        }
    }

    public static string Utf8(byte[] b) => Encoding.UTF8.GetString(b);
}

public sealed class LicenseFormatException : Exception
{
    public LicenseFormatException(string message, Exception? inner = null) : base(message, inner) { }
}
