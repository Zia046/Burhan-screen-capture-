using System.Security.Cryptography;

namespace BurhanScreenCapture.Licensing;

/// <summary>ECDSA P-256 / SHA-256 (IEEE P1363, 64-byte signatures). Private key: vendor only. Public key: embedded in the app.</summary>
public static class LicenseCrypto
{
    public static (string publicKeyBase64, string privateKeyBase64) CreateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()),
                Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()));
    }

    public static string PublicKeyFromPrivate(string privateKeyBase64)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64.Trim()), out _);
        return Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
    }

    public static byte[] SignBytes(byte[] data, string privateKeyBase64)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64.Trim()), out _);
        return ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    public static bool VerifyBytes(byte[] data, byte[] signature, IEnumerable<string> trustedPublicKeysBase64)
    {
        foreach (var key in trustedPublicKeysBase64)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key.Trim()), out _);
                if (ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                    return true;
            }
            catch (CryptographicException) { }
            catch (FormatException) { }
        }
        return false;
    }
}
