using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BurhanScreenCapture.Licensing;

namespace BurhanNexus.LicenseKeyGenerator.Core;

/// <summary>
/// Stores the ECDSA private signing key encrypted with the administrator's password
/// (PBKDF2-SHA256, 310 000 iterations → AES-256-GCM). The decrypted key lives only in memory while the
/// generator is unlocked; it is never written to logs, exports or license files.
/// </summary>
public sealed class KeyVault
{
    private sealed class VaultFile
    {
        public int Version { get; set; } = 1;
        public int Iterations { get; set; } = 310_000;
        public string Salt { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string Cipher { get; set; } = "";
        public string Tag { get; set; } = "";
        public string PublicKey { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
    }

    public string FilePath { get; }
    private string? _privateKey;

    public KeyVault(string filePath) => FilePath = filePath;

    public bool Exists => File.Exists(FilePath);
    public bool IsUnlocked => _privateKey is not null;
    public string PublicKey { get; private set; } = "";

    public string PrivateKey => _privateKey ?? throw new InvalidOperationException("The signing key is locked.");

    public static string? CheckPassword(string password, string confirm)
    {
        if (password.Length < 8) return "Use a password of at least 8 characters.";
        if (password != confirm) return "The two passwords do not match.";
        return null;
    }

    public void Create(string password) => Save(LicenseCrypto.CreateKeyPair().privateKeyBase64, password);

    /// <summary>Imports an existing PKCS#8 private key (base64 text file) and protects it with a password.</summary>
    public void Import(string privateKeyBase64, string password)
    {
        LicenseCrypto.PublicKeyFromPrivate(privateKeyBase64); // validates
        Save(privateKeyBase64.Trim(), password);
    }

    private void Save(string privateKey, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 310_000, HashAlgorithmName.SHA256, 32);
        var plain = Encoding.UTF8.GetBytes(privateKey);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag);
        CryptographicOperations.ZeroMemory(key);
        var vf = new VaultFile
        {
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            Cipher = Convert.ToBase64String(cipher),
            Tag = Convert.ToBase64String(tag),
            PublicKey = LicenseCrypto.PublicKeyFromPrivate(privateKey),
            CreatedUtc = DateTime.UtcNow
        };
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(vf, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, true);
        _privateKey = privateKey;
        PublicKey = vf.PublicKey;
    }

    /// <summary>Returns null on success, otherwise a message.</summary>
    public string? Unlock(string password)
    {
        try
        {
            var vf = JsonSerializer.Deserialize<VaultFile>(File.ReadAllText(FilePath))!;
            var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromBase64String(vf.Salt), vf.Iterations, HashAlgorithmName.SHA256, 32);
            var cipher = Convert.FromBase64String(vf.Cipher);
            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(key, 16)) aes.Decrypt(Convert.FromBase64String(vf.Nonce), cipher, Convert.FromBase64String(vf.Tag), plain);
            CryptographicOperations.ZeroMemory(key);
            _privateKey = Encoding.UTF8.GetString(plain);
            PublicKey = LicenseCrypto.PublicKeyFromPrivate(_privateKey);
            if (PublicKey != vf.PublicKey) { Lock(); return "The key file is damaged (public key mismatch)."; }
            return null;
        }
        catch (AuthenticationTagMismatchException) { return "Wrong password."; }
        catch (CryptographicException) { return "Wrong password or damaged key file."; }
        catch (Exception ex) { return "The key file could not be read: " + ex.Message; }
    }

    public void ChangePassword(string newPassword) => Save(PrivateKey, newPassword);

    public void Lock() => _privateKey = null;
}
