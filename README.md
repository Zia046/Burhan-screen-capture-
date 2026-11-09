<p align="center"><img src="assets/logo.png" width="96" alt="Burhan Screen Capture Pro"></p>

# Burhan Screen Capture Pro

**Capture. Create. Communicate.** — a lightweight Windows 10/11 app by **Burhan Nexus** for screenshots,
screen recording with system audio and microphone, and image editing.

Two editions only: **Free Edition** (free forever) and **Lifetime Pro** (one-time purchase, never expires,
no subscription).

| | Free | Lifetime Pro |
|---|:---:|:---:|
| Area / window / full-screen / multi-monitor / delayed screenshots | ✔ | ✔ |
| PNG & JPG, auto-save, clipboard, preview, global shortcuts, quick toolbar | ✔ | ✔ |
| Screen recording to MP4 (full screen, area, window), pause/resume, FPS, quality, resolution | ✔ | ✔ |
| System audio (WASAPI loopback) + microphone, output/mic selection, level meters | ✔ | ✔ |
| Audio-only recording (system audio and/or microphone → .m4a) | ✔ | ✔ |
| **No watermark, no ads, no time limit** | ✔ | ✔ |
| Editor: crop, resize, rotate, flip, arrows, lines, rectangles, circles, text, colours, undo/redo | ✔ | ✔ |
| History | latest 20 | unlimited + search |
| Scrolling screenshot, repeat-last-area, fixed-size area, screenshot templates | | ✔ |
| Blur, pixelate, magnifier, spotlight, highlighter, numbered steps, filled shapes, watermark | | ✔ |
| GIF recording, recording profiles, H.265/HEVC, custom bitrate | | ✔ |
| OCR (offline, Windows OCR), batch processing, JPG quality control, BMP/TIFF/GIF export | | ✔ |

## Download

Every push to `main` builds on GitHub Actions (Windows). Open **Actions → Build → latest run → Artifacts**:

* `BurhanScreenCapturePro-x.y.z` → `BurhanScreenCapturePro-Setup-x.y.z.exe` (customer installer) and a portable zip
* `ADMIN-ONLY-BurhanNexusLicenseKeyGenerator-x.y.z` → the License Key Generator installer (**keep private**)

Pushing a tag such as `v1.0.0` also creates a GitHub Release with both files.

## Technology

* C# / .NET 8 / WPF, x64, Per-Monitor-V2 DPI aware; self-contained publish (no .NET install needed)
* Screenshots: GDI BitBlt (fast, no dependencies)
* Recording: [ScreenRecorderLib](https://github.com/sskodje/ScreenRecorderLib) (MIT) — Media Foundation H.264/H.265 + AAC, Desktop Duplication / Windows Graphics Capture, WASAPI loopback + microphone mixed on one clock
* Audio meters & device list: [NAudio](https://github.com/naudio/NAudio) (MIT)
* OCR: `Windows.Media.Ocr` (built into Windows)
* Audio-only: NAudio WASAPI loopback + microphone → 48 kHz stereo mix → AAC via Media Foundation
* Licensing: Free / Lifetime Pro, ECDSA P-256 signed `BNX-SC-PRO` keys, optional device lock, signed revocation lists, offline (see [docs/LICENSING.md](docs/LICENSING.md))
* Installer: Inno Setup 6

## Repository layout

```
src/BurhanScreenCapture/            WPF application
src/BurhanScreenCapture.Licensing/  license format, signature verification, Free/Pro feature catalog
tools/LicenseKeyGenerator/          Burhan Nexus License Key Generator (WPF admin app)
tools/LicenseGenerator/             command-line license tool (automation / CI)
tests/BurhanScreenCapture.Tests/    unit tests (licensing, entitlements)
installer/                          Inno Setup script and branded wizard images
docs/                               user guide, licensing, build instructions, EULA
```

## Documentation

* [User Guide](docs/USER_GUIDE.md)
* [Licensing & activation](docs/LICENSING.md)
* [Build instructions](docs/BUILD.md)
* [Changelog](CHANGELOG.md)

© 2026 Burhan Nexus. All rights reserved.
