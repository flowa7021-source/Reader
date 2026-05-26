# Privacy Policy

**Foliant** — version 0.1 (alpha)

> **Alpha draft** — describes the software's actual behavior; pending legal review before 1.0.

Foliant is an **offline-first** desktop application. It does **not** collect analytics or
telemetry, and your documents never leave your machine. This document explains the only two
features that touch the network or write diagnostic data, and how to control them.

## What we do NOT do

- No usage analytics, tracking, or telemetry.
- No uploading of your documents, annotations, or any file content anywhere.
- No hardware fingerprinting or device IDs.

## Crash reports — opt-in, local only

Disabled by default. If you enable **Send crash reports** (Tools → Settings), unhandled errors are
written as JSON files to `%LOCALAPPDATA%\Foliant\CrashReports\` containing the timestamp, error
type/message, and stack trace. These files **stay on your machine** — nothing is transmitted. You
choose whether to share them (e.g. when filing a GitHub issue).

## Update check — opt-out, version only

Enabled by default; turn it off with **Check for updates** (Tools → Settings). At most once per
day, Foliant requests the latest release tag from the GitHub Releases API to tell you when a newer
version exists. The request carries no personal data or identifiers beyond what any HTTP client
sends; only the version number is used. Network failures are ignored silently.

## License and trial data

Your license key and trial state are stored **locally** and protected with the Windows Data
Protection API (DPAPI). They are not tied to a hardware ID and are never sent off the machine.

## Local data locations

- `%APPDATA%\Foliant\` — settings, license, trial, bookmarks.
- `%LOCALAPPDATA%\Foliant\` — cache, logs, annotations, autosave, crash reports, backups.

You control this data; uninstalling offers to remove it.

## Contact

Issues and questions: <https://github.com/flowa7021-source/Reader/issues>.

---

© 2026 Foliant contributors.
