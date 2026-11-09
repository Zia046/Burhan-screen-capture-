using System.Security.Cryptography;
using System.Text;

namespace BurhanScreenCapture.Licensing;

/// <summary>
/// Human-typable license keys: BSCP-XXXXX-XXXXX-XXXXX-CCCCC (Crockford base32).
/// The last group is a checksum that catches typing mistakes. It is NOT a security mechanism:
/// a key only grants Pro after the activation service issues a signed, device-bound license for it.
/// </summary>
public static class LicenseKey
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const string Prefix = "BSCP";

    public static string Generate()
    {
        var body = new char[15];
        var bytes = RandomNumberGenerator.GetBytes(15);
        for (int i = 0; i < 15; i++) body[i] = Alphabet[bytes[i] % 32];
        var b = new string(body);
        return $"{Prefix}-{b[..5]}-{b[5..10]}-{b[10..15]}-{Checksum(b)}";
    }

    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var sb = new StringBuilder();
        foreach (var ch in input.Trim().ToUpperInvariant())
        {
            if (char.IsWhiteSpace(ch)) continue;
            sb.Append(ch switch { 'O' => '0', 'I' => '1', 'L' => '1', _ => ch });
        }
        var s = sb.ToString().Replace("-", "");
        if (s.StartsWith(Prefix)) s = s[Prefix.Length..];
        if (s.Length != 20) return input.Trim().ToUpperInvariant();
        return $"{Prefix}-{s[..5]}-{s[5..10]}-{s[10..15]}-{s[15..20]}";
    }

    public static bool IsWellFormed(string? input)
    {
        var k = Normalize(input);
        var parts = k.Split('-');
        if (parts.Length != 5 || parts[0] != Prefix) return false;
        for (int i = 1; i < 5; i++)
        {
            if (parts[i].Length != 5 || parts[i].Any(c => !Alphabet.Contains(c))) return false;
        }
        var body = parts[1] + parts[2] + parts[3];
        return Checksum(body) == parts[4];
    }

    private static string Checksum(string body)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes("BSC|" + body));
        var c = new char[5];
        for (int i = 0; i < 5; i++) c[i] = Alphabet[hash[i] % 32];
        return new string(c);
    }
}
