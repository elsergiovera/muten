<p align="center">
  <img src="src/assets/img/logo.png" alt="muten logo" width="120" />
</p>

<h1 align="center">muten</h1>

<p align="center">
  Per-application audio control for Windows.<br/>
  Automatically mute apps when they lose focus and unmute them when you switch back.
</p>

## Features

- **Auto-mute** — **mutened** apps only play sound when their window is active
- **Start with Windows** — optional, toggle from the tray menu
- Settings saved to `%APPDATA%\muten\settings.json`

## Install

Download `muten.zip` from the [latest release](https://github.com/elsergiovera/muten/releases/latest), extract anywhere, and run `muten.exe`.

Visit [muten.veraserg.io](https://muten.veraserg.io) for more info.

## How it Works

<p align="center">
  <img src="src/assets/img/tray.png" alt="tray menu" />
</p>

1. Click the tray icon to see audio sessions
2. Click an app to **muten** it (highlighted)
3. **Mutened** apps are only audible when their window is in the foreground
4. Alt-tab away → the **mutened** app mutes. Alt-tab back → it unmutes

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

## Releasing

```bash
build.bat                          # produces muten.zip
git add muten.zip
git commit -m "v1.1.0"
git tag v1.1.0
git push && git push --tags        # triggers GitHub Actions
```

Pushing a `v*` tag creates a GitHub Release with `muten.zip` attached and deploys the site.

Pushing to `main` with changes in `src/www/` deploys the site only (no release).

## Project Structure

```
muten/
├── muten.sln
├── build.bat                      # Build + zip script
├── .github/workflows/release.yml  # CI: release + deploy site
└── src/
    ├── assets/                   # Shared icons and images
    ├── muten.Core/               # Core library (shared by CLI and Tray)
    ├── muten.Cli/                # Command-line interface
    ├── muten.Tray/               # System tray application
    └── www/                      # Landing page (Astro)
```

## Tech Stack

- **C# 12 / .NET 8**
- **NAudio 2.2.1** — Windows Core Audio API wrapper
- **Astro** — Landing page at [muten.veraserg.io](https://muten.veraserg.io)
