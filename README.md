# muten

Per-application audio control for Windows. Automatically mute apps when they lose focus and unmute them when you switch back.

muten wraps the Windows Core Audio API (via [NAudio](https://github.com/naudio/NAudio)) and lives in your system tray.

## Features

- **Auto-mute** — managed apps only play sound when their window is active
- **Start with Windows** — optional, toggle from the tray menu
- Settings saved to `%APPDATA%\muten\settings.json`

## Install

Download `muten.zip` from the latest release, extract anywhere, and run `muten.exe`.

## Auto-Mute

1. Right-click the tray icon
2. Click an app to **manage** it (a checkmark appears)
3. Managed apps are only audible when their window is in the foreground
4. Alt-tab away → the managed app mutes. Alt-tab back → it unmutes at its original volume

Use **Pause auto-mute** / **Resume auto-mute** in the menu to temporarily disable the feature.

## CLI

A companion CLI is included for one-shot commands:

```bash
dotnet run --project src/muten.Cli -- <command>
```

| Command | Description |
|---|---|
| `muten list` | List all active audio sessions |
| `muten mute <name\|pid>` | Mute an application |
| `muten unmute <name\|pid>` | Unmute an application |
| `muten toggle <name\|pid>` | Toggle mute state |
| `muten volume <name\|pid> <0-100>` | Set volume percentage |

You can target apps by **process name** (case-insensitive) or **PID**.

## Building from Source

Requires Windows 10+ and [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
# Run during development
dotnet run --project src/muten.Tray

# Build release zip
build.bat
```

`build.bat` publishes a self-contained single-file exe and packages it into `muten.zip`.

## Project Structure

```
muten/
├── muten.sln
├── build.bat                      # Build + zip script
└── src/
    ├── muten.Core/               # Core library (shared by CLI and Tray)
    ├── muten.Cli/                # Command-line interface
    └── muten.Tray/               # System tray application
```

## Tech Stack

- **C# 12 / .NET 8**
- **NAudio 2.2.1** — Windows Core Audio API wrapper
