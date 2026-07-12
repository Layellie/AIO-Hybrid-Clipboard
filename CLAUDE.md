# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Solution Overview

This is a three-project Visual Studio solution (`AIO Hybrid Clipboard.slnx`):

- **`AIO Clipboard & Search/`** — C# WPF application targeting .NET 10.0-windows (`AIO_Hybrid_Clipboard.csproj`). A thin code-behind `MainWindow` delegates to SRP service classes under `Services/`.
- **`AIO_SearchEngine/`** — Native C++ DLL (`AIO_SearchEngine.vcxproj`, toolset **v143**) that wraps the Windows Runtime OCR API (`Windows.Media.Ocr`). Exports a single function `ProcessImageOCR` via C-style linkage for P/Invoke consumption; returns a typed status code.
- **`AIO_Hybrid_Clipboard.Tests/`** — xUnit tests: unit tests for history/pinning rules, session persistence and localization, plus integration tests that call the real native OCR DLL.

## Build

Build using Visual Studio 2022+ or MSBuild. The C++ project must be built as **x64** before the C# project, because the post-build event copies `AIO_SearchEngine.dll` to the C# output directory (the event creates the target folder if needed and works for standalone project builds).

```
msbuild AIO_SearchEngine\AIO_SearchEngine.vcxproj /p:Configuration=Debug /p:Platform=x64
dotnet build "AIO Clipboard & Search\AIO_Hybrid_Clipboard.csproj" -p:Platform=x64
```

Warnings are errors (`TreatWarningsAsErrors`) in the C# project.

### Tests

```
msbuild AIO_SearchEngine\AIO_SearchEngine.vcxproj /p:Configuration=Release /p:Platform=x64
dotnet test AIO_Hybrid_Clipboard.Tests\AIO_Hybrid_Clipboard.Tests.csproj
```

The OCR integration tests need the Release|x64 native DLL; they self-skip when it is absent.

### Installer & Releases

`installer/AIO_Hybrid_Clipboard.iss` (Inno Setup 6) packages a self-contained `dotnet publish -r win-x64` output; per-user install (no UAC) because the app writes `AIO_Cache/` next to the exe. Pushing a `v*` tag triggers `.github/workflows/release.yml`, which builds everything, compiles the installer (version injected via `/DMyAppVersion`) and publishes the GitHub release. `ci.yml` builds + tests every push/PR. Release flow: bump `<Version>` in the csproj, update `CHANGELOG.md`, tag, push.

## Architecture

### WPF Application (`AIO Clipboard & Search/`)

The window is a borderless, transparent, always-on-top overlay toggled via a configurable global hotkey (default: **Alt+Space**). `MainWindow` is UI-only; behavior lives in services:

- `HistoryService` — owns the `Texts`/`Screenshots` ObservableCollections and all insertion/pinning/eviction/filter rules (limit 15; pinned entries never auto-evicted and may exceed the cap)
- `ClipboardService` — `AddClipboardFormatListener` monitoring, 800ms image debounce, silent copy-back (`SetClipboardSilent` re-attaches the listener in `finally`), PNG encode + save on the thread pool
- `HotkeyManager` — `RegisterHotKey` for toggle + Alt+1/2/3 quick paste, `SendInput`-based paste simulation
- `OcrService` — P/Invoke into `AIO_SearchEngine.dll`; the native call returns `OcrStatus` codes (`Success/NoText/EngineUnavailable/ProcessingError/InvalidArgument`), empty string means "no text"
- `UpdateService` — GitHub Releases API version check, installer download + handoff
- `SessionStore` — JSON persistence in `AIO_Cache/session.json`, orphan PNG cleanup
- `SettingsService` — registry-backed settings; `T(key)` localization via `Resources/Strings.resx` (EN) / `Strings.tr.resx` (TR)
- `TrayIconService`, `StartupService`, `Win32Api`, `AppPaths`, `Log` (file logger → `AIO_Cache/app.log`, size-capped)

`App` enforces single instance via mutex; a second launch signals a named `EventWaitHandle` which brings the running window to front.

### C++ OCR DLL (`AIO_SearchEngine/`)

```cpp
extern "C" __declspec(dllexport) int __cdecl ProcessImageOCR(
    const unsigned char* pixelData, int width, int height, int stride,
    wchar_t* outText, int maxLen);
```

Accepts raw BGRA8 pixels, memcpys rows directly into a `SoftwareBitmap` buffer (single copy, via `IMemoryBufferByteAccess`), runs `OcrEngine::RecognizeAsync(...).get()`. Tries `TryCreateFromUserProfileLanguages()` (EN+TR), falls back to `en-US`. Returns status codes kept in sync with `OcrService.OcrStatus`. Never lets exceptions cross the P/Invoke boundary.

### Cross-component Data Flow

1. Clipboard image captured → pixels extracted on UI thread → PNG encoded/saved to `AIO_Cache/` on thread pool → `ScreenshotModel` added via `HistoryService`
2. OCR runs on a background task through `OcrService`; result lands in `ScreenshotModel.OcrText` (change-notifying)
3. Click on a thumbnail = on-demand OCR (result also inserted into text history + clipboard); drag = `DataFormats.FileDrop` with the cached PNG
4. Search filters both lists via `HistoryService.FilterTexts/FilterScreenshots` (OrdinalIgnoreCase, includes OCR text)

## Development Guidelines & Optimization Rules

- **Memory Management (C++):** Zero tolerance for memory leaks. Use C++/WinRT RAII idioms; never let exceptions cross the P/Invoke boundary.
- **Latency:** Clipboard monitoring must not affect system latency; background threads and P/Invoke paths stay lightweight.
- **WPF UI Performance:** UI thread must never be blocked. Keep the 800ms debounce intact; heavy work (encoding, disk I/O, OCR) belongs on background threads.
- **Code Generation:** Do not break the borderless window behaviors or the P/Invoke hooks (`HwndHook` handles tray, hotkey and clipboard messages).
- **Localization:** every user-facing string goes into both `Strings.resx` and `Strings.tr.resx`.
- **Testing:** logic that can run without the UI belongs in a service with xUnit coverage.
