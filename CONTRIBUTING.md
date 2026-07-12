# Contributing to AIO Hybrid Clipboard

Thanks for your interest in contributing! This document explains how to get a
working build and what we expect from pull requests.

## Development Setup

### Prerequisites

- **Visual Studio 2022** (or newer) with:
  - *Desktop development with C++* workload (MSVC **v143** toolset)
  - *.NET desktop development* workload
- **.NET 10 SDK**
- **Windows SDK 10.0+**
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) — only needed to build the installer

### Building

The C++ OCR engine must be built **before** the C# app (its post-build step
copies the DLL into the C# output folder):

```bat
msbuild AIO_SearchEngine\AIO_SearchEngine.vcxproj /p:Configuration=Debug /p:Platform=x64
dotnet build "AIO Clipboard & Search\AIO_Hybrid_Clipboard.csproj" -p:Platform=x64
```

> The app must run as **x64** — the native OCR DLL is x64-only.

### Running Tests

```bat
:: Build the native engine in Release|x64 first so the OCR integration tests can run
msbuild AIO_SearchEngine\AIO_SearchEngine.vcxproj /p:Configuration=Release /p:Platform=x64
dotnet test AIO_Hybrid_Clipboard.Tests\AIO_Hybrid_Clipboard.Tests.csproj
```

## Project Layout

| Path | Purpose |
|---|---|
| `AIO Clipboard & Search/` | WPF app — thin `MainWindow` + SRP service classes under `Services/` |
| `AIO_SearchEngine/` | Native C++/WinRT DLL wrapping `Windows.Media.Ocr` |
| `AIO_Hybrid_Clipboard.Tests/` | xUnit unit + integration tests |
| `installer/` | Inno Setup script |

## Guidelines

- **UI thread must never be blocked.** Heavy work (encoding, disk I/O, OCR)
  belongs on background threads.
- **Don't break the P/Invoke hooks** — global hotkeys, the clipboard format
  listener and the tray icon all flow through `HwndHook`.
- **Zero tolerance for native memory leaks.** Use RAII / C++/WinRT idioms;
  never let exceptions cross the P/Invoke boundary.
- New logic that can be tested without the UI goes in a service class with tests.
- User-facing strings go in **both** `Resources/Strings.resx` (EN) and
  `Resources/Strings.tr.resx` (TR).
- Follow the existing code style (enforced via `.editorconfig`); build must stay
  warning-free (warnings are errors).

## Pull Requests

1. Fork and create a topic branch from `master`.
2. Keep PRs focused — one feature/fix per PR.
3. Make sure CI is green (build + tests).
4. Update `CHANGELOG.md` under an *Unreleased* heading when the change is user-visible.

## Releases (maintainers)

Bump `<Version>` in `AIO_Hybrid_Clipboard.csproj`, update `CHANGELOG.md`, then:

```bat
git tag v1.x.y
git push origin v1.x.y
```

The release workflow builds the installer and publishes the GitHub release
automatically; the in-app updater picks it up from there.
