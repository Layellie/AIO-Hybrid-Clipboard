# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Solution Overview

This is a two-project Visual Studio solution (`AIO Hybrid Clipboard.slnx`):

- **`AIO Clipboard & Search/`** — C# WPF application targeting .NET 10.0-windows (`AIO_Hybrid_Clipboard.csproj`). The entire UI and application logic lives in a single window (`MainWindow.xaml` / `MainWindow.xaml.cs`). No MVVM pattern — all logic is in code-behind.
- **`AIO_SearchEngine/`** — Native C++ DLL (`AIO_SearchEngine.vcxproj`) that wraps the Windows Runtime OCR API (`Windows.Media.Ocr`). Exports a single function `ProcessImageOCR` via C-style linkage for P/Invoke consumption.

## Build

Build using Visual Studio 2022 or MSBuild. The C++ project must be built as **x64** before the C# project, because the post-build event automatically copies `AIO_SearchEngine.dll` to the C# output directory:

```
copy "$(OutDir)AIO_SearchEngine.dll" "$(SolutionDir)AIO_Hybrid_Clipboard\bin\x64\Debug\net10.0-windows\" /Y
```

When building the full solution, use the **x64** platform configuration. The C# project supports both `AnyCPU` and `x64`, but the native DLL requires x64 at runtime.

```
msbuild "AIO Hybrid Clipboard.slnx" /p:Configuration=Debug /p:Platform=x64
```

There are no automated tests in this project.

## Architecture

### WPF Application (`AIO Clipboard & Search/`)

The window is a borderless, transparent, always-on-top overlay (`WindowStyle="None"`, `AllowsTransparency="True"`, `Topmost="True"`, `ShowInTaskbar="False"`). It appears centered at the top of the screen (15px from top). It is toggled visible/hidden via a configurable global hotkey (default: **Alt+Space**).

Win32 APIs are P/Invoked directly from `MainWindow.xaml.cs` for:
- **Global hotkey**: `RegisterHotKey`/`UnregisterHotKey`, processed via `WM_HOTKEY` in `HwndHook`
- **Clipboard monitoring**: `AddClipboardFormatListener`/`RemoveClipboardFormatListener`, processed via `WM_CLIPBOARDUPDATE` in `HwndHook`
- **System tray**: `Shell_NotifyIcon` with `NOTIFYICONDATA`, using a custom `DiscordTrayMenu` context menu defined in XAML
- **Icon loading**: `LoadImage` reads `app_icon.ico` from the executable directory at startup

Data collections:
- `ClipboardHistory` — `List<string>` of text entries, max 15 items
- `ScreenshotHistory` — `ObservableCollection<ScreenshotModel>` of image captures, max 15 items

`ScreenshotModel` carries: `Name`, `Path` (saved PNG in `AIO_Cache/`), `Image` (`BitmapSource`), and `OcrText`.

### C++ OCR DLL (`AIO_SearchEngine/`)

The DLL exposes one function:

```cpp
extern "C" __declspec(dllexport) void __cdecl ProcessImageOCR(
    const unsigned char* pixelData, int width, int height, int stride,
    wchar_t* outText, int maxLen
);
```

It accepts raw BGRA8 pixel data (matching WPF's `BitmapSource` format), creates a `SoftwareBitmap` via C++/WinRT, then runs `OcrEngine::RecognizeAsync(...).get()` synchronously. The engine first tries `OcrEngine::TryCreateFromUserProfileLanguages()` (supports EN and TR simultaneously), falling back to `en-US`. Requires `windowsapp.lib` and Windows SDK 10.0+.

### Cross-component Data Flow

1. Clipboard image captured → saved as PNG to `AIO_Cache/` in the exe directory → `ScreenshotModel` added to `ScreenshotHistory`
2. OCR runs on a `Task.Run` background thread, calling `ProcessImageOCR` via P/Invoke with the raw pixel bytes
3. OCR result written to `ScreenshotModel.OcrText` and prepended to `ClipboardHistory`
4. Click on a screenshot thumbnail triggers OCR on demand (`ExecuteOcrOnScreenshot`)
5. Drag on a screenshot thumbnail initiates `DragDrop.DoDragDrop` with `DataFormats.FileDrop` pointing to the cached PNG

### Important Behavioral Details

- **Clipboard recursion prevention**: `GeriKopyalaVeGizle` temporarily calls `RemoveClipboardFormatListener` before `Clipboard.SetText` and re-adds it after, preventing the app from recording its own paste-back as a new history entry.
- **Image debounce**: A 800ms guard (`_lastScreenshotTime`) prevents duplicate captures when the same image clipboard event fires multiple times.
- **Startup registry**: The app reads/writes `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run\AIO_ClipboardSearch` for "Start with Windows" support.
- **Settings drawer**: `PnlSettingsDrawer` is a collapsed `Border` in Grid row 2. It holds hotkey combos and startup/hide checkboxes.
- **Search**: `TxtSearch_TextChanged` filters both `LstResults` (text) and `LstScreenshots` (by `Name` and `OcrText`) simultaneously using `StringComparison.OrdinalIgnoreCase`.

## Development Guidelines & Optimization Rules

- **Memory Management (C++):** Zero tolerance for memory leaks. Always ensure strict RAM management, especially when handling raw pixel data and OCR tasks. Use modern C++ memory management (smart pointers) where applicable, or strictly pair allocations with deallocations.
- **Latency & Hardware Performance:** System latency must remain completely unaffected by clipboard monitoring. Use the absolute fastest and most lightweight algorithms for background threads and P/Invoke calls.
- **WPF UI Performance:** UI thread must never be blocked. Keep the 800ms debounce logic perfectly intact and optimize the `BitmapSource` caching mechanism to avoid unnecessary memory spikes.
- **Code Generation:** When modifying code, do not break the existing borderless window behaviors or the P/Invoke hooks for hotkeys.