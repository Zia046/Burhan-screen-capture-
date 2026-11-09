# Licensing — Burhan Screen Capture Pro

## Overview
* **Free Edition** — no license; all free features, no limits.
* **Pro Trial** — 7 days, started once per computer from the app.
* **Pro Monthly / Yearly / Lifetime** — a signed license file bound to one device.

A license is a small JSON file (`.bslic`) whose payload (plan, license key, device ID, issue and expiry dates,
customer name) is signed with **ECDSA P-256 / SHA-256**. The app embeds only the **public** key, so nobody
can create or modify licenses without the private key. Validation is fully offline, so Pro works without
internet after activation. Stored licenses are additionally protected with Windows DPAPI (per user).
Feature access is enforced in code through one `Entitlements` gate (not by hiding buttons).

Protection included: signature check, device binding (hash of Windows MachineGuid + system volume serial),
expiry, clock roll-back detection (36 h tolerance), tamper detection. As with any offline desktop
licensing, a determined attacker who patches the executable can bypass checks; code signing and an
obfuscator raise that bar further.

## One-time setup before selling (production keys)
1. Build the generator: `dotnet build tools/LicenseGenerator -c Release`
2. Create your key pair **on a trusted offline computer**:
   ```
   dotnet tools/LicenseGenerator/bin/Release/net8.0/LicenseGenerator.dll keygen --out C:\BurhanKeys
   ```
3. Copy the text of `C:\BurhanKeys\public.key` into
   `src/BurhanScreenCapture/Licensing/ProductionPublicKey.txt`, commit and rebuild.
4. Keep `private.key` secret and backed up (USB + password manager). **Never commit it.**
   If it leaks, generate a new pair and release an update.

Until step 3 is done, **Release builds cannot activate any license** (the Upgrade page says so).
Debug builds trust the public development key in `tools/LicenseGenerator/dev-keys/` for testing.

## Selling today — manual (offline) activation
1. Customer buys and receives a license key: `LicenseGenerator newkey`
2. Customer opens **Upgrade to Pro**, copies the **Device ID** and emails it with the key.
3. You issue the file:
   ```
   LicenseGenerator issue --private C:\BurhanKeys\private.key --key BSCP-XXXXX-XXXXX-XXXXX-XXXXX ^
       --plan Yearly --device ABCD-1234-EF56-7890 --to "Customer Name" --out customer.bslic
   ```
   Plans: `Trial`, `Monthly` (31 days), `Yearly` (366 days), `Lifetime` (never expires); override with `--days N`.
4. Customer clicks **Import license file…**.
5. **Renewal**: issue a new file with a later expiry. **Device transfer**: customer clicks
   *Deactivate / transfer*, sends the new Device ID, you issue a new file.

Check a file any time: `LicenseGenerator verify --public public.key --file customer.bslic`

## Online activation (when you have a server)
Set `LicensingConfig.ServerUrl` (HTTPS only) in `src/BurhanScreenCapture/Licensing/LicensingConfig.cs`.
The app then calls:

| Endpoint | Request JSON | Response JSON |
|---|---|---|
| `POST v1/activate` | `{ licenseKey, deviceId, deviceName, product: "BSC-PRO" }` | `{ ok, message, license: { payload, signature } }` |
| `POST v1/refresh` | `{ licenseKey, deviceId, product }` | same as activate |
| `POST v1/deactivate` | `{ licenseKey, deviceId, product }` | `{ ok, message }` |

The server stores keys, plan, expiry and the activated device(s), enforces the device limit and signs the
payload with the private key (same format as `LicenseCrypto.Sign`). The app verifies every server response
locally, so a fake server cannot grant Pro. Subscription licenses are refreshed silently when they are within
5 days of expiry and the computer is online; offline use continues until the expiry date.

No payment or activation service is operational yet — `ServerUrl` is intentionally empty.
