using BurhanScreenCapture.Licensing;

// Burhan Nexus — command-line companion to the License Key Generator (automation / CI).
// Never ship this tool or a private key to customers.
static string Arg(string[] a, string name, string? def = null)
{
    var i = Array.IndexOf(a, name);
    if (i >= 0 && i + 1 < a.Length) return a[i + 1];
    return def ?? throw new ArgumentException($"Missing argument {name}");
}

static int Usage()
{
    Console.WriteLine("""
    Burhan Screen Capture Pro — license CLI (vendor tool)

      keygen      --out <folder>                     create public.key / private.key
      issue       --private <file> [--device <id>] [--out <file>]
                                                     issue a signed Lifetime Pro key (device-locked if --device)
      verify      --public <file> --key <key-or-file> [--device <id>]
      revoke-list --private <file> --ids <LIC-…,LIC-…> --out <file.bnxrev>
    """);
    return 1;
}

try
{
    if (args.Length == 0) return Usage();
    switch (args[0].ToLowerInvariant())
    {
        case "keygen":
        {
            var dir = Arg(args, "--out");
            Directory.CreateDirectory(dir);
            var (pub, prv) = LicenseCrypto.CreateKeyPair();
            File.WriteAllText(Path.Combine(dir, "public.key"), pub);
            File.WriteAllText(Path.Combine(dir, "private.key"), prv);
            Console.WriteLine($"Key pair written to {Path.GetFullPath(dir)} — keep private.key offline.");
            return 0;
        }
        case "issue":
        {
            var prv = File.ReadAllText(Arg(args, "--private")).Trim();
            var dev = Arg(args, "--device", "");
            var data = new LicenseData
            {
                LicenseIdBytes = LicenseData.NewLicenseIdBytes(),
                IssuedUtc = DateTime.UtcNow,
                BoundDeviceId = dev == "" ? null : dev.Trim().ToUpperInvariant()
            };
            var key = LicenseKey.Create(data, prv);
            var outPath = Arg(args, "--out", "");
            if (outPath != "") File.WriteAllText(outPath, key);
            Console.WriteLine(data.LicenseId);
            Console.WriteLine(key);
            return 0;
        }
        case "verify":
        {
            var pub = File.ReadAllText(Arg(args, "--public")).Trim();
            var k = Arg(args, "--key");
            if (File.Exists(k)) k = File.ReadAllText(k);
            var st = LicenseEvaluator.Evaluate(k, new[] { pub }, Arg(args, "--device", "0000-0000-0000-0000"));
            Console.WriteLine($"{st.State}: {st.Message} {st.Data?.LicenseId} device={st.Data?.BoundDeviceId ?? "any"}");
            return st.State == LicenseState.Active ? 0 : 3;
        }
        case "revoke-list":
        {
            var prv = File.ReadAllText(Arg(args, "--private")).Trim();
            var ids = Arg(args, "--ids").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            File.WriteAllText(Arg(args, "--out"), RevocationList.Create(ids, prv).ToText());
            Console.WriteLine($"Signed revocation list with {ids.Length} ID(s).");
            return 0;
        }
        default:
            return Usage();
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("Error: " + ex.Message);
    return 1;
}
