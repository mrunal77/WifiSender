# WifiSender

A cross-platform desktop application to send and receive files over WiFi or LAN.

 [![CI](https://github.com/mrunal77/WifiSender/actions/workflows/ci.yml/badge.svg)](https://github.com/mrunal77/WifiSender/actions/workflows/ci.yml)
 [![Coverage](https://codecov.io/gh/mrunal77/WifiSender/branch/main/graph/badge.svg)](https://codecov.io/gh/mrunal77/WifiSender)
[![Release](https://img.shields.io/github/v/release/mrunal77/WifiSender)](https://github.com/mrunal77/WifiSender/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Built with AI](https://img.shields.io/badge/Built%20with-AI%20on%20Opencode-blueviolet)](https://opencode.ai)

## Table of Contents

- [Features](#features)
- [Installation](#installation)
- [Building from Source](#building-from-source)
- [Usage](#usage)
- [Firewall Configuration](#firewall-configuration)
- [Architecture](#architecture)
- [Releases](#releases)
 - [Contributing](#contributing)
 - [Testing](#testing)
- [Security](#security)
- [License](#license)

## Features

- **Cross-platform** — Windows, Linux, and macOS (Apple Silicon + Intel)
- **Auto-detect local IP** — automatically finds your network interface
- **Send files** — select files and send to any device on your network
- **Receive files** — start receiving and save to a selected folder
- **Progress tracking** — real-time transfer speed (MB/s) and ETA
- **Device discovery** — UDP-based automatic peer discovery
- **Dark / light themes** — adaptive theme that follows system preference
- **Drag and drop** — drop files directly onto the app
- **Firewall helper** — auto-configures ufw / firewalld / iptables

## Installation

Download the latest release for your platform from the
[**Releases page**](https://github.com/mrunal77/WifiSender/releases/latest):

| Platform | Package | Notes |
|----------|---------|-------|
| Windows (x64) | [`WifiSender-<ver>-win-x64.exe`](https://github.com/mrunal77/WifiSender/releases/latest) (+ `.zip`) | Run directly |
| Linux (x64) | [`WifiSender-<ver>-linux-x64.AppImage`](https://github.com/mrunal77/WifiSender/releases/latest) | `chmod +x` then run |
| macOS (Apple Silicon) | [`WifiSender-<ver>-osx-arm64.dmg`](https://github.com/mrunal77/WifiSender/releases/latest) | Open `.dmg`, drag to Applications |
| macOS (Intel) | [`WifiSender-<ver>-osx-x64.dmg`](https://github.com/mrunal77/WifiSender/releases/latest) | Open `.dmg`, drag to Applications |

### Linux prerequisites

The AppImage requires FUSE:

```bash
# Ubuntu / Debian
sudo apt install libfuse2

# Fedora
sudo dnf install fuse-libs
```

### Verifying downloads

Each release ships `SHA256SUMS.txt`. Verify a downloaded asset:

```bash
sha256sum -c SHA256SUMS.txt --ignore-missing
```

All artifacts carry **build provenance attestations** (SLSA). Verify with:

```bash
gh attestation verify <asset> --repo mrunal77/WifiSender
```

## Building from Source

Requires **.NET 10.0 SDK** or later.

```bash
# Clone
git clone https://github.com/mrunal77/WifiSender.git
cd WifiSender

# Debug build
dotnet build

# Release build (self-contained, single-file)
dotnet publish -c Release -o ./publish

# Run
./publish/WifiSender
```

See [`docs/release-process.md`](docs/release-process.md) for packaging scripts
and the full release workflow.

## Usage

### Sending files

1. Open the app on both devices connected to the same network.
2. On the **receiving** device: click **START RECEIVING**.
3. On the **sending** device:
   - Enter the receiver's IP address (or use **Scan** to discover peers).
   - Click **Select Files** to choose files (or drag and drop).
   - Click **SEND**.

### Receiving files

1. Click **START RECEIVING** — the app listens on port **5555** (TCP).
2. Incoming transfers appear with progress, speed, and ETA.
3. Received files are saved to the selected download folder (`~/Downloads` by default).

### Keyboard shortcuts

| Action | Shortcut |
|--------|----------|
| Send files | `Enter` |

### Tips

- Use the **Test** button to verify connectivity before sending.
- Click the **Local IP** label to copy your IP to the clipboard.
- Open the download folder directly with the **Open Download Folder** button.

## Firewall Configuration

Device discovery uses **UDP port 5556** and file transfers use **TCP port 5555**
(configurable). Your firewall must allow these ports.

### Automatic setup (Linux)

```bash
sudo scripts/setup-firewall.sh
```

The script auto-detects your firewall (ufw, firewalld, iptables, or nftables)
and adds the required rules. You can also click **Fix Firewall** in the app,
which launches the script via Polkit.

### Manual setup

| Firewall | Command |
|----------|---------|
| ufw | `sudo ufw allow 5556/udp && sudo ufw allow 5555/tcp` |
| firewalld | `sudo firewall-cmd --add-port=5556/udp --add-port=5555/tcp && sudo firewall-cmd --runtime-to-permanent` |
| iptables | `sudo iptables -A INPUT -p udp --dport 5556 -j ACCEPT && sudo iptables -A INPUT -p tcp --dport 5555 -j ACCEPT` |

## Architecture

```
WifiSender/
├── src/                        # Source code
│   ├── WifiSender/             # Desktop GUI app (Avalonia UI)
│   └── WifiSender.Transfer/    # Transfer engine (TCP, session codecs, protocols)
├── test/                       # Unit & integration tests
│   └── WifiSender.Tests/
├── docs/                       # Documentation
├── scripts/                    # Release & firewall helper scripts
├── packaging/                  # Platform packaging scripts
├── installer/                  # Platform installer assets
├── .github/workflows/          # CI / Release / Security pipelines
└── ...
```

Key technologies:
- [Avalonia UI](https://avaloniaui.net/) — cross-platform XAML framework
- [.NET 10.0](https://dotnet.microsoft.com/) — runtime and SDK
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) — MVVM source generators
- [MinVer](https://github.com/dotnet/MinVer) — semantic versioning from git tags

See [`SPEC.md`](SPEC.md) for the full application specification.

## Releases

Releases are automated via GitHub Actions. See
[`docs/release-process.md`](docs/release-process.md) for the full process.

Each release includes:

| Asset | Description |
|-------|-------------|
| `WifiSender-<ver>-win-x64.exe` | Windows installer (self-contained) |
| `WifiSender-<ver>-win-x64.zip` | Windows portable archive |
| `WifiSender-<ver>-linux-x64.AppImage` | Linux AppImage (FUSE required) |
| `WifiSender-<ver>-osx-arm64.dmg` | macOS disk image (Apple Silicon) |
| `WifiSender-<ver>-osx-x64.dmg` | macOS disk image (Intel) |
| `SHA256SUMS.txt` | SHA-256 checksums for all assets |
| `release-manifest.json` | Version + per-asset hashes |
| `sbom.spdx.json` | SPDX 2.2 software bill of materials |

Versions are derived from git tags via [MinVer](https://github.com/dotnet/MinVer)
and are **never** edited by hand.

### Cutting a release

1. Go to **Actions → Release → Run workflow**.
2. Set `version_type` (`patch` / `minor` / `major` / `prerelease`) or provide
   an explicit `version`.
3. Optionally enable `dry_run` to verify the pipeline first.

## Testing

Run the unit‑test suite and collect coverage:

```bash
dotnet test WifiSender.sln -c Release --no-build --no-restore \
  --collect:"XPlat Code Coverage" \
  --results-directory ./TestResults
```

The coverage report (Cobertura format) is emitted under `TestResults/**/coverage.cobertura.xml`. You can view it locally with a tool such as `reportgenerator` or upload it to a service like Codecov (the CI workflow already does this).

## Contributing

1. Fork the repository.
2. Create a feature branch (`git checkout -b feat/my-feature`).
3. Commit with [conventional commits](https://www.conventionalcommits.org/)
   (`feat:`, `fix:`, `refactor:`, etc.).
4. Push and open a Pull Request.

CI runs automatically on PRs (build, format, test on Linux + Windows).

Dependabot keeps NuGet and GitHub Actions dependencies up to date.

## Security

If you discover a security vulnerability, please report it responsibly. Do **not**
open a public issue. Instead, contact the maintainer directly.

CodeQL analysis runs weekly via
[`.github/workflows/security.yml`](.github/workflows/security.yml).

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file
for details.

---

<p align="center">
  Built with AI on <a href="https://opencode.ai">Opencode</a>
</p>
