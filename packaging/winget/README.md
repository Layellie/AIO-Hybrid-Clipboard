# winget Package Manifests

These manifests make the app installable via `winget install Layellie.AIOHybridClipboard`.

## First-time submission (manual, once)

1. Fork [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs).
2. Copy the three `Layellie.AIOHybridClipboard*.yaml` files into
   `manifests/l/Layellie/AIOHybridClipboard/1.5.0/` in your fork.
3. Validate locally: `winget validate --manifest manifests/l/Layellie/AIOHybridClipboard/1.5.0`
4. Open a PR against `microsoft/winget-pkgs` — automated validation runs, then a
   human moderator approves (usually a few days).

## Updating for a new release

After each release, the fastest path is [wingetcreate](https://github.com/microsoft/winget-create):

```bat
wingetcreate update Layellie.AIOHybridClipboard ^
  --version 1.6.0 ^
  --urls https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/download/v1.6.0/AIO_Hybrid_Clipboard_Setup_v1.6.0.exe ^
  --submit
```

It downloads the installer, computes the SHA-256, bumps the manifests and opens
the PR for you. Keep the copies in this folder in sync for reference.

Compute the hash manually with: `Get-FileHash <setup.exe> -Algorithm SHA256`
