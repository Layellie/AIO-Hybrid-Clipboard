# Security Policy

## Supported Versions

Only the latest release receives security fixes. Please update via the in-app
updater (Settings → Check for updates) or from
[Releases](https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/latest).

## Reporting a Vulnerability

Please **do not** open a public issue for security problems.

Use GitHub's private reporting instead: go to the repository's **Security** tab
→ **Report a vulnerability**, or email `sametkasmer16@gmail.com`.

You can expect an initial response within a few days. Please include steps to
reproduce and the app version (shown in the settings drawer).

## Scope Notes

- The app stores clipboard history and screenshots **locally only**
  (`AIO_Cache/` next to the executable). Nothing is uploaded anywhere.
- The only network call is the update check against the public GitHub Releases
  API; installers are downloaded exclusively from this repository's releases.
