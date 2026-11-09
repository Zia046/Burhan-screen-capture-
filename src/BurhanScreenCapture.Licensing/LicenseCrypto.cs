using System.Security.Cryptography;

namespace BurhanScreenCapture.Licensing;

/// <summary>ECDSA P-256 signing (vendor side only) and verification (application side).</summary>
public static class LicenseCrypto
{
    public static (string publicKeyBase64, string privateKeyBase64) CreateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()),
                Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()));
    }

    public static SignedLicense Sign(LicenseData data, string privateKeyBase64)
    {
        var payload = SignedLicense.SerializePayload(data);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64), out _);
        var signature = ecdsa.SignData(payload, HashAlgorithmName.SHA256);
        return new SignedLicense
        {
            PayloadBase64 = Convert.ToBase64String(payload),
            SignatureBase64 = Convert.ToBase64String(signature)
        };
    }

    public static bool Verify(SignedLicense license, IEnumerable<string> trustedPublicKeysBase64)
    {
        byte[] payload, signature;
        try
        {
            payload = license.PayloadBytes;
            signature = license.SignatureBytes;
        }
        catch (FormatException)
        {
            return false;
        }

        foreach (var key in trustedPublicKeysBase64)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key.Trim()), out _);
                if (ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256))
                    return true;
            }
            catch (CryptographicException) { }
            catch (FormatException) { }
        }
        return false;
    }
}
