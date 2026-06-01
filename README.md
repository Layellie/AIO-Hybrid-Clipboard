# AIO Hybrid Clipboard

<img width="900" alt="AIO Hybrid Clipboard screenshot" src="https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/download/v1.3.0/screenshot_v1.3.0.png" />

![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue?style=flat-square&logo=windows)
![Version](https://img.shields.io/badge/Version-v1.3.0-blueviolet?style=flat-square)
![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)
![C++](https://img.shields.io/badge/Engine-C%2B%2B%2020-00599C?style=flat-square&logo=c%2B%2B)
![C#](https://img.shields.io/badge/Frontend-C%23%20WPF-239120?style=flat-square&logo=c-sharp)

A blazing fast, lightweight, hybrid (C# WPF + C++20) clipboard manager for Windows. AIO Hybrid Clipboard tracks your copied texts and images and features a built-in asynchronous C++ WinRT OCR engine to instantly extract text from screenshot captures.

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

## Tech Stack

| Layer | Technology |
|---|---|
| UI | C# 12 / .NET 10 / WPF |
| OCR Engine | C++20 / WinRT (`Windows.Media.Ocr`) |
| Interop | P/Invoke (C-style DLL export) |
| Persistence | `System.Text.Json` + registry |

## Architecture

The solution contains two projects:

- **`AIO Clipboard & Search/`** — WPF application. Logic is split into SRP-compliant service classes (`ClipboardService`, `HotkeyManager`, `OcrService`, `TrayIconService`, `SessionStore`, `SettingsService`, `StartupService`) with a thin UI-only `MainWindow`.
- **`AIO_SearchEngine/`** — Native C++ DLL exposing a single `ProcessImageOCR` function. Accepts raw BGRA8 pixel data, runs `OcrEngine::RecognizeAsync` synchronously via C++/WinRT, and writes the result to a caller-supplied wide-char buffer.

## License

[MIT](LICENSE) — © 2026 SAMET KAŞMER AKA LAYE77IE
