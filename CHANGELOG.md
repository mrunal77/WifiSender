# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
via [MinVer](https://github.com/dotnet/MinVer) (versions come from git tags, never
edited by hand).

Per-release commit summaries are generated automatically by
[`scripts/release-notes.sh`](scripts/release-notes.sh) and attached to each
GitHub Release. This file tracks the notable changes.

## [Unreleased]

### Added
- MinVer-based versioning (`Directory.Build.props`): the version is derived
  from `vMAJOR.MINOR.PATCH` git tags, so version numbers are never hard-coded.
- Version display in the app header (stamped from the build, e.g. `v1.4.3`).
- Release automation:
  - `.github/workflows/ci.yml` — build, format, test, and publish validation on
    push/PR (Linux + Windows).
  - `.github/workflows/release.yml` — manual release pipeline that computes the
    next version, tags, builds per-platform packages, validates and attests
    them (SLSA-style provenance), aggregates checksums/SBOM/manifest, and
    publishes the GitHub Release.
  - `.github/workflows/security.yml` — CodeQL analysis + auto-merge for
    Dependabot PRs (human review still required for major bumps).
  - `.github/dependabot.yml` — weekly NuGet and monthly GitHub Actions updates.
- `scripts/next-version.sh` — deterministic next-version computation
  (`patch` / `minor` / `major` / `prerelease`, with release-candidate flow).
- `scripts/release-notes.sh` — grouped commit summary for release notes.
- SBOM (SPDX 2.2) generation during release (via `Microsoft.Sbom.DotNetTool`),
  shipped alongside each release.
- Release manifest + SHA256 checksums shipped with every release.

### Changed
- Single-file package naming is now RID-based and consistent across platforms
  (`WifiSender-<version>-win-x64.exe`, `-linux-x64.AppImage`, `-osx-arm64.dmg`,
  `-osx-x64.dmg`).

## [1.0.0-alpha.0] - 2026-06

> Alpha snapshot before formal versioning began.

### Added
- Avalonia-based cross-platform desktop UI (dark/light themes, file picker,
  toasts, progress/speed indicators).
- UDP discovery + TCP file transfer engine with chunking, resume, and
  whole-file hash verification (`src/WifiSender.Transfer/`).
- Device pairing via shared secret.
- Firewall auto-configuration helper (`scripts/setup-firewall.sh`).
- Loopback integration smoke test / benchmark (`src/WifiSender.Transfer/Bench`).
