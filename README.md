# muten

Control per-application audio volume and mute status from the Windows command line.

muten wraps the Windows Core Audio API (via [NAudio](https://github.com/naudio/NAudio)) so you can manage app volumes without opening the Volume Mixer.

## Project Structure

```
muten/
├── muten.sln
└── src/
    ├── muten.Core/               # Core library (shared by CLI and Tray)
    │   ├── AudioSession.cs       # Read-only model: PID, Name, Volume, IsMuted
    │   └── AudioSessionManager.cs# Mute, unmute, volume control via Core Audio API
    │
    ├── muten.Cli/                # Command-line interface
    │   └── Program.cs            # Entry point & command routing
    │
    └── muten.Tray/               # System tray application
        ├── Program.cs            # Entry point (WinForms message loop)
        └── TrayApplicationContext.cs # NotifyIcon, context menu, toggle mute
```

`muten.Core` holds all the audio logic and is shared by both the CLI and the Tray app.

## Requirements

- Windows 10+
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build & Run

### CLI (one-shot commands)

```bash
dotnet run --project src/muten.Cli -- <command>
```

### System Tray

```bash
dotnet run --project src/muten.Tray
```

This starts a persistent icon in the notification area (next to the clock). Right-click the icon to see active audio sessions — click any session to toggle its mute state. Choose **Quit** to exit.

## Commands

| Command | Description |
|---|---|
| `muten list` | List all active audio sessions |
| `muten mute <name\|pid>` | Mute an application |
| `muten unmute <name\|pid>` | Unmute an application |
| `muten toggle <name\|pid>` | Toggle mute state |
| `muten volume <name\|pid> <0-100>` | Set volume percentage |

You can target apps by **process name** (case-insensitive) or **PID**.

## Examples

```bash
# See what's playing
muten list

# Mute Spotify
muten mute spotify

# Set Discord to 50% volume
muten volume discord 50

# Toggle mute on Chrome
muten toggle chrome

# Unmute by PID
muten unmute 12345
```

## Tech Stack

- **C# 12 / .NET 8**
- **NAudio 2.2.1** — Windows Core Audio API wrapper
