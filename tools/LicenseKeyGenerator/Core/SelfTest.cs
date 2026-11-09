using System.Diagnostics;
using BurhanScreenCapture.Licensing;

namespace BurhanNexus.LicenseKeyGenerator.Core;

/// <summary>CI checks for the generator: vault, password, unique generation, export files, revocation list.</summary>
public static class SelfTest
{
    public static async Task<int> RunAsync(string outDir, string? importPrivate, string? device)
    {
        await Task.Yield();
        Directory.CreateDirectory(outDir);
        var lines = new List<string>();
        bool failed = false;
        void Check(string name, Func<string> f)
        {
            var sw = Stopwatch.StartNew();
            try { lines.Add($"PASS  {name}  ({sw.ElapsedMilliseconds} ms)  {f()}"); }
            catch (Exception ex) { failed = true; lines.Add($"FAIL  {name}  {ex.Message}"); }
        }
        static void Assert(bool c, string m) { if (!c) throw new Exception("ASSERT: " + m); }

        var dataDir = Path.Combine(outDir, "data");
        if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true);
        var vault = new KeyVault(Path.Combine(dataDir, "signing-key.bnxkey"));
        const string pw = "Test-Password-123";

        Check("Create or import signing key (password-encrypted)", () =>
        {
            if (importPrivate is not null) vault.Import(File.ReadAllText(importPrivate), pw);
            else vault.Create(pw);
            var fileText = File.ReadAllText(vault.FilePath);
            Assert(!fileText.Contains(vault.PrivateKey), "private key stored in clear text");
            return "public key " + vault.PublicKey[..24] + "…";
        });
        Check("Wrong password rejected, right password unlocks", () =>
        {
            var v2 = new KeyVault(vault.FilePath);
            Assert(v2.Unlock("wrong-password") is not null && !v2.IsUnlocked, "wrong password accepted");
            Assert(v2.Unlock(pw) is null && v2.PublicKey == vault.PublicKey, "unlock failed");
            return "ok";
        });

        var db = new LicenseDatabase(Path.Combine(dataDir, "licenses.json"));
        LicenseRecord? bound = null, unbound = null, other = null;
        Check("Generate 200 unique Lifetime licenses", () =>
        {
            for (int i = 0; i < 200; i++) db.Generate(vault, $"Customer {i}", $"c{i}@example.com", null, 1, null);
            Assert(db.Records.Select(r => r.LicenseId).Distinct().Count() == 200, "duplicate IDs");
            Assert(db.Records.Select(r => r.LicenseKey).Distinct().Count() == 200, "duplicate keys");
            return $"example {db.Records[0].LicenseKey[..30]}… ({db.Records[0].LicenseKey.Length} chars)";
        });
        Check("Device-locked license for the test computer", () =>
        {
            bound = db.Generate(vault, "Test Customer", "test@example.com", device ?? "ABCD-1234-ABCD-1234", 1, "CI");
            other = db.Generate(vault, "Other PC", null, "FFFF-0000-FFFF-0000", 1, null);
            unbound = db.Generate(vault, "Unlocked key", null, null, 1, null);
            var st = LicenseEvaluator.Evaluate(bound.LicenseKey, new[] { vault.PublicKey }, bound.BoundDeviceId!);
            Assert(st.State == LicenseState.Active && st.Data!.LicenseId == bound.LicenseId, st.Message);
            Assert(LicenseEvaluator.Evaluate(bound.LicenseKey, new[] { vault.PublicKey }, "1111-1111-1111-1111").State == LicenseState.WrongDevice, "device lock");
            return bound.LicenseId;
        });
        Check("Database persisted with automatic backups", () =>
        {
            var reload = new LicenseDatabase(db.FilePath);
            Assert(reload.Records.Count == db.Records.Count, "reload count");
            Assert(Directory.GetFiles(db.BackupFolder).Length > 0, "no backups");
            Assert(!File.ReadAllText(db.FilePath).Contains(vault.PrivateKey), "private key leaked into database");
            return $"{reload.Records.Count} records, {Directory.GetFiles(db.BackupFolder).Length} backups";
        });
        Check("Search", () =>
        {
            Assert(db.Search("Test Customer").Single().LicenseId == bound!.LicenseId, "by name");
            Assert(db.Search(bound!.LicenseId).Count() == 1, "by id");
            return "ok";
        });
        Check("Export license files + customer message", () =>
        {
            File.WriteAllText(Path.Combine(outDir, "customer.bnxlic"), LicenseDatabase.LicenseFileText(bound!));
            File.WriteAllText(Path.Combine(outDir, "other-device.bnxlic"), LicenseDatabase.LicenseFileText(other!));
            File.WriteAllText(Path.Combine(outDir, "unbound.bnxlic"), LicenseDatabase.LicenseFileText(unbound!));
            var text = File.ReadAllText(Path.Combine(outDir, "customer.bnxlic"));
            Assert(text.Contains(bound!.LicenseKey) && !text.Contains(vault.PrivateKey), "export content");
            Assert(LicenseDatabase.CustomerMessage(bound).Contains(bound.LicenseKey), "message");
            File.WriteAllText(Path.Combine(outDir, "licenses.csv"), db.ExportCsv());
            return "customer.bnxlic, other-device.bnxlic, unbound.bnxlic, licenses.csv";
        });
        Check("Revoke + signed revocation list", () =>
        {
            db.Revoke(bound!, "CI revocation test");
            var text = db.RevocationListText(vault);
            File.WriteAllText(Path.Combine(outDir, "revocations.bnxrev"), text);
            var body = RevocationList.ParseVerified(text, new[] { vault.PublicKey });
            Assert(body is not null && body.RevokedLicenseIds.Contains(bound!.LicenseId), "list");
            var st = LicenseEvaluator.Evaluate(bound!.LicenseKey, new[] { vault.PublicKey }, bound.BoundDeviceId!, body!.RevokedLicenseIds);
            Assert(st.State == LicenseState.Revoked, st.State.ToString());
            db.Reinstate(bound!);
            return "revoked → rejected; reinstated for app test";
        });
        Check("Backup to folder", () =>
        {
            var b = Path.Combine(outDir, "backup");
            db.BackupTo(b, vault);
            Assert(Directory.GetFiles(b, "licenses-*.json").Length == 1 && Directory.GetFiles(b, "signing-key-*.bnxkey").Length == 1, "backup files");
            return "ok";
        });
        File.WriteAllText(Path.Combine(outDir, "public.key"), vault.PublicKey);
        // Revocation list for the app test: revoke the bound license in a separate signed list
        File.WriteAllText(Path.Combine(outDir, "revoke-customer.bnxrev"), RevocationList.Create(new[] { bound!.LicenseId }, vault.PrivateKey).ToText());
        File.WriteAllLines(Path.Combine(outDir, "results.txt"), lines);
        return failed ? 1 : 0;
    }
}
