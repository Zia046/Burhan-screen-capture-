# Licensing — Burhan Screen Capture Pro

There are exactly two editions:

| | Free Edition | Lifetime Pro |
|---|---|---|
| Price | Free forever | One-time purchase |
| Key needed | No | Yes (`BNX-SC-PRO-…`) |
| Expires | Never | Never |
| Recurring payment | — | None (no monthly/yearly plans, no renewal) |

Screen recording, system audio and microphone recording are **free**, with no watermark and no time limit.

## How a license key works

```
BNX-SC-PRO-XXXX-XXXX-…            (≈ 175 characters, paste — don't type)
            └── Crockford base32 of:  payload ‖ ECDSA P-256 / SHA-256 signature (64 bytes)

payload v1 (17 or 25 bytes)
  version | product | type=Lifetime | deviceLimit | flags | licenseId[10 random bytes] | issuedDay
  [ | deviceId[8] ]   ← present when the key is locked to one computer
```

* The **private signing key** exists only in the *Burhan Nexus License Key Generator*, encrypted with the
  administrator's password (PBKDF2-SHA256 310 000 iterations → AES-256-GCM). It is never written to the
  repository, the app, logs or customer files.
* The app embeds only the **public key** (`src/BurhanScreenCapture/Licensing/ProductionPublicKey.txt`).
  Changing one character of a key, or signing with any other private key, makes it invalid.
* **License ID** (`LIC-XXXX-XXXX-XXXX-XXXX`) is 80 bits from a cryptographic RNG; the generator also refuses duplicates.
* **Device lock (recommended):** the customer sends the Device ID shown in *Upgrade to Pro*; the key then works only
  on that computer. Keys issued without a Device ID work on any computer (use only for trusted customers).
* **Activation** is offline: the app verifies the signature, device lock and revocation list, then stores an
  activation record (key, activation date, device) protected with Windows DPAPI for that Windows user. There is
  no Boolean "isPro" flag; every start re-verifies the signature. Activated customers are never asked again.
* **Revocation:** the generator exports a **signed** `revocations.bnxrev`. The app accepts it from
  `LicensingConfig.RevocationListUrl` (checked in the background when online, cached for offline use) and from
  the built-in `Licensing/RevokedLicenses.txt`. Forged lists are ignored.

### Honest limits of offline licensing
Without an activation server nobody can see globally which computers activated a key. Device-locked keys solve
sharing for normal users. An unlocked key can be shared; revoke it and publish the list. A computer that never
goes online and never updates the app cannot learn about a revocation. Like all desktop licensing, a patched
executable can bypass checks; code signing raises that bar.

## One-time setup (before selling)

1. Install the generator (`BurhanNexusLicenseKeyGenerator-Setup-x.y.z.exe`, **admin artifact — never give it to customers**).
2. First start → **Create signing key** with a strong password.
3. **Signing Key & Backup → Copy public key**, paste it into
   `src/BurhanScreenCapture/Licensing/ProductionPublicKey.txt` on GitHub and commit. CI builds new installers that
   accept your keys. (Until then the shipping app shows "Developer build: no licensing public key…".)
4. **Back up** the database + encrypted key to a USB drive. If the key is lost you cannot issue keys accepted by
   apps already installed.

## Selling a license

1. Customer pays you (bank transfer, JazzCash, Easypaisa, … — payment is outside the software; nothing is faked).
2. Customer sends their **Device ID** (Upgrade to Pro → Purchase Lifetime License → Email Burhan Nexus does this).
3. Generator → **Generate New License**: customer name, email, Device ID → **Generate License**.
4. **Copy customer message** (or **Export License…** for a `.bnxlic` file) and send it.
5. Customer: **Upgrade to Pro → Enter License Key → paste → Activate** → *LICENSE STATUS: LIFETIME PRO ACTIVATED*.

**Device transfer:** customer clicks *Deactivate / transfer*, sends the new Device ID; issue a new key for it and
revoke the old one. **Refund/abuse:** *License History → Revoke*, then *Revocations → Export signed revocation list*
and publish it.

## Generator data
`%AppData%\Burhan Nexus\License Key Generator\`
* `signing-key.bnxkey` — encrypted private key + public key
* `licenses.json` — license database (ID, key, customer, email, product, type, device limit, device, created,
  status, revocation, activation info, notes)
* `backups\` — automatic copy before every change (last 50)

## Optional activation server
Set `LicensingConfig.ServerUrl` (HTTPS) to add central tracking. The app then calls:

| Endpoint | Request JSON | Response JSON |
|---|---|---|
| `POST v1/activate` | `{ licenseKey, deviceId, deviceName, product }` | `{ ok, message, licenseKey? }` (a device-locked key signed by the server) |
| `POST v1/deactivate` | `{ licenseKey, deviceId, product }` | `{ ok, message }` |
| `GET v1/revocations` | — | signed revocation list text |

If the server refuses (revoked, device limit), activation fails with its message. If it is unreachable, a valid
signed key still activates offline. No server is included or running today.

## Command-line tool (automation)
`tools/LicenseGenerator`: `keygen`, `issue [--device]`, `verify`, `revoke-list` — used by CI.
Debug builds also trust `tools/LicenseGenerator/dev-keys/dev-test-public.txt` (its private key is public — test only).
