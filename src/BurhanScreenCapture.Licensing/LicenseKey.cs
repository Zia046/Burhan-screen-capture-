using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace BurhanScreenCapture.Licensing;

/// <summary>Decoded content of a Lifetime Pro license key. Every field is covered by the ECDSA signature.</summary>
public sealed record LicenseData
{
    public const byte FormatVersion = 1;
    public const byte ProductScreenCapturePro = 1;

    public byte Version { get; init; } = FormatVersion;
    public byte Product { get; init; } = ProductScreenCapturePro;
    public LicenseType Type { get; init; } = LicenseType.Lifetime;
    public byte DeviceLimit { get; init; } = 1;
    /// <summary>10 cryptographically random bytes — the unique license identifier.</summary>
    public byte[] LicenseIdBytes { get; init; } = Array.Empty<byte>();
    public DateTime IssuedUtc { get; init; }
    /// <summary>When set, the key only activates on this Device ID (XXXX-XXXX-XXXX-XXXX).</summary>
    public string? BoundDeviceId { get; init; }

    /// <summary>Human-readable license ID, e.g. LIC-7K3Q-9TXM-2B8D-HV4N.</summary>
    public string LicenseId => FormatLicenseId(LicenseIdBytes);

    public static string FormatLicenseId(byte[] id)
    {
        var s = Base32.Encode(id).PadRight(16, '0')[..16];
        return $"LIC-{s[..4]}-{s[4..8]}-{s[8..12]}-{s[12..16]}";
    }

    public static byte[] NewLicenseIdBytes() => RandomNumberGenerator.GetBytes(10);
}

/// <summary>
/// License key = "BNX-SC-PRO-" + Crockford-base32( payload ‖ ECDSA-P256-SHA256 signature ), grouped by 4.
/// Payload (v1): version, product, type, deviceLimit, flags, licenseId[10], issuedDays(uint16 LE since 2024-01-01)
/// [, deviceId[8] when flags bit0 is set]. The client app holds only the public key, so keys cannot be forged.
/// </summary>
public static class LicenseKey
{
    public const string Prefix = "BNX-SC-PRO-";
    private static readonly DateTime Epoch = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int SignatureLength = 64;
    private const int BasePayloadLength = 17;

    public static byte[] EncodePayload(LicenseData d)
    {
        bool bound = !string.IsNullOrWhiteSpace(d.BoundDeviceId);
        if (d.LicenseIdBytes.Length != 10) throw new ArgumentException("License ID must be 10 bytes.");
        var p = new byte[BasePayloadLength + (bound ? 8 : 0)];
        p[0] = d.Version;
        p[1] = d.Product;
        p[2] = (byte)d.Type;
        p[3] = d.DeviceLimit;
        p[4] = (byte)(bound ? 1 : 0);
        d.LicenseIdBytes.CopyTo(p, 5);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(15), (ushort)Math.Clamp((d.IssuedUtc.Date - Epoch).TotalDays, 0, ushort.MaxValue));
        if (bound)
        {
            var hex = d.BoundDeviceId!.Replace("-", "").Trim();
            var dev = Convert.FromHexString(hex);
            if (dev.Length != 8) throw new ArgumentException("Device ID must look like XXXX-XXXX-XXXX-XXXX.");
            dev.CopyTo(p, 17);
        }
        return p;
    }

    public static LicenseData DecodePayload(ReadOnlySpan<byte> p)
    {
        if (p.Length < BasePayloadLength) throw new LicenseFormatException("The license key is incomplete.");
        if (p[0] != LicenseData.FormatVersion) throw new LicenseFormatException("This license key was made for a different version.");
        bool bound = (p[4] & 1) != 0;
        if (p.Length != BasePayloadLength + (bound ? 8 : 0)) throw new LicenseFormatException("The license key is incomplete.");
        string? device = null;
        if (bound)
        {
            var hex = Convert.ToHexString(p.Slice(17, 8));
            device = $"{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}";
        }
        return new LicenseData
        {
            Version = p[0],
            Product = p[1],
            Type = (LicenseType)p[2],
            DeviceLimit = p[3],
            LicenseIdBytes = p.Slice(5, 10).ToArray(),
            IssuedUtc = Epoch.AddDays(BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(15, 2))),
            BoundDeviceId = device
        };
    }

    /// <summary>Vendor side: signs the payload and formats the customer-facing key.</summary>
    public static string Create(LicenseData data, string privateKeyBase64)
    {
        var payload = EncodePayload(data);
        var sig = LicenseCrypto.SignBytes(payload, privateKeyBase64);
        if (sig.Length != SignatureLength) throw new CryptographicException("Unexpected signature length.");
        return Format(Base32.Encode(payload.Concat(sig).ToArray()));
    }

    private static string Format(string body)
    {
        var sb = new StringBuilder(Prefix);
        for (int i = 0; i < body.Length; i += 4)
        {
            if (i > 0) sb.Append('-');
            sb.Append(body, i, Math.Min(4, body.Length - i));
        }
        return sb.ToString();
    }

    /// <summary>Splits a pasted key (any case, spaces, line breaks, dashes) into payload and signature.</summary>
    public static (byte[] Payload, byte[] Signature) Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new LicenseFormatException("Please enter your license key.");
        var s = new string(input.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        if (!s.StartsWith(Prefix))
            throw new LicenseFormatException("This is not a Burhan Screen Capture Pro license key. Keys start with BNX-SC-PRO-.");
        var bytes = Base32.Decode(s[Prefix.Length..].Replace("-", ""));
        if (bytes is null) throw new LicenseFormatException("The license key contains invalid characters. Copy and paste it exactly as received.");
        if (bytes.Length < BasePayloadLength + SignatureLength) throw new LicenseFormatException("The license key is incomplete. Copy the whole key.");
        int payloadLen = bytes.Length - SignatureLength;
        if (payloadLen != BasePayloadLength && payloadLen != BasePayloadLength + 8)
            throw new LicenseFormatException("The license key is not valid. Copy the whole key exactly as received.");
        return (bytes[..payloadLen], bytes[payloadLen..]);
    }

    public static string Normalize(string input)
    {
        var (p, s) = Parse(input);
        return Format(Base32.Encode(p.Concat(s).ToArray()));
    }
}

public sealed class LicenseFormatException : Exception
{
    public LicenseFormatException(string message, Exception? inner = null) : base(message, inner) { }
}
