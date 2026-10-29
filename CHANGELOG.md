# Changelog

All notable changes to Halo are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project will use
[Semantic Versioning](https://semver.org/) once it has releases.

## [Unreleased]

First public source release. There are no binary releases yet; build from source.

### Added
- Open-source release under the MIT License, with contribution, conduct, security and support guides, issue and
  pull request templates, and a Windows CI workflow (restore, Release build, unit tests).

### Included in this source release
- **Island presentations:** an idle thin line, a floating compact pill with a detached second-activity bubble, and an
  expanded island with Today, Pomodoro, To-do and Shelf pages.
- **Live Activity Engine 2.0:** concurrent activities, one deterministic scheduler, and an activity lifecycle
  (end, dismiss, expiry, provider stop, history).
- **Activities:** media now playing and playback controls, volume, focus and countdown timers, reminders and to-dos,
  battery and charging, network status, brightness, Bluetooth headphone connection, and a camera-in-use indicator
  (off by default).
- **Desktop behaviour:** Halo's own Do Not Disturb, full-screen suppression, Reduce Motion, dark and light themes, an
  optional global shortcut, a tray icon, and native settings, tasks and shelf windows.
- **Accessibility:** a UI Automation provider with Invoke, ExpandCollapse and alert announcements.
- **Privacy:** local-only JSON storage under `%LOCALAPPDATA%\ProjectHalo`, a content-free diagnostic log, private text
  hidden by default, and no telemetry or network calls.
- Development-only MSIX packaging scripts (`packaging/`). No package or certificate is distributed.

### Known limitations
- See [Current limitations](README.md#current-limitations) in the README.
