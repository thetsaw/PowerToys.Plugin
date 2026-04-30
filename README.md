# DiskAnalyzer — PowerToys Run Plugin

A **TreeSize-like disk space analyzer** for PowerToys Run. Quickly scan folders, find the largest files, view drive usage, and navigate your file system — all from the quick launcher.

![License](https://img.shields.io/github/license/thetsaw/PowerToys.Plugin)
![GitHub release](https://img.shields.io/github/v/release/thetsaw/PowerToys.Plugin)
![Downloads](https://img.shields.io/github/downloads/thetsaw/PowerToys.Plugin/total)

## Features

| Command | Description |
|---------|-------------|
| `ds drives` | Show all drives with used/free/total space and visual progress bars |
| `ds C:\Users` | Scan a folder — lists subfolders and files sorted by size |
| `ds largest C:\` | Find the largest files recursively under a path |
| `ds top C:\` | Rank top-level subfolders by total size |
| `ds C:\Use...` | Auto-complete partial paths |

### Context Menu Actions (right-click a result)

- **Open in File Explorer** — opens the folder or selects the file
- **Copy path** (`Ctrl+C`)
- **Copy size** (`Ctrl+Shift+C`)
- **Scan this folder** (`Ctrl+Enter`) — drill deeper into a subfolder
- **Find largest files here** (`Ctrl+L`) — launch a largest-files search

### Settings (PowerToys → PowerToys Run → DiskAnalyzer)

| Setting | Default | Description |
|---------|---------|-------------|
| Maximum results | 15 | How many items to show (5–50) |
| Default scan depth | 1 | How many directory levels deep to scan (1–5) |
| Include hidden files | Off | Include hidden files/folders in results |
| Show percentage | On | Display each item's percentage of the parent folder |

## Installation

### Prerequisites

- [PowerToys](https://github.com/microsoft/PowerToys) v0.88.0 or later
- Windows 10/11

### Quick Install (from Release)

1. **Close PowerToys** completely (exit from system tray).
2. Download the latest release ZIP for your architecture from the [Releases page](https://github.com/thetsaw/PowerToys.Plugin/releases).
3. Extract the `DiskAnalyzer` folder to:
   ```
   %LocalAppData%\Microsoft\PowerToys\PowerToys Run\Plugins\
   ```
4. **Restart PowerToys**.
5. Open PowerToys Run (`Alt+Space`) and type `ds` to get started.

## Building from Source

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (with Windows desktop workload)
- Visual Studio 2022 (recommended) or `dotnet` CLI

### Clone and Build

```powershell
git clone https://github.com/thetsaw/PowerToys.Plugin.git
cd PowerToys.Plugin
```

### Build with Visual Studio

1. Open `Community.PowerToys.Run.Plugin.DiskAnalyzer.sln`
2. Select **Release | x64** (or ARM64 for ARM devices)
3. Build → Build Solution
4. Output is in `bin\x64\Release\net9.0-windows\`

### Build with dotnet CLI

```powershell
# For x64
dotnet build -c Release -p:Platform=x64

# For ARM64
dotnet build -c Release -p:Platform=ARM64
```

### Deploy after building

```powershell
# Close PowerToys first, then:
$src = "bin\x64\Release\net9.0-windows"
$dest = "$env:LOCALAPPDATA\Microsoft\PowerToys\PowerToys Run\Plugins\DiskAnalyzer"

# Remove old version if upgrading
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }

# Copy plugin files
Copy-Item $src $dest -Recurse

# Restart PowerToys from your Start Menu or taskbar
```

## Project Structure

```
PowerToys.Plugin/
├── Community.PowerToys.Run.Plugin.DiskAnalyzer/
│   ├── Images/
│   │   ├── diskanalyzer.dark.png      # Icon for dark theme
│   │   └── diskanalyzer.light.png     # Icon for light theme
│   ├── Main.cs                        # Plugin entry point (IPlugin, IContextMenu, ISettingProvider)
│   ├── DiskAnalyzerHelper.cs          # Disk scanning engine, size formatting, progress bars
│   ├── DiskItemInfo.cs                # Data model for files/folders
│   ├── plugin.json                    # Plugin metadata
│   └── *.csproj                       # Project file (targets net9.0-windows)
├── Community.PowerToys.Run.Plugin.DiskAnalyzer.sln
├── README.md
└── LICENSE
```

## How It Works

- **`ds drives`** uses `System.IO.DriveInfo` for instant drive stats.
- **Folder scans** use `Parallel.For` with `DirectoryInfo.EnumerateFiles` for fast, concurrent I/O.
- **Largest files** uses a bounded `SortedSet` to efficiently track the top-N files during a recursive walk.
- Access-denied errors are caught gracefully — inaccessible folders are skipped silently.

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests at [github.com/thetsaw/PowerToys.Plugin](https://github.com/thetsaw/PowerToys.Plugin).

## License

MIT License — see [LICENSE](LICENSE) for details.

## Credits

Built by [Thet](https://github.com/thetsaw) with Copilot.
