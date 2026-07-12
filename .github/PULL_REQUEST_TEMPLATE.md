## What does this PR do?

<!-- Short summary of the change and the motivation behind it. -->

## Checklist

- [ ] Builds with `dotnet build` (x64) without new warnings
- [ ] `dotnet test AIO_Hybrid_Clipboard.Tests` passes
- [ ] Borderless window behavior and P/Invoke hooks (hotkeys, clipboard listener, tray) still work
- [ ] UI thread is never blocked (heavy work stays on background threads)
- [ ] New user-facing strings added to both `Strings.resx` and `Strings.tr.resx`
