# Release process

WifiSender versions are derived from git tags by MinVer: the version **is** the
tag. There are no version numbers to edit by hand.

## Versioning rules

- Tag format: `vMAJOR.MINOR.PATCH`, e.g. `v1.4.3`.
- Pre-releases: `vMAJOR.MINOR.PATCH-rc.N`, e.g. `v1.5.0-rc.1`.
- `scripts/next-version.sh` computes the next version deterministically:

  | Request | Last tag        | Next version |
  |---------|-----------------|--------------|
  | patch   | `v1.4.3`        | `v1.4.4`     |
  | patch   | `v1.4.3-rc.2`   | `v1.4.3` (stable of the rc) |
  | minor   | `v1.4.3`        | `v1.5.0`     |
  | major   | `v1.4.3`        | `v2.0.0`     |
  | prerelease | `v1.4.3`     | `v1.4.4-rc.1`|
  | prerelease | `v1.4.3-rc.1` | `v1.4.3-rc.2`|

- The first release is `v1.0.0` (MinVer minimum). Until the first tag, builds
  are `1.0.0-alpha.0.<height>`.

## Running a release

1. Open **Actions → Release → Run workflow** (manual trigger).
2. Set inputs:
   - `version_type` — `patch`, `minor`, `major`, or `prerelease`
     (default `patch`). Ignored when `version` is set.
   - `version` — optional explicit version (e.g. `1.4.3`, `1.5.0-rc.1`).
   - `platforms` — `all`, `windows`, `linux`, or `macos` (default `all`).
   - `dry_run` — build and verify everything **without** tagging or publishing.
3. Optionally run a `dry_run` first to sanity-check the pipeline.
4. Run for real. The workflow:
   1. **prepare** — computes the version, rejects if the tag already exists,
      creates and pushes the annotated tag `v<version>`.
   2. **package** — builds per-platform packages, validates them, and attaches
      SLSA-style build provenance (GitHub Artifact Attestations).
   3. **finalize** — collects packages, writes `SHA256SUMS.txt`, a
      `release-manifest.json`, and an SPDX SBOM, generates release notes, and
      publishes the GitHub Release.

### Approval gate

The **finalize** job runs in the `production-release` environment. To require
human approval before anything is published, add reviewers to that environment
in **Settings → Environments → production-release**. (The environment is
auto-created the first time the workflow runs.)

## Typical flows

- **Bug fix / patch** → run with `version_type: patch`.
- **Feature** → `minor`.
- **Breaking change** → `major`.
- **Release candidate** → `prerelease` until stable, then `patch` (which
  resolves to the rc's base version, e.g. `v1.5.0`).

## What is shipped

Per release:

- `WifiSender-<version>-win-x64.exe` (+ `.zip`)
- `WifiSender-<version>-linux-x64.AppImage`
- `WifiSender-<version>-osx-arm64.dmg` (Apple Silicon)
- `WifiSender-<version>-osx-x64.dmg` (Intel)
- `SHA256SUMS.txt` — checksums for every asset
- `release-manifest.json` — version + per-asset hashes
- `sbom.spdx.json` — SPDX 2.2 SBOM of shipped components

Every package and the SBOM/manifest carry build-provenance attestations,
visible under the release and on the artifact page
(`gh attestation verify` works against them).

## Notes and invariants

- Releases are **never** triggered by pushes or tags (only manual), so tagging
  cannot recurse.
- `concurrency: group: release` serializes releases — a second run waits.
- A tag that already exists aborts the run; tags are never overwritten.
- The packaging scripts honor the `VERSION` env var. Locally they fall back to
  `dist/version.txt`, written by the `WriteVersionInfo` MSBuild target during
  every build (MinVer output).
