# WifiSender — Product Roadmap

This document tracks the planned evolution of WifiSender across four phases.
Items are ordered by impact and implementation effort; the plan is to
deliver incremental value every sprint while keeping the codebase stable.

---

## Phase 1 — Quick Wins (1–2 days)

### UI Polish
- [x] Migrate code to Avalonia 12 APIs (DataValidationPlugins, drag-drop, clipboard)
- [ ] Replace hardcoded colors in `SelectedFileItem.GetCategoryDetails()` with theme tokens
- [ ] Remove unused `FluentTheme` base (everything is overridden)
- [ ] Enable window resizing and add reasonable max sizes
- [ ] Split `MainWindow.axaml` / `MainWindowViewModel.cs` into `SendView` + `ReceiveView` UserControls
- [ ] Convert `DiscoveredDevice` to inherit `ObservableObject`

### Engine
- [ ] Wire `SupportsCompression` flag with Brotli on `Data` frames
- [ ] Add `WireProtocol.Magic` preamble to reject stray connections early
- [ ] Add protocol version negotiation in handshake

### DX / CI
- [x] Restructure into `src/` and `test/` directories
- [ ] Add architecture diagrams to docs
- [ ] Add design-time sample data for Blend preview

---

## Phase 2 — Core UX Upgrades (3–5 days)

### Send Flow
- [ ] Per-file progress cards with enter/exit animations
- [ ] Inline file removal during transfer
- [ ] Pause/resume per file (requires resume engine support)

### Receive Flow
- [ ] Auto-open download folder on completion
- [ ] OS-native notification on completion / error
- [ ] Duplicate filename resolution preview (rename / skip / overwrite dialog)

### Discovery
- [ ] Continuous background scanning with debounced device list
- [ ] Device favorites / pinned peers
- [ ] Show device transfer history (last seen, last transfer size)

### Toast System
- [ ] Queue support (no overlap)
- [ ] Action buttons (Open folder / Retry / Dismiss)
- [ ] Hover-to-pause auto-dismiss
- [ ] Positioning variants (top, bottom, center)

### Empty States
- [ ] Illustrated empty states for Send tab, Receive tab, Device list
- [ ] First-run onboarding hints

### Keyboard & Accessibility
- [ ] Full Tab navigation order
- [ ] Visible focus indicators
- [ ] `AutomationProperties.Name` on all interactive controls
- [ ] Screen reader friendly status announcements

### Theme System
- [ ] Wire up `Colors.cs` / `Spacing.cs` / `Typography.cs` tokens in XAML
- [ ] Accent color picker (user-selectable brand color)
- [ ] High-contrast theme variant

---

## Phase 3 — Feature Expansion (5–8 days)

### Transport
- [ ] Auto-select QUIC when available, fallback to TCP
- [ ] TLS wrapper for TCP when pairing secret is set
- [ ] Configurable TCP socket buffer sizes in Settings

### Resume & Reliability
- [ ] Enable resume by default in app layer
- [ ] Partial-file recovery beyond resume offset
- [ ] Retry with exponential backoff on transient failures
- [ ] Circuit breaker when sending to multiple recipients

### Batch & Queue UX
- [ ] Drag-and-drop reordering of file queue
- [ ] Per-file pause/resume/cancel
- [ ] Transfer queue persistence (survive app restart)

### History
- [ ] Recent transfers list (peer, files, size, timestamp, status)
- [ ] One-click re-send from history
- [ ] Clear history option

### Settings
- [ ] Port configuration
- [ ] Default download folder
- [ ] Auto-start receive on launch toggle
- [ ] Compression toggle (on / off / auto)
- [ ] Discovery interval slider
- [ ] Theme / accent picker persistence

### Protocol
- [ ] Implement `Ping`/`Pong` keepalive with configurable timeout
- [ ] Adaptive chunk sizing based on RTT / throughput
- [ ] Optional per-chunk hashes for large files
- [ ] Connection reuse for sequential batches without full handshake

---

## Phase 4 — Modernization (ongoing)

### Icons & Assets
- [ ] Migrate inline `StreamGeometry` to external SVG assets
- [ ] Icon font fallback for platforms without Skia
- [ ] Animated icons (sending pulse, receiving wave)

### Localization
- [ ] Extract all strings to `.resx` files
- [ ] Community translation framework
- [ ] RTL layout support

### Auto-Updates
- [ ] Integrate GitHub Releases updater
- [ ] Delta updates to reduce download size
- [ ] Update channel selection (stable / beta)

### Platform Integration
- [ ] macOS menu bar / Windows system tray mode
- [ ] Background receiver (no window required)
- [ ] Launch at login / startup
- [ ] Protocol handler (`wifisender://` deep links)

### Packaging
- [ ] MSI with WiX / Advanced Installer for Windows
- [ ] AppImage with desktop integration for Linux
- [ ] PKG + notarization for macOS
- [ ] Code signing on all platforms
- [ ] SBOM generation in CI (already implemented for release)

### Telemetry & Reliability
- [ ] Optional crash reporting (privacy-first, user opt-in)
- [ ] Anonymous usage metrics (transfer sizes, success rates)
- [ ] Health checks on startup (firewall, network, port availability)

---

## Priorities

| Priority | Item | Rationale |
|----------|------|-----------|
| P0 | Brotli compression, resume by default, continuous discovery | User-perceived speed and reliability |
| P1 | Window resize, per-file progress, auto-open folder | Polish and onboarding |
| P1 | QUIC auto-fallback, TCP TLS | Security and performance |
| P2 | Settings dialog, history, batch queue | Power-user workflows |
| P2 | Accessibility pass | Inclusive design |
| P3 | Auto-updates, localization, platform tray | Distribution maturity |

---

## Out of Scope (for now)

- Web / mobile clients (server-only roadmap)
- Cloud relay / NAT traversal beyond local subnet
- End-to-end encryption beyond pairing secret
- Plugin / extension system

---

Last updated: 2026-08-18
