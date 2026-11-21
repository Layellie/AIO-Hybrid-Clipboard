# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [2.0.0] - 2026-11-21

### Added
- **Modern Fluent Dark UI:** Redesigned 2-column layout with custom `Themes/DarkTheme.xaml`, crisp SVG vector icons (replacing emojis), custom dark `ComboBox`, Windows 11 toggle switches, history-limit slider, floating toast notifications, and empty states.
- **Smart Content Classification:** `ContentClassifier` automatically tags copied text as URL, Hex Color (with live color swatch preview), Email, Windows/UNC File Path, Code snippet, or plain text.
- **Category Filter Pills & Multi-Word Fuzzy Search:** Filter history by All / Pinned / Links / Code / Colors, and search across multiple words with typo-tolerant matching.
- **Password Manager Privacy Guard:** `ClipboardPrivacyGuard` automatically ignores sensitive clipboard items flagged with `ExcludeClipboardContentFromMonitorProcessing`, `Clipboard Viewer Ignore`, `CanIncludeInClipboardHistory=0`, or concealed/transient pasteboard formats.
- **Pause / Incognito Mode:** Temporarily pause clipboard recording from the header or system tray menu with a live status banner and tray tooltip indicator.
- **Auto-Paste & Full Keyboard Navigation:** Optional auto-paste restores focus to the previous foreground window and pastes automatically; full keyboard workflow (`Up`/`Down`/`Tab`/`Enter`/`Shift+Enter`/`Ctrl+P`/`Del`/`Ctrl+F`/`Esc`).
- **Multi-Monitor & DPI Positioning:** Launcher opens centered on the active monitor's work area (scaled per-monitor DPI, never overlapping the taskbar) and dismisses on click-outside (`Deactivated`).
- **Low-RAM Thumbnails, LOH Pooling & Full-Res OCR:** Frozen `po:Freeze="True"` brushes/geometries, `VirtualizingStackPanel` (`Recycling`) on both lists, 320px gallery thumbnails with on-demand hover previews released on hide/close, `ArrayPool<byte>`/`ArrayPool<char>` pooling to eliminate LOH churn, `SemaphoreSlim` concurrency cap, low-priority background OCR, and per-item `CancellationToken` cancellation on delete/shutdown.
- **Native OCR Engine Upgrades:** RAII `RoInitialize` apartment guard, dimension/overflow bounds checks, line-break preservation via `OcrResult::Lines()`, and `IsOcrEngineAvailable()` probe export.
- **24/7 Stability & Fast Cold Startup:** Deferred async session load + orphan cleanup after tray/hotkey startup, 80ms search filter debounce, `TaskbarCreated` Explorer restart tray recovery, `PowerModeChanged` sleep/resume listener recovery, `DisplaySettingsChanged` repositioning, `OnSessionEnding` flush, deterministic Win32 `HICON` ownership, non-blocking async clipboard retries, and bounded preview/tooltip handling for >1 MB text entries.
- **Rich Actions & Persistent Settings:** Copy screenshot image back to clipboard, open URL clips in browser, copy OCR text directly, clear all unpinned entries, open cache folder, configurable history limit (`10..200`, default `50`), persistent `HideOnCopy`/`AutoPaste`/`StartMinimized`/`AutoOcr`/`QuickPasteEnabled`/`HistoryLimit`, and `--minimized` hidden-HWND startup.
- **Developer Power Features:** Source app badge (`Code`, `chrome`, etc.) and user-configurable app exclusion list, right-click quick text transforms (`JSON Format/Minify`, `Single Line`, `Trim Lines`, `UPPER/lower`, `URL Decode/Encode`), searchable clip aliases (`F2`), `Save Image As PNG`, and editable OCR text dialog (`TextPromptDialog`).
- **OCR Language Picker:** Native `AIO_SearchEngine.dll` exports `GetAvailableOcrLanguages` and `ProcessImageOCRForLanguage` so users can choose `auto` or any installed Windows OCR language pack (e.g. `en-US`, `tr-TR`).
- **Optional DPAPI Disk Encryption & Auto-Expire TTL:** Zero-dependency Windows `crypt32.dll` DPAPI (`CurrentUser`) encryption toggle for `session.json` plus configurable unpinned auto-expiration (`Never / 7 / 14 / 30 / 90 days`).
- **Installer SHA-256 Verification & Windows 11 DWM:** `release.yml` publishes `SHA256SUMS.txt` alongside the installer; `UpdateService` verifies SHA-256 before launching updates. Added Windows 11 DWM rounded corner / dark hints.
- **Expanded Test Suite:** 121 xUnit unit and integration tests covering content classification, privacy guard, app exclusion, text transforms, DPAPI encryption, TTL expiration, SHA-256 installer verification, SHA-256 screenshot deduplication, multi-word fuzzy search, 80ms search debounce, huge-text bounds, frozen theme resources, pooled pixel buffer lifecycle, async startup deferral, EN/TR `.resx` key parity, atomic session persistence, and multi-line + language-specific native OCR.

### Changed
- **Clipboard Reliability:** Added `GetClipboardSequenceNumber` deduplication, `COMException` retry with backoff, `Pbgra32` pixel format normalization, SHA-256 image deduplication, and collision-free capture filenames.
- **Atomic & Debounced Session Persistence:** `SessionStore` now writes via `.tmp -> Move` with debounced auto-save after history changes while staying backward compatible with v1.x session files.
- **Hardened Auto-Updater:** `UpdateService` uses a shared `HttpClient`, enforces HTTPS + trusted GitHub hosts, blocks path traversal, downloads via `.part` temp files, and reports download percentage.
- **Hotkey Conflict Detection:** `HotkeyManager.Register()` returns registration status and surfaces conflicts in Settings and toast notifications; quick-paste shortcuts (`Alt+1/2/3`) can be toggled off.
- **Startup Command:** `StartupService` now writes a quoted executable path with `--minimized`.
- **Global Exception Logging & DI Seams:** Added global crash logging hooks, `IDialogService`, `AsyncRelayCommand`, and `AppPaths.OverrideCacheFolder` for isolated testing.

### Fixed
- Fixed `SearchQuery` swallowing trailing spaces while typing multi-word queries.
- Fixed screenshot capture format mismatches on non-`Pbgra32` clipboard bitmaps.
- Fixed solution folder name in `AIO Hybrid Clipboard.slnx` (`Çözüm Öğeleri` -> `Solution Items`).

## [1.6.0] - 2026-07-12

### Changed
- Full MVVM migration: new `MainViewModel` owns application state and behavior
  (filtered collection views, search, pin/copy/OCR/delete commands, settings,
  updates); `MainWindow` code-behind now contains only Win32 plumbing (HWND
  hooks, hotkeys, tray, drag & drop). Localized labels bind through a
  `Loc[...]` indexer proxy and refresh live on language change.
- Test dependencies bumped via Dependabot (Test.Sdk 18.7, xunit 2.9.3,
  runner 3.1.5, System.Drawing.Common 10.0.9, setup-msbuild v3)

### Added
- ViewModel test coverage (34 tests total)
- Repository social preview asset (`assets/social_preview.png`)

## [1.5.0] - 2026-07-12

### Added
- Unit and integration test suite (25 tests) covering history rules, session
  persistence, localization, version comparison and the real native OCR engine
- File logging to `AIO_Cache/app.log` (size-capped, one rotation) so users can
  attach logs to bug reports
- CI pipeline: every push/PR builds the native engine, the WPF app and runs tests
- Release pipeline: pushing a `v*` tag builds and publishes the installer automatically
- Turkish README (`README.tr.md`), contribution guide, security policy,
  issue/PR templates and Dependabot configuration

### Changed
- Native OCR engine now returns typed status codes instead of bracketed marker
  strings, and writes pixels into the `SoftwareBitmap` buffer with a single copy
- History rules (insertion, pinning, eviction, filtering) extracted from the
  window into a UI-independent, tested `HistoryService`
- UI strings moved from hard-coded dictionaries to standard `.resx` resources
- Launching a second instance now brings the running window to front instead of
  showing an error dialog
- C++ project retargeted from toolset v145 to v143 for CI compatibility

### Fixed
- When every history entry was pinned, a newly copied text was silently dropped;
  it is now always recorded (pins may temporarily exceed the cap, matching
  screenshot behavior)

## [1.4.0] - 2026-07-12

### Added
- Windows installer (Inno Setup): per-user, no admin rights, self-contained —
  no .NET runtime installation required, EN/TR setup languages
- In-app version check and one-click auto-update from GitHub Releases,
  with a quiet update notice on startup

### Fixed
- Post-build event now works when the C++ project is built standalone

## [1.3.1] - 2026-07-12

### Fixed
- Clipboard monitoring could permanently stop if a copy-back to the clipboard
  failed; the listener is now always re-attached
- Second app instance no longer throws on exit (mutex ownership)
- PNG encoding and disk writes moved off the UI thread — large captures no
  longer stall the window
- OCR status messages are no longer copied to the clipboard or recorded as
  history entries
- `ScreenshotModel.OcrText` now raises change notifications
- Hotkey registration failures are logged instead of silently ignored

## [1.3.0] - 2026-06-01

### Added
- Pin text entries and screenshots (right-click); pinned items stay on top and
  are never auto-evicted
- Quick-paste hotkeys `ALT+1/2/3` with numbered badges
- Large hover preview for screenshots
- Automatic orphan cache cleanup on startup

## [1.2.0] - 2026-05-26

### Changed
- Split the monolithic window class into SRP-compliant services
- Memory-leak fixes and thread-safety hardening

## [1.1.0] - 2026-05-26

### Added
- Session persistence: clipboard history and screenshots survive restarts

## [1.0.0] - 2026-05-22

### Added
- Initial release: WPF clipboard manager with native C++/WinRT OCR engine,
  screenshot gallery, drag & drop export, global hotkeys, system tray, EN/TR UI

[2.0.0]: https://github.com/Layellie/AIO-Hybrid-Clipboard/compare/v1.6.0...v2.0.0
[1.6.0]: https://github.com/Layellie/AIO-Hybrid-Clipboard/compare/v1.5.0...v1.6.0
[1.5.0]: https://github.com/Layellie/AIO-Hybrid-Clipboard/compare/v1.4.0...v1.5.0
[1.4.0]: https://github.com/Layellie/AIO-Hybrid-Clipboard/compare/v1.3.0...v1.4.0
[1.3.1]: https://github.com/Layellie/AIO-Hybrid-Clipboard/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/Layellie/AIO-Hybrid-Clipboard/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/Layellie/AIO-Hybrid-Clipboard/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/Layellie/AIO-Hybrid-Clipboard/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/tag/v1.0.0
