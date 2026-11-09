using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BurhanScreenCapture.Licensing;

/// <summary>
/// Signed list of revoked license IDs, produced by the License Key Generator. It can be hosted on any static web
/// address (e.g. a GitHub file) and is verified with the same public key, so it cannot be faked or edited.
/// </summary>
public sealed class RevocationList
{
    public sealed class Body
    {
        [JsonPropertyName("issuedUtc")] public DateTime IssuedUtc { get; set; }
        [JsonPropertyName("revoked")] public List<string> RevokedLicenseIds { get; set; } = new();
    }

    [JsonPropertyName("payload")] public string PayloadBase64 { get; set; } = "";
    [JsonPropertyName("signature")] public string SignatureBase64 { get; set; } = "";

    public static RevocationList Create(IEnumerable<string> revokedIds, string privateKeyBase64)
    {
        var body = new Body { IssuedUtc = DateTime.UtcNow, RevokedLicenseIds = revokedIds.Distinct().OrderBy(x => x).ToList() };
        var payload = JsonSerializer.SerializeToUtf8Bytes(body);
        return new RevocationList
        {
            PayloadBase64 = Convert.ToBase64String(payload),
            SignatureBase64 = Convert.ToBase64String(LicenseCrypto.SignBytes(payload, privateKeyBase64))
        };
    }

    public string ToText() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Returns the verified body, or null if the text is malformed or the signature is not trusted.</summary>
    public static Body? ParseVerified(string text, IEnumerable<string> trustedKeys)
    {
        try
        {
            var list = JsonSerializer.Deserialize<RevocationList>(text);
            if (list is null) return null;
            var payload = Convert.FromBase64String(list.PayloadBase64);
            var sig = Convert.FromBase64String(list.SignatureBase64);
            if (!LicenseCrypto.VerifyBytes(payload, sig, trustedKeys)) return null;
            return JsonSerializer.Deserialize<Body>(payload);
        }
        catch { return null; }
    }
}
