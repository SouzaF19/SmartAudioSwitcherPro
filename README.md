<p align="center">
  <img src="assets/logo.png" alt="SmartAudioSwitcher Logo" width="120" height="120">
</p>

<h1 align="center">SmartAudioSwitcher Pro</h1>

<p align="center">
  <strong>🎵 Intelligent Windows Audio Device Switcher</strong><br>
  Automatically switches audio output based on connected devices, active applications, and user-defined profiles.
</p>

<p align="center">
  <a href="https://github.com/SouzaF19/SmartAudioSwitcherPro/releases/latest">
    <img src="https://img.shields.io/github/v/release/SouzaF19/SmartAudioSwitcherPro?style=for-the-badge&logo=github&label=Latest%20Release&color=2ea44f" alt="Latest Release">
  </a>
  <a href="https://github.com/SouzaF19/SmartAudioSwitcherPro/blob/main/LICENSE">
    <img src="https://img.shields.io/github/license/SouzaF19/SmartAudioSwitcherPro?style=for-the-badge&color=8957e5" alt="License">
  </a>
  <a href="https://github.com/SouzaF19/SmartAudioSwitcherPro/actions/workflows/build.yml">
    <img src="https://img.shields.io/github/actions/workflow/status/SouzaF19/SmartAudioSwitcherPro/build.yml?style=for-the-badge&logo=githubactions&logoColor=white&label=Build" alt="Build Status">
  </a>
  <a href="https://www.microsoft.com/windows/windows-11">
    <img src="https://img.shields.io/badge/Windows-10%2F11-0078d6?style=for-the-badge&logo=windows&logoColor=white" alt="Windows 10/11">
  </a>
  <a href="https://dotnet.microsoft.com/download/dotnet/8.0">
    <img src="https://img.shields.io/badge/.NET-8.0-512bd4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 8.0">
  </a>
</p>

<p align="center">
  <a href="#-the-problem">The Problem</a> •
  <a href="#-the-solution">The Solution</a> •
  <a href="#-features">Features</a> •
  <a href="#-installation">Installation</a> •
  <a href="#-usage">Usage</a> •
  <a href="#-configuration">Configuration</a> •
  <a href="#-architecture">Architecture</a> •
  <a href="#-contributing">Contributing</a>
</p>

---

## 🎯 The Problem

Windows audio management is **surprisingly primitive**:

| Scenario | Windows Behavior | User Pain |
|----------|------------------|-----------|
| 🎧 Plug in headset | Stays on speakers/HDMI | Manual switch every time |
| 🔌 Unplug headset | Often stays on "disconnected" device | No audio until manual fix |
| 🎮 Game + 🎧 Discord | Single output for everything | Can't route game→speakers, voice→headset |
| 📺 HDMI monitor sleep | Switches to monitor audio (silent) | Audio disappears mysteriously |
| 🔄 Bluetooth connect | Unpredictable priority | Fight with Windows for control |

**Result:** Users constantly open *Sound Settings* → *Choose output device* → *Apply* → *Close*. Dozens of times per day.

---

## 💡 The Solution

**SmartAudioSwitcher** runs quietly in your system tray and handles audio routing **intelligently**:

```
┌─────────────────────────────────────────────────────────────────┐
│  🎧 Headset plugged in          →  Instant switch to headset   │
│  🔌 Headset unplugged           →  Restore previous device     │
│  🎮 Game launched               →  Route to speakers/HDMI      │
│  💬 Discord/Teams call starts   →  Route to headset            │
│  📺 HDMI monitor wakes/sleeps   →  Smart fallback handling     │
│  🎵 Spotify playing             →  User-defined preference     │
└─────────────────────────────────────────────────────────────────┘
```

### Core Philosophy

> **Zero-config for 80% of users. Deep customization for the other 20%.**

- **Works out of the box** — Install, run, forget
- **Profile-based** — Define rules once, apply forever
- **Per-app routing** — Different apps → different devices
- **Event-driven** — Reacts to hardware + software events in real-time
- **Lightweight** — ~15MB RAM, near-zero CPU when idle

---

## ✨ Features

### 🔌 Device-Aware Switching
- **Hot-plug detection** — USB/Bluetooth/HDMI connect/disconnect events
- **Device capabilities** — Recognizes headsets, speakers, HDMI, monitors, virtual devices
- **Fallback chains** — "If headset gone → try speakers → try HDMI → system default"

### 🎮 Per-Application Routing
```yaml
# Example: Route apps intelligently
profiles:
  gaming:
    - process: "*.exe"          # All games
      device: "Speakers (Realtek)"
    - process: "Discord.exe"
      device: "Headset (Logitech G Pro)"
    - process: "chrome.exe"
      device: "Headset (Logitech G Pro)"  # Browser media → headset
```

### 📅 Context Profiles
| Profile | Trigger | Behavior |
|---------|---------|----------|
| **Work** | 9AM–6PM, docked | Monitor speakers + headset for calls |
| **Gaming** | Game process detected | Game audio → speakers, voice → headset |
| **Night** | 10PM–7AM | Force headset, lower volume |
| **Presentation** | PowerPoint fullscreen | HDMI audio, suppress notifications |

### 🛠️ Power User Features
- **CLI control** — `saswitch set-device "Headset"` for scripts/shortcuts
- **REST API** — Local HTTP API for Stream Deck, Home Assistant, etc.
- **Audio metering** — Visual feedback in tray (optional)
- **Hotkeys** — Global shortcuts for quick device cycling
- **Logging** — Structured logs for debugging

---

## 📦 Installation

### Option 1: Windows Installer (Recommended)
[![Download](https://img.shields.io/badge/Download-MSIX-0078d6?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/SouzaF19/SmartAudioSwitcherPro/releases/latest/download/SmartAudioSwitcher.msix)

```powershell
# Or via winget (coming soon)
winget install SouzaF19.SmartAudioSwitcher
```

### Option 2: Portable (No Admin Required)
[![Download Portable](https://img.shields.io/badge/Download-Portable%20ZIP-2ea44f?style=for-the-badge&logo=github&logoColor=white)](https://github.com/SouzaF19/SmartAudioSwitcherPro/releases/latest/download/SmartAudioSwitcher-Portable.zip)

1. Download & extract anywhere
2. Run `SmartAudioSwitcher.exe`
3. (Optional) Create shortcut in `shell:startup` for auto-start

### Option 3: Build from Source
```bash
git clone https://github.com/SouzaF19/SmartAudioSwitcherPro.git
cd SmartAudioSwitcherPro
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

> **Requirements:** Windows 10 1903+ / Windows 11, .NET 8 Runtime (included in self-contained build)

---

## 🚀 Usage

### First Run
1. Launch → Icon appears in system tray 🔊
2. Right-click tray icon → **"Open Dashboard"** (or press `Ctrl+Shift+A`)
3. See detected devices, current routing, active profile

### Dashboard Overview
```
┌─ SmartAudioSwitcher ────────────────────────────────────────┐
│  🟢 Running  |  Profile: Gaming  |  Output: Headset (USB)   │
├──────────────────────────────────────────────────────────────┤
│  DEVICES                                                      │
│  🎧 Headset (Logitech G Pro)     [DEFAULT] [COMMUNICATIONS]  │
│  🔊 Speakers (Realtek)           [READY]                     │
│  📺 LG 27" HDMI                  [DISCONNECTED]              │
├──────────────────────────────────────────────────────────────┤
│  ACTIVE ROUTING                                               │
│  🎮 eldenring.exe        →  🔊 Speakers                      │
│  💬 Discord.exe          →  🎧 Headset                       │
│  🌐 chrome.exe           →  🎧 Headset                       │
│  🎵 Spotify.exe          →  🔊 Speakers                      │
├──────────────────────────────────────────────────────────────┤
│  [Edit Profiles]  [Device Settings]  [Logs]  [Minimize]      │
└──────────────────────────────────────────────────────────────┘
```

### Quick Actions (Tray Menu)
- **Cycle Output** — `Ctrl+Alt+O` (customizable)
- **Toggle Mute** — `Ctrl+Alt+M`
- **Force Headset** — One-click override
- **Reload Config** — Apply changes without restart

---

## ⚙️ Configuration

Config file: `%APPDATA%\SmartAudioSwitcher\config.yaml`

```yaml
# SmartAudioSwitcher Configuration
# Full schema: https://github.com/SouzaF19/SmartAudioSwitcherPro/wiki/Configuration

general:
  start_minimized: true
  check_updates: true
  log_level: "Information"

# Device matching (friendly names, partial match supported)
devices:
  headset: ["Headset", "Headphones", "WH-1000XM", "G Pro"]
  speakers: ["Speakers", "Realtek", "Audio"]
  hdmi: ["HDMI", "Monitor", "Display"]

# Profiles evaluated in order; first match wins
profiles:
  - name: "Gaming"
    when:
      - process_match: "*game*"
      - process_match: "steam.exe"
    routing:
      default: "speakers"
      exceptions:
        - process: "Discord.exe"
          device: "headset"
        - process: "chrome.exe"
          device: "headset"

  - name: "Work Hours"
    when:
      - time_between: "09:00-18:00"
      - docked: true
    routing:
      default: "hdmi"
      exceptions:
        - process: "Teams.exe"
          device: "headset"

  - name: "Night Mode"
    when:
      - time_between: "22:00-07:00"
    routing:
      default: "headset"
      volume_limit: 0.4

  - name: "Default"
    when: []
    routing:
      default: "speakers"
      fallback_chain: ["headset", "hdmi", "system_default"]

hotkeys:
  cycle_output: "Ctrl+Alt+O"
  toggle_mute: "Ctrl+Alt+M"
  force_headset: "Ctrl+Alt+H"
  open_dashboard: "Ctrl+Shift+A"

api:
  enabled: false
  port: 52841
  allow_remote: false
```

### Device Naming Tips
- Use **partial names** — `"Headset"` matches `"Headset (Logitech G Pro)"`
- Check **exact names** in Dashboard → Device Settings
- **Communication device** vs **Default device** — Windows separates these; SAS handles both

---

## 🏗️ Architecture

```
┌────────────────────────────────────────────────────────────────┐
│                      SmartAudioSwitcher                        │
├────────────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐             │
│  │  Tray UI    │  │  Dashboard  │  │  CLI/API    │  ← Interfaces│
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘             │
│         │                │                │                    │
│         └────────────────┼────────────────┘                    │
│                          ▼                                     │
│  ┌─────────────────────────────────────────────────────────┐  │
│  │                    Core Engine                          │  │
│  │  ┌────────────┐ ┌────────────┐ ┌────────────┐           │  │
│  │  │ Profile    │ │ Device     │ │ Routing    │           │  │
│  │  │ Evaluator  │ │ Monitor    │ │ Engine     │           │  │
│  │  └────────────┘ └────────────┘ └────────────┘           │  │
│  └─────────────────────────────────────────────────────────┘  │
│                          │                                     │
│         ┌────────────────┼────────────────┐                   │
│         ▼                ▼                ▼                   │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐             │
│  │ Windows     │ │ Audio       │ │ Process     │  ← Platform  │
│  │ CoreAudio   │ │ Session     │ │ Watcher     │     Adapters │
│  │ API (MMDev) │ │ Manager     │ │ (WMI/ETW)   │             │
│  └─────────────┘ └─────────────┘ └─────────────┘             │
└────────────────────────────────────────────────────────────────┘
```

### Key Technologies
| Layer | Technology | Purpose |
|-------|------------|---------|
| **Audio API** | `NAudio` + `CoreAudio` (COM interop) | Device enumeration, endpoint control, session management |
| **Device Events** | `ManagementEventWatcher` (WMI) | USB/Bluetooth/HDMI plug/unplug detection |
| **Process Monitor** | `System.Diagnostics` + ETW | Real-time process start/end, window focus |
| **UI** | `WPF` + `Hardcodet.Wpf.TaskbarNotification` | Modern, native-feeling interface |
| **Config** | `YamlDotNet` | Human-readable, version-controllable config |
| **DI/Hosting** | `Microsoft.Extensions.Hosting` | Clean startup, lifecycle, logging |

---

## 🤝 Contributing

### Development Setup
```bash
# Prereqs: .NET 8 SDK, Visual Studio 2022 / VS Code
git clone https://github.com/SouzaF19/SmartAudioSwitcherPro.git
cd SmartAudioSwitcherPro
dotnet restore
dotnet build
```

### Project Structure
```
src/
├── SmartAudioSwitcher.Core/       # Business logic, interfaces, models
├── SmartAudioSwitcher.Platform/   # Windows APIs (CoreAudio, WMI, ETW)
├── SmartAudioSwitcher.UI/         # WPF Dashboard, Tray, ViewModels
├── SmartAudioSwitcher.CLI/        # Command-line interface
├── SmartAudioSwitcher.Api/        # REST API (ASP.NET Core Minimal)
└── SmartAudioSwitcher.App/        # Host, DI composition, entry point
tests/
├── SmartAudioSwitcher.UnitTests/
└── SmartAudioSwitcher.IntegrationTests/
```

### Good First Issues
- 🐛 [Fix device name matching for virtual cables (VB-Cable, Voicemeeter)](https://github.com/SouzaF19/SmartAudioSwitcherPro/issues?q=label%3A%22good+first+issue%22)
- 📝 [Add configuration schema validation with helpful errors](https://github.com/SouzaF19/SmartAudioSwitcherPro/issues?q=label%3A%22good+first+issue%22)
- 🎨 [Improve dashboard device icons (SVG → colored per type)](https://github.com/SouzaF19/SmartAudioSwitcherPro/issues?q=label%3A%22good+first+issue%22)

### Pull Request Process
1. Fork → Create branch (`feat/awesome-feature` or `fix/annoying-bug`)
2. Make changes with tests
3. Run `dotnet format` and `dotnet test`
4. Open PR with clear description + screenshots (if UI)
5. CI passes → Review → Merge

---

## 📄 License

**MIT License** — Free for personal and commercial use.  
See [LICENSE](LICENSE) for details.

---

## 🙏 Acknowledgments

| Project | Role | License |
|---------|------|---------|
| [NAudio](https://github.com/naudio/NAudio) | Core audio interop | MIT |
| [Hardcodet.Wpf.TaskbarNotification](https://github.com/hardcodet/wpf-notifyicon) | System tray icon | CPOL |
| [Microsoft.Extensions.Hosting](https://github.com/dotnet/runtime) | Generic host, DI, logging | MIT |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | YAML parsing | MIT |
| [MaterialDesignInXamlToolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) | Beautiful WPF controls | MIT |

---

## 📞 Support & Community

| Channel | Purpose |
|---------|---------|
| [GitHub Issues](https://github.com/SouzaF19/SmartAudioSwitcherPro/issues) | Bug reports, feature requests |
| [GitHub Discussions](https://github.com/SouzaF19/SmartAudioSwitcherPro/discussions) | Questions, configs, tips |
| [Wiki](https://github.com/SouzaF19/SmartAudioSwitcherPro/wiki) | Deep-dive docs, troubleshooting |

---

<p align="center">
  <strong>Made with ❤️ for Windows power users who just want their audio to work.</strong><br>
  <sub>If this saves you one trip to Sound Settings per day, it's done its job.</sub>
</p>

<p align="center">
  <a href="https://github.com/SouzaF19/SmartAudioSwitcherPro/stargazers">
    <img src="https://img.shields.io/github/stars/SouzaF19/SmartAudioSwitcherPro?style=social" alt="Star on GitHub">
  </a>
  <a href="https://github.com/SouzaF19/SmartAudioSwitcherPro/fork">
    <img src="https://img.shields.io/github/forks/SouzaF19/SmartAudioSwitcherPro?style=social" alt="Fork on GitHub">
  </a>
</p>