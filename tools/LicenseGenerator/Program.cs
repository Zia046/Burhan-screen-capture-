using BurhanScreenCapture.Licensing;

// Burhan Nexus — vendor-side license tool. Never ship this tool or a production private key to customers.
static string Arg(string[] a, string name, string? def = null)
{
    var i = Array.IndexOf(a, name);
    if (i >= 0 && i + 1 < a.Length) return a[i + 1];
    return def ?? throw new ArgumentException($"Missing argument {name}");
}

static int Usage()
{
    Console.WriteLine("""
    Burhan Screen Capture Pro — License Generator (vendor tool)

      keygen   --out <folder>
               Creates a production key pair: public.key (commit into the app) and private.key (keep OFFLINE).
      newkey   [--count N]
               Prints new license keys (BSCP-XXXXX-XXXXX-XXXXX-XXXXX).
      issue    --private <private key file> --key <license key> --plan <Trial|Monthly|Yearly|Lifetime>
               --device <device id from the app> [--to <customer name>] [--days <n>] --out <file.bslic>
               Issues a signed, device-bound license file.
      verify   --public <public key file> --file <file.bslic> [--device <device id>]
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
            Console.WriteLine($"Key pair written to {Path.GetFullPath(dir)}");
            Console.WriteLine("Copy public.key into src/BurhanScreenCapture/Licensing/ProductionPublicKey.txt.");
            Console.WriteLine("Store private.key OFFLINE and never commit it.");
            return 0;
        }
        case "newkey":
        {
            var n = int.Parse(Arg(args, "--count", "1"));
            for (int i = 0; i < n; i++) Console.WriteLine(LicenseKey.Generate());
            return 0;
        }
        case "issue":
        {
            var prv = File.ReadAllText(Arg(args, "--private")).Trim();
            var key = LicenseKey.Normalize(Arg(args, "--key"));
            if (!LicenseKey.IsWellFormed(key)) { Console.Error.WriteLine("License key is not well-formed."); return 2; }
            var plan = Enum.Parse<LicensePlan>(Arg(args, "--plan"), true);
            if (plan == LicensePlan.Free) { Console.Error.WriteLine("Free does not need a license."); return 2; }
            var device = Arg(args, "--device").Trim().ToUpperInvariant();
            var now = DateTime.UtcNow;
            var daysArg = Arg(args, "--days", "");
            DateTime? expires = daysArg != "" ? now.AddDays(int.Parse(daysArg))
                : LicensePlanInfo.DefaultDuration(plan) is { } d ? now + d : null;
            var data = new LicenseData
            {
                LicenseId = Guid.NewGuid().ToString("N"),
                LicenseKey = key,
                Plan = plan,
                LicensedTo = Arg(args, "--to", ""),
                DeviceId = device,
                IssuedUtc = now,
                ExpiresUtc = expires
            };
            var signed = LicenseCrypto.Sign(data, prv);
            var outPath = Arg(args, "--out");
            File.WriteAllText(outPath, signed.ToFileText());
            Console.WriteLine($"Issued {LicensePlanInfo.DisplayName(plan)} for device {device} -> {Path.GetFullPath(outPath)}");
            Console.WriteLine(expires is null ? "Never expires." : $"Expires {expires:yyyy-MM-dd HH:mm} UTC");
            return 0;
        }
        case "verify":
        {
            var pub = File.ReadAllText(Arg(args, "--public")).Trim();
            var lic = SignedLicense.Parse(File.ReadAllText(Arg(args, "--file")));
            var data = lic.ReadPayload();
            var device = Arg(args, "--device", data.DeviceId);
            var st = LicenseEvaluator.Evaluate(lic, new[] { pub }, device, DateTime.UtcNow, null);
            Console.WriteLine($"{st.State}: {st.Message}");
            Console.WriteLine($"Plan={data.Plan} Device={data.DeviceId} To={data.LicensedTo} Expires={data.ExpiresUtc?.ToString("u") ?? "never"}");
            return st.State == LicenseState.Active ? 0 : 3;
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
