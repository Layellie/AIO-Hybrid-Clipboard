# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Global exception logging (`DispatcherUnhandledException`, domain and
  unobserved-task hooks) — crashes now leave a trace in `AIO_Cache/app.log`
- `IDialogService` abstraction; view model no longer calls `MessageBox` directly
- Update-flow test coverage: release JSON parsing, newer/equal/older version
  decisions, API-failure handling, plus a live contract test against the
  published GitHub release (43 tests total)
- `CODE_OF_CONDUCT.md`; GitHub Discussions enabled

### Changed
- `UpdateService` is instance-based with an injectable `HttpMessageHandler`
- CI actions bumped (checkout v7, setup-dotnet v5, gh-release v3)

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

[1.6.0]: https://github.com/Layellie/AIO-Hybrid-Clipboard/compare/v1.5.0...v1.6.0
[1.5.0]: https://github.com/Layellie/AIO-Hybrid-Clipboard/compare/v1.4.0...v1.5.0
[1.4.0]: https://github.com/Layellie/AIO-Hybrid-Clipboard/compare/v1.3.0...v1.4.0
[1.3.1]: https://github.com/Layellie/AIO-Hybrid-Clipboard/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/Layellie/AIO-Hybrid-Clipboard/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/Layellie/AIO-Hybrid-Clipboard/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/Layellie/AIO-Hybrid-Clipboard/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/tag/v1.0.0
