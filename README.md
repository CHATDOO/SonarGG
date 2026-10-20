# SonarGG ⚡

> **High-Performance Virtual Audio Mixer, Per-App Routing & DSP Suite for Windows**  
> *Low-latency, native C# audio engine designed for gamers, streamers, and audio enthusiasts.*

[![Release](https://img.shields.io/github/v/release/CHATDOO/SonarGG?color=00c853&label=Latest%20Release)](https://github.com/CHATDOO/SonarGG/releases/latest)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D6.svg)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

[🇫🇷 Lire en Français](README.fr.md)

---

## 🌟 Overview

**SonarGG** is a lightweight, standalone virtual audio mixer and routing manager for Windows. It provides full control over your audio streams with real-time hardware ChatMix, per-application routing, custom parametric EQ, low-latency microphone processing with VST3 hosting, and a compact Mini Mode with Always-on-Top overlay.

---

## 🚀 Key Features

### 🎚️ 5-Channel Virtual Mixing Console
- **Dedicated Virtual Channels**:
  - 🎮 **Gaming** (`Sonar - Gaming`)
  - 💬 **Voice Chat** (`Sonar - Chat`)
  - 🎵 **Media / Music** (`Sonar - Media`)
  - 🎧 **Auxiliary** (`Sonar - Aux`)
  - 🎙️ **Microphone** (`Sonar - Mic`)
- High-precision vertical faders with real-time 60 FPS ballistic VU-meters.
- Instant mute controls with global hotkeys.

### 📦 Application Audio Routing & Multi-Source Assignment
- **Automatic Audio Session Detection**: Detects any active software playing audio (Discord, Spotify, Steam games, Chrome, OBS, etc.).
- **1-Click Channel Routing**: Assign apps instantly between Game, Chat, Media, and Aux with segmented controls.
- **Drag & Drop**: Drag applications directly into mixer columns.
- **Smooth Horizontal Navigation**: Scroll effortlessly through your apps using the mouse wheel or fast arrow buttons `[ ◀ ]` `[ ▶ ]`.
- Individual volume faders and mute buttons per application.

### ⚖️ Real-Time ChatMix Balance
- Dynamic hardware/software blend between your game audio and voice communications (e.g., Discord).
- Instant 50/50 balance reset button and global shortcut.

### 🎛️ 10-Band Parametric Equalizer
- Interactive real-time frequency curve visualizer with calibrated dB grid.
- Tailored gaming and audio presets:
  - 🎯 **CS2 / Footsteps Boost** (Optimized for footsteps and positional cues)
  - 🎯 **Valorant Pro**
  - 🪖 **Warzone / CoD Tactical**
  - 🎵 **Deep Bass Boost**
  - 💬 **Studio Vocal Clarity**

### 🎙️ Studio Microphone Engine & VST3 Rack
- **Ultra-low latency (< 0.5 ms) DSP vocal chain**:
  - **Noise Gate**: Dynamic threshold to eliminate mechanical keyboards and ambient background noise.
  - **Studio Vocal Compressor**: Levels out peaks and whispers for consistent streaming/chat volume.
  - **80 Hz High-Pass Filter**: Cuts desk rumbles, vibrations, and plosive pops.
  - **Zero-Latency Sidetone**: Direct local mic monitoring into your headset.
- **VST3 Plugin Hosting**: Load your favorite external audio plugins, EQs, or compressors directly into the microphone signal chain.

### 🌐 7.1 Virtual Spatial Audio
- Immersive 360-degree virtual surround sound simulation optimized for competitive gaming headsets.

### 📱 Compact Mini Mode & Always-on-Top Overlay
- Ultra-compact floating HUD displaying only the essentials: the 5 faders, mutes, and ChatMix wheel.
- **Always-on-Top**: Pin the mixer over borderless windowed games or stream setups.
- Quick toggle hotkey: `Ctrl + M`.

### ⌨️ Fully Customizable Global Hotkeys
- Customize and bind all shortcuts directly from the **Hotkeys** tab.
- Operates globally, even when games are running full screen.

### 🌍 Bilingual Interface (English / French)
- Instant 1-click language switching across the entire UI via the `🌐 FR / EN` toggle.

### 🔄 In-App Auto-Updater
- Checks GitHub Releases on launch or manually from the **Settings** tab.
- In-place download, progress indicator, and self-updating restart mechanism.

---

## 📥 Download & Installation

### Option 1: Pre-built Executable (Recommended)
1. Download the latest `SonarGG.exe` or `SonarGG-Windows-x64.zip` from [Releases](https://github.com/CHATDOO/SonarGG/releases/latest).
2. Run `SonarGG.exe`. No installation required.

### Option 2: Build from Source

#### Prerequisites
- **Windows 10 / 11 (64-bit)**
- **[.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)**

#### Building
```powershell
# Clone the repository
git clone https://github.com/CHATDOO/SonarGG.git
cd SonarGG

# Restore dependencies and build
dotnet build -c Release
```

The compiled binary will be located at:
`bin/Release/net8.0-windows/SonarGG.exe`

---

## 🛠️ Tech Stack & Architecture

- **Language / Runtime**: C# 12 / .NET 8 WPF (Windows Presentation Foundation)
- **Audio Routing**: Windows Core Audio API (WASAPI) via `NAudio`
- **Low-Latency Processing**: Double-buffered floating-point DSP pipeline
- **Configuration**: Local SQLite database (`Microsoft.Data.Sqlite`)

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
