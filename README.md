# Konserva — Minecraft Server Manager

**Konserva** is a simple and user‑friendly graphical tool for creating, configuring, and running local Minecraft servers on Windows. The name is a blend of "**con**sole" and "**serv**er", reflecting the idea of a single interface that preserves your settings and world — everything is "canned" and ready to go.

The utility eliminates the need to manually edit configuration files or use the command line. All server management tasks are performed through a unified dashboard.

---

## 📋 Features

### Server Management
- ✅ Create and delete servers
- ✅ Start and stop servers with one click
- ✅ Automatic server installation (Vanilla, Fabric, Forge, NeoForge, Paper, Purpur)
- ✅ Support for multiple Minecraft versions simultaneously
- ✅ Auto-restart servers after crash

### Console and Monitoring
- ✅ Built-in server console with logs
- ✅ Send commands to server
- ✅ Real-time server status monitoring
- ✅ Resource usage statistics (RAM)

### Settings and Configuration
- ✅ GUI editor for server.properties
- ✅ Memory allocation settings (Min/Max RAM)
- ✅ Server port management
- ✅ Auto-start configuration

### Mods and Plugins Management
- ✅ View installed mods
- ✅ View installed plugins
- ✅ Delete mods and plugins via interface
- ✅ Open mods/plugins folders

### Java System
- ✅ Automatic detection of installed Java versions
- ✅ Support for multiple Java versions simultaneously
- ✅ Automatic selection of compatible Java for Minecraft version
- ✅ Manual Java path addition

---

## 🖥️ System Requirements

### Minimum
- **OS**: Windows 10 (version 1903 or later)
- **Processor**: 1.5 GHz
- **RAM**: 2 GB (for application)
- **Storage**: 200 MB (for application)
- **.NET**: Not required (built into application)

### Recommended
- **OS**: Windows 11
- **Processor**: 2.0 GHz or higher
- **RAM**: 4 GB or more
- **Storage**: 500 MB

### For Minecraft Servers
| Minecraft Version | Min RAM | Rec RAM | Java Required |
|-----------------|----------|----------|----------------|
| 1.20.5+ | 2 GB | 4 GB | Java 21 |
| 1.18–1.20.4 | 2 GB | 4 GB | Java 17 |
| 1.17 | 1 GB | 2 GB | Java 16 |
| 1.16 and below | 512 MB | 1 GB | Java 8 |

---

## 📦 Installation

### Quick Start
1. Download `Konserva.exe` from release
2. Run the file with double-click
3. Application is ready to use!

**Important**: No installation required. The application is self-contained.

### Data Location
All application data is stored in:
```
%AppData%\Konserva\
```

Folder structure:
```
%AppData%\Konserva\
├── config.json          # Application configuration
├── servers.json         # Server list
├── Servers\             # Server folders
│   ├── server-name-1\
│   └── server-name-2\
└── Logs\                # Application logs
    └── logs-DD.MM.YY-HH.MM.log
```

---

## 🚀 Getting Started

### Creating Your First Server

1. **Launch Konserva**
2. **Go to "Servers" tab** (opens by default)
3. **Click "+" button** (Create Server)
4. **Fill in parameters**:
   - **Name**: Server name (e.g., "My Server")
   - **Minecraft Version**: Select from list
   - **Mod Loader**: Vanilla, Fabric, Forge, NeoForge, Paper, Purpur
   - **Folder**: Choose location (default: `%AppData%\Konserva\Servers`)
5. **Click "Create"**
6. **Wait for installation** (30 sec to 2 min depending on version)
7. **Start server** with ▶ button

### First Server Launch

On first server launch:
1. Wait for message `Done (...)! For help, type "help"`
2. Server is ready for connections
3. Default port: **25565**

**Important**: When stopping server **during startup** (before `Done` message), force kill is applied. After full startup, graceful shutdown with `stop` command is used.

---

## ⚙️ Server Configuration

### Memory Allocation

1. Open server page (⚙️ button in server list)
2. Go to **"Settings"** tab
3. Change parameters:
   - **Min RAM**: Minimum memory (recommended: 1024 MB)
   - **Max RAM**: Maximum memory (recommended: 4096 MB)
4. Click **"Save"**

**RAM Recommendations**:
- Vanilla server: 2–4 GB
- With mods (up to 50): 4–6 GB
- With mods (50+): 6–8 GB

### Auto-Restart

1. Open server **"Settings"** tab
2. Enable **"Auto-Restart"**
3. Set delay in seconds (default: 10 sec)

---

## 🔧 Mods and Plugins Management

### Installing Mods (Fabric/Forge/NeoForge)

1. Open server page
2. Go to **"Mods"** tab
3. Click **"mods folder"**
4. Copy `.jar` mod files to folder
5. Refresh list (⟳ button)
6. Restart server

### Installing Plugins (Paper/Spigot/Purpur)

1. Open server page
2. Go to **"Plugins"** tab
3. Click **"plugins folder"**
4. Copy `.jar` plugin files to folder
5. Refresh list (⟳ button)
6. Restart server

### Deleting Mods/Plugins

1. Open **"Mods"** or **"Plugins"** tab
2. Find needed element in list
3. Click **🗑️** (Delete) button
4. Confirm deletion
5. Restart server

---

## 🛠️ Java Configuration

### Automatic Java Selection

Application automatically:
- Finds installed Java versions on system
- Selects compatible version for Minecraft
- Uses default Java

### Manual Java Addition

1. Go to **"Settings"** → **"Java Management"**
2. Click **"Add Java"**
3. Specify path to `java.exe` (e.g., `C:\Program Files\Java\jdk-17\bin\java.exe`)
4. Click **"Add"**
5. Select as default Java if needed

### Changing Java for Server

1. Open server page
2. Go to **"Settings"** tab
3. In **"Java"** section, select needed version
4. Click **"Save"**

---

## 📊 server.properties Editor

To edit server settings:

1. Open server page
2. Go to **"Properties"** tab
3. Change needed parameters in GUI
4. Click **"Save"**

**Available categories**:
- **Main**: Port, max players, game mode
- **World**: World type, difficulty, generation
- **Network**: Whitelist, online mode, protection
- **Performance**: View distance, simulation
- **Gameplay**: Mob spawning, animals, NPCs

---

## 🔍 Search and Filters

### Server Search

Enter server name in search field (partial match works).

### Filters

**By mod loader type**:
- All types
- Vanilla
- Fabric
- Forge
- NeoForge
- Paper
- Spigot
- Purpur

**By status**:
- All servers
- Running (🟢)
- Stopped (⚫)

---

## ❓ FAQ

### Server won't start

**Problem**: Error on server startup

**Solution**:
1. Check logs in application console
2. Ensure Java is installed and configured
3. Check Java and Minecraft version compatibility
4. Ensure port 25565 is not occupied by another server

### "EULA not accepted" error

**Problem**: Server requires EULA acceptance

**Solution**:
1. Open file `%AppData%\Konserva\Servers\<server_name>\eula.txt`
2. Change `eula=false` to `eula=true`
3. Save file
4. Start server again

### Server freezes on stop

**Problem**: Long server shutdown

**Solution**:
- If server is still loading (no `Done` message) — instant stop
- If server is loaded — graceful shutdown (up to 30 sec)
- On timeout — force kill is applied

### Out of memory

**Problem**: `OutOfMemoryError`

**Solution**:
1. Open server settings
2. Increase **Max RAM**
3. Ensure system has enough free memory
4. Close other resource-intensive applications

---

## 📞 Support

If problems occur:

1. Check application logs in `%AppData%\Konserva\Logs\`
2. Check server logs in server folder (`logs/`)
3. Ensure system requirements are met
4. Try restarting application

---

## 📄 License

This project is licensed under the **MIT License** - see the [LICENSE.txt](LICENSE.txt) file for details.

---

## ⚠️ Disclaimer

Konserva is an unofficial third-party tool and is not affiliated with or endorsed by Mojang Studios, Microsoft, or any Minecraft server developers. Use at your own risk. The application is provided "as is" without warranty of any kind.

**Important Notes**:
- Always backup your server data before using any management tool
- The developers are not responsible for data loss or server corruption
- Minecraft server files and EULA are property of Mojang Studios

---

## 🙏 Special Thanks

This project was created with the assistance of **Qwen Code** — an AI-powered coding assistant by Alibaba Cloud.

🔗 [Qwen Code](https://github.com/QwenLM/qwen-code)

---

**Application Version**: 1.2.0
**Update Date**: 2026-04-01
**Platform**: Windows x64
**Size**: ~66 MB (Full), ~3 MB (Portable)

📖 **Build Instructions**: See [BUILD.md](BUILD.md)

🇷🇺 **Русская версия**: См. [README.ru.md](README.ru.md)
