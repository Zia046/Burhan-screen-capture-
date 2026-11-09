# Changelog

## 1.1.0
* **Fixed:** recording with system audio and/or microphone failed with "Value cannot be null … NULLPTR is not
  supported for this conversion". Audio sources were created without a device name; ScreenRecorderLib converts
  it with `marshal_as`, which rejects null. Every source now gets an explicit endpoint ID (or the Windows default).
* Audio devices are validated before recording; unplugged saved devices fall back to the Windows default;
  microphone privacy blocks are detected up front with a clear message; native recorder log for diagnostics.
* System audio output device selection; "Windows default" entries for speakers and microphone.
* True audio-only recording (system audio, microphone, or both) to `.m4a`.
* Stop is idempotent and shows "Saving…" while the file is finalised.
* **Licensing:** only Free Edition and Lifetime Pro. Monthly, Yearly and the trial were removed.
  Signed `BNX-SC-PRO` keys, optional device lock, signed revocation lists, new Upgrade page, license details
  (type, activation date, License ID, device), About/Settings show edition and license.
* **New:** Burhan Nexus License Key Generator (admin app) with password-encrypted signing key, license database,
  backups, search, export, revoke and signed revocation lists; its own installer.

## 1.0.0 — initial release
* Screenshots: selected area (with window detection), active window, full screen, all monitors, delayed (3/5/10 s),
  PNG/JPG, auto-save, clipboard, preview, global shortcuts, quick toolbar, tray menu.
* Screen recording to MP4: full screen, area, window, audio-only; pause/resume; timer; FPS, quality, resolution;
  system audio + microphone with level meters; crash-safe output; no watermark or time limit.
* Image editor: crop, resize, rotate, flip, arrow, line, rectangle, ellipse, text, colours, thickness, undo/redo,
  save, copy, zoom, drag & drop.
* History with thumbnails (Free: latest 20).
* Pro: scrolling screenshot, repeat last area, fixed-size area, templates, blur, pixelate, magnifier, spotlight,
  highlighter, numbered steps, filled shapes, watermark, JPG quality, BMP/TIFF/GIF export, batch processing,
  OCR, GIF recording, recording profiles, H.265, custom bitrate, unlimited history and search.
* Licensing: 7-day trial, Monthly/Yearly/Lifetime signed device-bound licenses, offline validation,
  license-file import, deactivation/transfer, online activation interface.
* Light/Dark themes, Windows 10/11, high-DPI and multi-monitor support, Inno Setup installer.
