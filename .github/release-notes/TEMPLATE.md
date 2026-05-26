<!--
  Release-notes template for Foliant.

  HOW TO USE
  ----------
  1. Copy this file to `.github/release-notes/v<MAJOR>.<MINOR>.<PATCH>.md`
     (the filename MUST match the git tag exactly — the release workflow reads
     `.github/release-notes/${{ github.ref_name }}.md` and fails the build if it
     is missing).
  2. Fill in every section below; delete sections that do not apply rather than
     leaving them empty.
  3. Keep wording in sync with the matching `CHANGELOG.md` section.

  Tip: derive the bullet lists from the corresponding `CHANGELOG.md` entry so the
  two never drift apart.
-->

# Foliant vX.Y.Z

> One-line summary of what this release is and who it is for.

## Highlights

- Most important user-facing change.
- Second headline item.

## Added

- New features introduced in this release.

## Changed

- Behaviour or default changes (note anything potentially breaking).

## Fixed

- Bug fixes.

## Known limitations

- Anything users should be aware of that is incomplete, deferred, or blocked.

## Install

1. Download the installer for your tier from the assets below:
   - `Foliant-Setup-<tier>-vX.Y.Z.exe` (Basic / Standard / Full).
2. (Recommended) Verify the download against `SHA256SUMS` before running it.
3. Run the installer and follow the prompts.

### Verify the download (SHA256)

`SHA256SUMS` is published alongside the installers. Compare the hash of your
download with the value listed there.

PowerShell:

```powershell
# Prints the SHA-256 of the downloaded installer in lowercase
(Get-FileHash .\Foliant-Setup-Full-vX.Y.Z.exe -Algorithm SHA256).Hash.ToLower()
# Compare it against the matching line in SHA256SUMS
Select-String -Path .\SHA256SUMS -Pattern 'Foliant-Setup-Full-vX.Y.Z.exe'
```

bash (WSL / Git Bash):

```bash
sha256sum -c SHA256SUMS 2>/dev/null | grep Foliant-Setup-Full
```

The hashes must match exactly. If they do not, do not run the installer.

## System requirements

- Windows 10 21H2 (build 19044) or later, 64-bit (x64).
