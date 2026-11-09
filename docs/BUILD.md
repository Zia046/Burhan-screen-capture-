# Build instructions

## Requirements
* Windows 10 1809+ or Windows 11 (x64)
* .NET 8 SDK — https://dotnet.microsoft.com/download/dotnet/8.0
* Visual Studio 2022 17.8+ (optional) with *.NET desktop development*
* Inno Setup 6 (for the installer) — https://jrsoftware.org/isinfo.php
* Microsoft Visual C++ 2015-2022 x64 runtime (needed at run time by ScreenRecorderLib; the installer adds it)

## Build and run
```
dotnet test tests/BurhanScreenCapture.Tests
dotnet build src/BurhanScreenCapture/BurhanScreenCapture.csproj -c Debug
dotnet run --project src/BurhanScreenCapture
```
Or open `BurhanScreenCapture.sln` in Visual Studio, choose **Debug | x64**, press F5.
The project always builds as x64 (ScreenRecorderLib is native x64).

## Release package
```
dotnet publish src/BurhanScreenCapture/BurhanScreenCapture.csproj -c Release -r win-x64 ^
    --self-contained true -p:PublishReadyToRun=true -p:Version=1.0.0 -o publish\app
curl -L -o installer\redist\vc_redist.x64.exe https://aka.ms/vs/17/release/vc_redist.x64.exe
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DAppVersion=1.0.0 /DPublishDir=..\publish\app installer\BurhanScreenCapture.iss
```
Output: `artifacts\BurhanScreenCapturePro-Setup-1.0.0.exe`.
Before a commercial release, add your production public key (see `docs/LICENSING.md`).

## Automated build (GitHub Actions)
`.github/workflows/build.yml` runs on every push on a Windows runner:
unit tests → Debug build → licensing end-to-end with the generator → **self-test on a real Windows desktop**
(screenshots, recordings, editor, GIF, OCR, Free/Pro gating, activation/expiry/tamper/trial, UI snapshots in
light and dark) → self-contained Release publish → start-up smoke test → installer → portable zip.
Tag `vX.Y.Z` to publish a GitHub Release.

Run the self-test yourself:
```
BurhanScreenCapture.exe --selftest C:\temp\bsc-selftest
```
Results are in `results.txt`; UI snapshots and sample outputs are written next to it.

## Code signing
Unsigned apps trigger SmartScreen warnings. With a code-signing certificate:
```
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a publish\app\BurhanScreenCapture.exe
```
and enable `SignTool=` in `installer/BurhanScreenCapture.iss` to sign the installer and uninstaller.

## Logo
`assets/logo.png` and `assets/app.ico` (copied into `src/BurhanScreenCapture/Assets`) are placeholders.
Replace them with the official Burhan Nexus logo (same file names; `.ico` should contain 16–256 px sizes)
and rebuild. The splash screen is `src/BurhanScreenCapture/Assets/splash.png` (520×300).
