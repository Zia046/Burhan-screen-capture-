using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BurhanScreenCapture.Licensing;

namespace BurhanNexus.LicenseKeyGenerator.Core;

public sealed class LicenseRecord
{
    public string LicenseId { get; set; } = "";
    public string LicenseKey { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string? CustomerEmail { get; set; }
    public string Product { get; set; } = "Burhan Screen Capture Pro";
    public string LicenseType { get; set; } = "Lifetime Pro";
    public int DeviceLimit { get; set; } = 1;
    /// <summary>Device ID the key is cryptographically locked to (null = not device-locked).</summary>
    public string? BoundDeviceId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string Status { get; set; } = "Active";           // Active | Revoked
    public DateTime? RevokedUtc { get; set; }
    public string? RevokeReason { get; set; }
    /// <summary>Activation information confirmed by the customer or an activation server.</summary>
    public string? ActivatedDeviceId { get; set; }
    public DateTime? ActivatedUtc { get; set; }
    public string? Notes { get; set; }

    [JsonIgnore] public bool IsRevoked => Status == "Revoked";
    [JsonIgnore] public string CreatedText => CreatedUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm");
    [JsonIgnore] public string DeviceText => BoundDeviceId ?? "Not device-locked";
    [JsonIgnore]
    public string ActivationStatus =>
        IsRevoked ? $"Revoked {RevokedUtc?.ToLocalTime():dd MMM yyyy}" :
        ActivatedUtc is { } a ? $"Activated {a.ToLocalTime():dd MMM yyyy} on {ActivatedDeviceId}" :
        BoundDeviceId is not null ? "Issued — works only on its device" : "Issued — not yet confirmed";
}

/// <summary>
/// Local license database (JSON). Every save is atomic and the previous version is kept in backups/ (last 50).
/// </summary>
public sealed class LicenseDatabase
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string FilePath { get; }
    public string BackupFolder => Path.Combine(Path.GetDirectoryName(FilePath)!, "backups");
    public List<LicenseRecord> Records { get; private set; } = new();

    public LicenseDatabase(string filePath)
    {
        FilePath = filePath;
        Load();
    }

    public void Load()
    {
        if (!File.Exists(FilePath)) { Records = new(); return; }
        Records = JsonSerializer.Deserialize<List<LicenseRecord>>(File.ReadAllText(FilePath)) ?? new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        if (File.Exists(FilePath))
        {
            Directory.CreateDirectory(BackupFolder);
            File.Copy(FilePath, Path.Combine(BackupFolder, $"licenses-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json"), true);
            foreach (var old in new DirectoryInfo(BackupFolder).GetFiles("licenses-*.json").OrderByDescending(f => f.Name).Skip(50))
                old.Delete();
        }
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Records, Json));
        File.Move(tmp, FilePath, true);
    }

    /// <summary>Generates, signs and stores a new Lifetime Pro license. License IDs are never reused.</summary>
    public LicenseRecord Generate(KeyVault vault, string customer, string? email, string? deviceId, int deviceLimit, string? notes)
    {
        if (string.IsNullOrWhiteSpace(customer)) throw new ArgumentException("Customer name is required.");
        deviceId = NormalizeDeviceId(deviceId);
        LicenseData data;
        do
        {
            data = new LicenseData
            {
                LicenseIdBytes = LicenseData.NewLicenseIdBytes(),
                IssuedUtc = DateTime.UtcNow,
                BoundDeviceId = deviceId,
                DeviceLimit = (byte)Math.Clamp(deviceLimit, 1, 255)
            };
        } while (Records.Any(r => r.LicenseId == data.LicenseId));

        var key = LicenseKey.Create(data, vault.PrivateKey);
        // Self-check: the key must verify with the public key before it is handed out.
        var check = LicenseEvaluator.Evaluate(key, new[] { vault.PublicKey }, deviceId ?? "0000-0000-0000-0000");
        if (check.State != LicenseState.Active) throw new InvalidOperationException("Self-check failed: " + check.Message);

        var rec = new LicenseRecord
        {
            LicenseId = data.LicenseId,
            LicenseKey = key,
            CustomerName = customer.Trim(),
            CustomerEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            DeviceLimit = data.DeviceLimit,
            BoundDeviceId = deviceId,
            CreatedUtc = DateTime.UtcNow,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        Records.Insert(0, rec);
        Save();
        return rec;
    }

    public static string? NormalizeDeviceId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var hex = new string(input.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (hex.Length != 16) throw new ArgumentException("A Device ID looks like ABCD-1234-EF56-7890 (16 letters/numbers). Copy it from the customer's Upgrade page.");
        return $"{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}";
    }

    public void Revoke(LicenseRecord r, string reason)
    {
        r.Status = "Revoked";
        r.RevokedUtc = DateTime.UtcNow;
        r.RevokeReason = reason;
        Save();
    }

    public void Reinstate(LicenseRecord r)
    {
        r.Status = "Active";
        r.RevokedUtc = null;
        r.RevokeReason = null;
        Save();
    }

    public void MarkActivated(LicenseRecord r, string deviceId)
    {
        r.ActivatedDeviceId = NormalizeDeviceId(deviceId);
        r.ActivatedUtc = DateTime.UtcNow;
        Save();
    }

    public IEnumerable<LicenseRecord> Search(string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return Records;
        q = q.Trim();
        return Records.Where(r =>
            r.LicenseId.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            r.CustomerName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            (r.CustomerEmail?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (r.BoundDeviceId?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            r.LicenseKey.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>File the customer imports (contains no private data other than their own key).</summary>
    public static string LicenseFileText(LicenseRecord r) => JsonSerializer.Serialize(new
    {
        product = r.Product,
        licenseType = r.LicenseType,
        licensedTo = r.CustomerName,
        licenseId = r.LicenseId,
        issued = r.CreatedUtc.ToString("yyyy-MM-dd"),
        deviceId = r.BoundDeviceId,
        licenseKey = r.LicenseKey,
        howToActivate = "Open Burhan Screen Capture Pro › Upgrade to Pro › Enter License Key › Import license file (or paste the key)."
    }, Json);

    public static string CustomerMessage(LicenseRecord r) =>
        $"Dear {r.CustomerName},\r\n\r\nThank you for purchasing Burhan Screen Capture Pro — Lifetime Pro.\r\n\r\n" +
        $"License ID: {r.LicenseId}\r\n" + (r.BoundDeviceId is null ? "" : $"Device ID: {r.BoundDeviceId}\r\n") +
        $"\r\nYour license key:\r\n{r.LicenseKey}\r\n\r\n" +
        "To activate: open Burhan Screen Capture Pro › Upgrade to Pro › Enter License Key, paste the whole key and click Activate.\r\n" +
        "This is a one-time purchase. Your license never expires.\r\n\r\nBurhan Nexus";

    public string ExportCsv()
    {
        static string C(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder("LicenseId,Customer,Email,Product,Type,DeviceLimit,DeviceId,Created,Status,RevokedUtc,RevokeReason,ActivatedDevice,ActivatedUtc,LicenseKey\r\n");
        foreach (var r in Records)
            sb.AppendLine(string.Join(",", C(r.LicenseId), C(r.CustomerName), C(r.CustomerEmail), C(r.Product), C(r.LicenseType), r.DeviceLimit,
                C(r.BoundDeviceId), C(r.CreatedUtc.ToString("u")), C(r.Status), C(r.RevokedUtc?.ToString("u")), C(r.RevokeReason),
                C(r.ActivatedDeviceId), C(r.ActivatedUtc?.ToString("u")), C(r.LicenseKey)));
        return sb.ToString();
    }

    public string RevocationListText(KeyVault vault) =>
        RevocationList.Create(Records.Where(r => r.IsRevoked).Select(r => r.LicenseId), vault.PrivateKey).ToText();

    public void BackupTo(string folder, KeyVault vault)
    {
        Directory.CreateDirectory(folder);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        File.Copy(FilePath, Path.Combine(folder, $"licenses-{stamp}.json"), true);
        if (File.Exists(vault.FilePath)) File.Copy(vault.FilePath, Path.Combine(folder, $"signing-key-{stamp}.bnxkey"), true); // stays password-encrypted
    }

    public void RestoreFrom(string jsonFile)
    {
        var list = JsonSerializer.Deserialize<List<LicenseRecord>>(File.ReadAllText(jsonFile)) ?? throw new InvalidDataException("Not a license database.");
        Records = list;
        Save();
    }
}
