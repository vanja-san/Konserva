<div align="center">
  <h1>🥫 Konserva</h1>
  <p><strong>Minecraft Server Manager for Windows</strong></p>
  <p>
    Create, run, and manage local Minecraft servers with a clean GUI.<br>
    No command line, no manual config editing.
  </p>
  <p>
    <a href="../../releases"><img src="https://img.shields.io/badge/version-1.9.5-blue?style=flat-square" alt="Version"></a>
    <img src="https://img.shields.io/badge/Windows-10%2B-0078D6?style=flat-square&logo=windows" alt="Windows">
    <img src="https://img.shields.io/badge/license-MIT-green?style=flat-square" alt="License">
  </p>
  <p>
    <a href="../../releases"><b>📥 Download Latest</b></a>
    &nbsp;&nbsp;·&nbsp;&nbsp;
    <a href="README.ru.md">🇷🇺 Русская версия</a>
  </p>
  <br>
</div>

---

## 📸 Gallery

<table>
  <tr>
    <td><img src=".github/screenshots/Main.png" alt="Main Window" width="400"></td>
    <td><img src=".github/screenshots/Properties.png" alt="Server Properties" width="400"></td>
  </tr>
  <tr>
    <td align="center"><em>Server List</em></td>
    <td align="center"><em>Properties Editor</em></td>
  </tr>
  <tr>
    <td><img src=".github/screenshots/Console.png" alt="Console" width="400"></td>
    <td><img src=".github/screenshots/Mods.png" alt="Mods & Plugins" width="400"></td>
  </tr>
  <tr>
    <td align="center"><em>Live Console</em></td>
    <td align="center"><em>Mods & Plugins</em></td>
  </tr>
</table>

<br>

---

## ✨ Features

| | |
|---|---|
| ⚡ **One-click setup** | Pick a version, choose a mod loader, hit create — server ready in minutes |
| 🎮 **All loaders** | Vanilla · Fabric · Forge · NeoForge · Paper · Quilt · Purpur |
| 🖥️ **Live console** | Real-time output, send commands, restart with one click |
| 🧩 **Mods & plugins** | Browse installed mods, view details, delete unwanted ones |
| ☕ **Java manager** | Auto-detects installed Java · Auto-selects compatible version · Manual override |
| 💾 **RAM tuning** | Per-server min/max memory allocation |
| 🔄 **Auto-restart** | Automatically restart server after a crash |
| 🛠️ **Properties editor** | GUI for `server.properties` — no manual editing |
| 📦 **Portable** | All data stored alongside the executable — no installers, no registry |
| 🌐 **Dual language** | English & Russian interface with runtime switching |

<br>

---

## 🚀 Quick Start

1. **Download** the latest release from [Releases](../../releases)
2. **Extract** the archive and run `Konserva.exe`
3. **Click +** to create your first server

> Your server runs on port `25565` by default.

<br>

---

## ❓ Troubleshooting

| Problem | Solution |
|---|---|
| **Server won't start** | Check console logs. Verify Java is installed and compatible (Java 25+ for 26.1.x, Java 21 for 1.20.5+, Java 17 for 1.18–1.20.4). Ensure port `25565` is free. |
| **EULA not accepted** | Open `.\Servers\<name>\eula.txt`, change `eula=false` → `eula=true`, restart server. |
| **Out of memory** | Increase **Max RAM** in server settings. Close other heavy applications. |

<br>

---

## ⚖️ License

**MIT License** — see [LICENSE.txt](LICENSE.txt).

> [!NOTE]
> Konserva is an unofficial, community-built tool. It is not affiliated with, endorsed by, or connected to Mojang Studios or Microsoft. Use at your own risk. Always backup your worlds.

<br>

---

<p align="center">Built with 🧡 using <a href="https://github.com/lepoco/wpfui">WPF UI</a></p>
