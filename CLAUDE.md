# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Solution Overview

This is a three-project Visual Studio solution (`AIO Hybrid Clipboard.slnx`):

- **`AIO Clipboard & Search/`** — C# WPF application targeting .NET 10.0-windows (`AIO_Hybrid_Clipboard.csproj`). MVVM architecture with a Fluent dark theme (`Themes/DarkTheme.xaml`) and thin Win32-only `MainWindow` code-behind delegating to SRP services under `Services/`.
- **`AIO_SearchEngine/`** — Native C++ DLL (`AIO_SearchEngine.vcxproj`, toolset **v143**) that wraps the Windows Runtime OCR API (`Windows.Media.Ocr`). Exports `ProcessImageOCR` (preserving line breaks via `OcrResult::Lines()`) and `IsOcrEngineAvailable` via C-style linkage for P/Invoke consumption.
- **`AIO_Hybrid_Clipboard.Tests/`** — xUnit tests covering content classification, privacy guard rules, history/pinning/SHA-256 dedup/fuzzy search, session persistence, update security validation, EN/TR localization key parity, ViewModel flows, and real native OCR integration tests.

## Build

Build using Visual Studio 2022+ or MSBuild. The C++ project must be built as **x64** before the C# project, because the post-build event copies `AIO_SearchEngine.dll` to the C# output directory (the event creates the target folder if needed and works for standalone project builds).

```
msbuild AIO_SearchEngine\AIO_SearchEngine.vcxproj /p:Configuration=Release /p:Platform=x64
dotnet build "AIO Clipboard & Search\AIO_Hybrid_Clipboard.csproj" -c Release -p:Platform=x64
```

Warnings are errors (`TreatWarningsAsErrors`) in the C# project.

### Tests

```
msbuild AIO_SearchEngine\AIO_SearchEngine.vcxproj /p:Configuration=Release /p:Platform=x64
dotnet test AIO_Hybrid_Clipboard.Tests\AIO_Hybrid_Clipboard.Tests.csproj -c Release
```

The OCR integration tests need the Release|x64 native DLL; they self-skip when it is absent.

### Installer & Releases

`installer/AIO_Hybrid_Clipboard.iss` (Inno Setup 6) packages a self-contained `dotnet publish -r win-x64` output; per-user install (no UAC) because the app writes `AIO_Cache/` next to the exe. Pushing a `v*` tag triggers `.github/workflows/release.yml`, which builds everything, compiles the installer (version injected via `/DMyAppVersion`) and publishes the GitHub release. `ci.yml` builds + tests every push/PR. Release flow: bump `<Version>` in the csproj and `.iss`, update `CHANGELOG.md`, tag, push.

## Architecture

### WPF Application (`AIO Clipboard & Search/`)

The window is a borderless, transparent, always-on-top Fluent dark overlay toggled via a configurable global hotkey (default: **Alt+Space**), positioned on the active monitor's DPI-scaled work area (excluding taskbar) and hidden on click-outside (`Deactivated`).

- `ViewModels/MainViewModel` — all application state and behavior: `TextsView`/`ScreenshotsView` (filtered `ICollectionView`s over `HistoryService`), raw `SearchQuery` (preserving spaces for multi-word queries), `SelectedCategory` (`All`/`Pinned`/`Links`/`Code`/`Colors`), `IsPaused` (Incognito mode), toast notifications, commands (`CopyTextCommand`, `CopyAndPasteCommand`, `TogglePin*Command`, `RunOcrCommand`, `CopyScreenshotCommand`, `CopyOcrTextCommand`, `OpenUrlCommand`, `ClearUnpinnedCommand`, `OpenCacheFolderCommand`, `TogglePauseCommand`, `CheckUpdatesCommand`), persistent settings (`HideOnCopy`, `AutoPaste`, `StartWithWindows`, `StartMinimized`, `AutoOcr`, `QuickPasteEnabled`, `HistoryLimit`, `LanguageIndex`), debounced auto-save, and `HideRequested`/`PasteToPreviousWindowRequested` events
- `ViewModels/LocalizationProxy` — bindable `Loc[key]` indexer over `Resources/Strings.resx` (EN) / `Strings.tr.resx` (TR); `Refresh()` re-evaluates every bound label on language change
- `Models/` — `ClipItem` (auto-classified via `ContentClassifier`: URL, Color hex, Email, FilePath, Code, Text), `ContentClassifier` (allocation-light span classifier), `ScreenshotModel` (metadata, SHA-256 `ContentHash`, 320px thumbnail, live `IsOcrRunning` and `OcrText`)
- `Common/` — `RelayCommand` and `AsyncRelayCommand` (re-entrancy guard + exception logging)
- `Converters/` — `UiConverters.cs` (`HexToBrushConverter`, `NonEmptyToVisibilityConverter`, `EnumEqualsConverter`), `EditSelectionToVisibilityConverter`, `QuickPasteBadgeConverter`
- `Themes/DarkTheme.xaml` — Fluent dark color tokens, SVG Path geometry icons, custom dark `ComboBox`, `FluentToggleSwitchStyle`, `FluentSliderStyle`, slim scrollbars, pill radio buttons, and tray context menu styles
- `MainWindow` code-behind — Win32 plumbing only: `InitializeHidden()` (`EnsureHandle` for `--minimized` startup), `HwndHook`, multi-monitor DPI positioning, previous foreground window capture for auto-paste, `HotkeyManager` registration + capture UI, `ClipboardService`/`TrayIconService` wiring, drag & drop, and full keyboard navigation (`Up`/`Down`/`Tab`/`Enter`/`Shift+Enter`/`Ctrl+P`/`Del`/`Ctrl+F`/`Esc`)

- `Services/`:
  - `HistoryService` — owns `Texts`/`Screenshots` ObservableCollections, configurable limit (`10..200`, default `50`), pinned-first ordering, SHA-256 screenshot deduplication, multi-word + typo-tolerant fuzzy search (text, alias, OCR, source app), category filtering, `ClearUnpinned`, and TTL `PurgeExpired`
  - `ClipboardPrivacyGuard` — blocks password manager / private clipboard formats (`ExcludeClipboardContentFromMonitorProcessing`, `Clipboard Viewer Ignore`, `CanIncludeInClipboardHistory=0`, `org.nspasteboard.*`) and user-configured excluded apps; resolves source process names via Win32 `QueryFullProcessImageName`
  - `ClipboardService` — `AddClipboardFormatListener` monitoring, privacy guard, app exclusion, Pause/Incognito mode, `GetClipboardSequenceNumber` dedup, non-blocking async `COMException` retry with backoff, `Pbgra32` format normalization, `PooledPixelBuffer` (`ArrayPool<byte>`), SHA-256 hashing, 320px thumbnails, on-demand 640px hover previews, `SetClipboardSilentAsync`/`SetImageSilentAsync`, sleep-resume `EnsureListening`
  - `OcrService` — P/Invoke into `AIO_SearchEngine.dll` (`ProcessImageOCRForLanguage`, `GetAvailableOcrLanguages`, `IsOcrEngineAvailable`) using `ArrayPool<byte>` + `ArrayPool<char>` 64K buffer, `SemaphoreSlim(2, 2)`, `ThreadPriority.BelowNormal`, and `RecognizeImageFileAsync`
  - `TextTransformer` — zero-dependency developer text transforms (JSON Format/Minify, Single Line, Trim Lines, UPPER/lower, URL Decode/Encode)
  - `SessionCrypto` — zero-dependency Windows DPAPI (`crypt32.dll` `CryptProtectData`/`CryptUnprotectData`, CurrentUser scope, `AIOE` header)
  - `SessionStore` — atomic `.tmp -> Move` JSON persistence (optional DPAPI encryption), debounced auto-save, `ShutdownAndSave`, 320px thumbnail restoration, orphan PNG cleanup, v1.x back-compat
  - `UpdateService` — shared static `HttpClient`, strict HTTPS + GitHub host validation, path traversal protection, `SHA256SUMS.txt` SHA-256 integrity verification, atomic `.part` download with progress reporting
  - `HotkeyManager`, `SettingsService`, `StartupService`, `TrayIconService` (`_ownsIcon` HICON tracking + `TaskbarCreated` `ReRegister`), `Win32Api` (incl. Win11 DWM rounded corners/dark hints), `AppPaths`, `DialogService` (`Views/TextPromptDialog`), `Log`

`App` enforces single instance via mutex; a second launch signals a named `EventWaitHandle` which brings the running window to front. Supports `--minimized` startup without flashing the window.

### C++ OCR DLL (`AIO_SearchEngine/`)

```cpp
extern "C" __declspec(dllexport) int __cdecl IsOcrEngineAvailable();
extern "C" __declspec(dllexport) int __cdecl ProcessImageOCR(
    const unsigned char* pixelData, int width, int height, int stride,
    wchar_t* outText, int maxLen);
```

Uses RAII `WinRtApartmentGuard` (`RoInitialize`/`RoUninitialize`), validates dimensions (`<= 16384`) and buffer bounds, memcpys premultiplied BGRA8 rows directly into `SoftwareBitmap` (single copy via `IMemoryBufferByteAccess`), runs `OcrEngine::RecognizeAsync(...).get()`, and preserves line breaks via `OcrResult::Lines()`. Never lets exceptions cross the P/Invoke boundary.

## Development Guidelines & Optimization Rules

- **Memory Management (C++):** Zero tolerance for memory leaks. Use C++/WinRT RAII idioms; never let exceptions cross the P/Invoke boundary.
- **Latency:** Clipboard monitoring must not affect system latency; background threads and P/Invoke paths stay lightweight.
- **WPF UI Performance:** UI thread must never be blocked. Keep the 800ms debounce, virtualized list panels, and 320px gallery thumbnails intact; heavy work (encoding, disk I/O, OCR) belongs on background threads.
- **Code Generation:** Do not break borderless window behaviors, `--minimized` hidden HWND initialization, or P/Invoke hooks (`HwndHook`).
- **Localization:** Every user-facing string goes into both `Strings.resx` and `Strings.tr.resx` (verified by `LocalizationParityTests`).
- **Testing:** Logic that can run without the UI belongs in a service with xUnit coverage.
