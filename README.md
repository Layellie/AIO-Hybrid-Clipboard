# AIO Hybrid Clipboard

**English** | [Türkçe](README.tr.md)

<img width="900" alt="AIO Hybrid Clipboard screenshot" src="https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/download/v1.3.0/screenshot_v1.3.0.png" />

[![CI](https://github.com/Layellie/AIO-Hybrid-Clipboard/actions/workflows/ci.yml/badge.svg)](https://github.com/Layellie/AIO-Hybrid-Clipboard/actions/workflows/ci.yml)
[![Downloads](https://img.shields.io/github/downloads/Layellie/AIO-Hybrid-Clipboard/total?style=flat-square&color=success)](https://github.com/Layellie/AIO-Hybrid-Clipboard/releases)
[![Release](https://img.shields.io/github/v/release/Layellie/AIO-Hybrid-Clipboard?style=flat-square&color=blueviolet)](https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/latest)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue?style=flat-square&logo=windows)
![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)
![C++](https://img.shields.io/badge/Engine-C%2B%2B%2020-00599C?style=flat-square&logo=c%2B%2B)
![C#](https://img.shields.io/badge/Frontend-C%23%20WPF-239120?style=flat-square&logo=c-sharp)

A blazing fast, lightweight, hybrid (C# WPF + C++20) clipboard manager for Windows. AIO Hybrid Clipboard tracks your copied texts and images and features a built-in asynchronous C++ WinRT OCR engine to instantly extract text from screenshot captures.

## Installation

Download the latest `AIO_Hybrid_Clipboard_Setup_v*.exe` from [Releases](https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/latest) and run it. The installer:

- Installs per-user — **no admin rights / UAC prompt needed**
- Is fully self-contained — **no .NET runtime installation required**
- Supports English and Turkish setup languages

## Features

- **Hybrid Architecture:** Beautiful UI built with C# WPF; heavy OCR and pixel processing handled by a custom C++20 DLL for zero UI lag.
- **Smart OCR Engine:** Instantly extracts text from captured images using the native Windows WinRT AI engine. Click any saved screenshot to run OCR.
- **Reverse OCR Search:** Search for words inside your images. The engine indexes image text so you can find captures instantly.
- **Session Persistence:** Clipboard history, screenshots, and pin state are saved on exit and restored on next launch.
- **Pin Important Entries:** Right-click any text entry or screenshot to pin it — pinned items stay on top and are never auto-removed by the history limit. Right-click again to unpin.
- **Quick-Paste Hotkeys:** Press `ALT+1`, `ALT+2`, `ALT+3` to paste the last 3 copied texts directly into any app — no window needed. Numbered `1 / 2 / 3` badges show exactly which entry each shortcut pastes.
- **Large Screenshot Preview:** Hover any screenshot thumbnail to see a full-size preview with its capture name.
- **Drag & Drop:** Seamlessly drag images from the gallery into Discord, Photoshop, or your Desktop.
- **Edit & Bulk Delete:** Toggle edit mode to multi-select (with a checkmark on selected items) and delete history entries and their cached files in one click.
- **Automatic Cache Cleanup:** Orphaned screenshot files are cleared from the cache on startup, keeping disk usage in check.
- **Automatic Updates:** The app checks GitHub Releases for new versions on startup and from the settings drawer — one click downloads and installs the update.
- **System Tray Integration:** Runs silently in the background with a minimal memory footprint.
- **Global Shortcuts:** Fully customizable hotkeys to summon the launcher from anywhere.
- **Multi-language UI:** Supports English and Turkish.

## Default Shortcuts

| Action | Shortcut |
|---|---|
| Summon / hide launcher | `ALT + SPACE` |
| Capture screen | `WIN + SHIFT + S` (Windows built-in) |
| Quick-paste entry 1 | `ALT + 1` |
| Quick-paste entry 2 | `ALT + 2` |
| Quick-paste entry 3 | `ALT + 3` |

## How to Use

1. Press `ALT + SPACE` to open the interface.
2. Copy any text or capture any image — they will automatically populate the lists.
3. **Single-click** an image to extract its text via OCR.
4. **Right-click** any entry to pin it to the top (right-click again to unpin).
5. **Click and drag** an image to export the PNG file to any application.
6. Press `ALT+1/2/3` to paste recent clipboard entries without opening the window.
7. Press `ESC` or click outside to dismiss.

## How to Build

### Prerequisites

- [Visual Studio 2022](https://visualstudio.microsoft.com/) with:
  - **Desktop development with C++** workload (MSVC v143 toolset)
  - **.NET desktop development** workload
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows SDK 10.0 or later

### Steps

```bat
:: 1. Clone the repository
git clone https://github.com/Layellie/AIO-Hybrid-Clipboard.git
cd "AIO-Hybrid-Clipboard"

:: 2. Build the C++ OCR DLL first (x64 required)
msbuild "AIO_SearchEngine\AIO_SearchEngine.vcxproj" /p:Configuration=Release /p:Platform=x64

:: 3. Build the C# WPF application
dotnet build "AIO Clipboard & Search\AIO_Hybrid_Clipboard.csproj" -p:Configuration=Release -p:Platform=x64
```

The post-build event automatically copies `AIO_SearchEngine.dll` into the C# output directory. The final binary is at:

```
AIO Clipboard & Search\bin\x64\Release\net10.0-windows\AIO_Hybrid_Clipboard.exe
```

> **Note:** The application must run as **x64** — the native OCR DLL is x64-only.

### Building the Installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```bat
:: 1. Publish a self-contained build
dotnet publish "AIO Clipboard & Search\AIO_Hybrid_Clipboard.csproj" -c Release -r win-x64 --self-contained true -p:Platform=x64

:: 2. Copy the native OCR DLL into the publish folder
copy "AIO_SearchEngine\x64\Release\AIO_SearchEngine.dll" "AIO Clipboard & Search\bin\x64\Release\net10.0-windows\win-x64\publish\" /Y

:: 3. Compile the installer (output: installer\Output\)
"%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" installer\AIO_Hybrid_Clipboard.iss
```

## Tech Stack

| Layer | Technology |
|---|---|
| UI | C# 12 / .NET 10 / WPF |
| OCR Engine | C++20 / WinRT (`Windows.Media.Ocr`) |
| Interop | P/Invoke (C-style DLL export) |
| Persistence | `System.Text.Json` + registry |

## Architecture

The solution contains three projects:

- **`AIO Clipboard & Search/`** — WPF application. Logic is split into SRP-compliant service classes (`HistoryService`, `ClipboardService`, `HotkeyManager`, `OcrService`, `UpdateService`, `TrayIconService`, `SessionStore`, `SettingsService`, `StartupService`, `Log`) with a thin UI-only `MainWindow`. UI strings live in standard `.resx` resources (EN/TR).
- **`AIO_SearchEngine/`** — Native C++ DLL exposing a single `ProcessImageOCR` function. Accepts raw BGRA8 pixel data, runs `OcrEngine::RecognizeAsync` via C++/WinRT and returns a typed status code alongside the recognized text.
- **`AIO_Hybrid_Clipboard.Tests/`** — xUnit suite: unit tests for the history/pinning rules, session persistence and localization, plus integration tests that exercise the real native OCR engine.

Every push is built and tested by [CI](https://github.com/Layellie/AIO-Hybrid-Clipboard/actions); pushing a `v*` tag automatically builds the installer and publishes a release.

## FAQ

**Windows SmartScreen warns me when running the installer. Why?**
The installer is not code-signed (certificates cost money for a free open-source tool). The source is fully public and every release is built from it — click *More info → Run anyway*. You can verify the SHA-256 digest shown on each release's page.

**Where is my data stored?**
Everything stays on your machine: history and screenshots live in `AIO_Cache/` next to the executable. The only network call is the update check against the GitHub Releases API.

**Why x64 only?**
The OCR engine is a native x64 DLL consumed over P/Invoke; an ARM64/x86 build of the app would fail to load it.

**A hotkey doesn't work.**
Another app probably registered the same combination first. Pick a different combo in Settings; failures are recorded in `AIO_Cache/app.log`.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the full version history.

## Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) first — it covers the build setup, project layout and PR expectations.

## License

[MIT](LICENSE) — © 2026 SAMET KAŞMER AKA LAYE77IE
