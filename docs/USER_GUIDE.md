# Burhan Screen Capture Pro — User Guide

## Getting started
After installation the app starts in the notification area (system tray) and is ready for shortcuts.
Left-click the tray icon to open the dashboard; right-click for quick actions.

## Default shortcuts
| Shortcut | Action |
|---|---|
| Print Screen | Capture a selected area |
| Ctrl + Print Screen | Capture the screen under the mouse |
| Alt + Print Screen | Capture the active window |
| Ctrl + Shift + R | Start / stop recording |
| Ctrl + Shift + E | Open the image editor |
| Ctrl + Shift + T | Show the quick toolbar |

Change them in **Settings › Keyboard shortcuts**.
**Windows 11:** if Print Screen opens the Snipping Tool instead, turn off
*Settings › Accessibility › Keyboard › Use the Print screen key to open screen capture*.

## Taking screenshots
* **Selected area** — drag a rectangle. A single click captures the window under the mouse (it is highlighted).
  `Enter` or double-click accepts, `Ctrl+A` selects everything, `Esc`/right-click cancels.
* **Full screen**, **Active window**, **All monitors** — from the dashboard, Capture page, tray menu or toolbar.
* **Delayed** — 3, 5 or 10 seconds; right-click the countdown bubble to cancel.
* After capture the image is saved (if *auto-save* is on), copied to the clipboard and shown in a preview
  (or opened in the editor — choose in Settings).

## Recording the screen
1. Open **Screen Recorder**, choose *Full screen*, *Selected area*, *Window* or *Audio only*.
2. Turn **System audio** and/or **Microphone** on. The level bars show that sound is arriving.
3. Choose frame rate, quality and resolution, then press **Start Recording**.
4. A small control bar shows the timer and audio levels, with **Pause/Resume**, **Stop** and **Discard**.
   The bar itself is not recorded.

Recordings are MP4 (H.264 video, AAC audio). Files are written in a crash-safe format, so a power cut
still leaves a playable file. If an audio device is unplugged or the display changes, recording stops and
the part recorded so far is kept.

**Microphone not working?** Windows *Settings › Privacy & security › Microphone* must allow desktop apps.

## Image editor
Tools (left): Select (V), Arrow (A), Line (L), Rectangle (R), Ellipse (E), Text (T), Crop (C) and the Pro tools.
Pick a colour and thickness on the right. Hold **Shift** for squares/circles and straight lines.
`Ctrl+Z`/`Ctrl+Y` undo/redo, `Ctrl+S` save, `Ctrl+Shift+S` save as, `Ctrl+C` copy, `Ctrl+V` paste,
`Ctrl+mouse wheel` zoom. With *Select*, click an annotation to move it, press Delete to remove it,
or click a colour to recolour it. Rotate, flip, resize and crop flatten annotations into the image (undoable).

## History
Shows saved screenshots with thumbnail, name, date, format and folder. Open, copy, edit, delete or show in
folder. The Free edition keeps the latest 20 entries (your files are never deleted automatically).
Pro keeps unlimited history and adds search (by name, date or OCR text).

## Pro tools
* **Scrolling screenshot** — select an area inside a scrolling page; the app scrolls and stitches automatically.
  Keep the mouse still; press Esc to stop early.
* **Repeat last area / Fixed-size area** — consistent screenshots for tutorials.
* **Blur / Pixelate** — hide passwords and personal data (applied to the pixels, cannot be undone after saving).
* **Magnifier, Spotlight, Highlighter, Numbered steps, Filled shapes, Watermark, Templates**.
* **OCR** — Quick Tools › Extract text, or the *Text (OCR)* button in the editor. Works offline.
* **GIF recording**, **Recording profiles**, **H.265**, **custom bitrate**.
* **Batch processing** — convert/resize/compress/watermark many images; originals stay untouched.

## Licensing
See **Upgrade to Pro**: start the 7-day trial, activate with your license key, or import a license file.
Your **Device ID** is shown there — send it with your key when requesting a license file or a transfer.

## Files and folders
* Screenshots: `Pictures\Burhan Screen Capture` (configurable)
* Recordings: `Videos\Burhan Screen Capture` (configurable)
* Settings & history: `%AppData%\Burhan Nexus\Screen Capture Pro`
* License, thumbnails, logs: `%LocalAppData%\Burhan Nexus\Screen Capture Pro`

Uninstalling removes the program, shortcuts, thumbnails and logs. Your screenshots, recordings, settings and
license are kept so a reinstall continues where you left off.
