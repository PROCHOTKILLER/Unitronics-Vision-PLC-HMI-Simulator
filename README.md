# Unitronics Vision PLC/HMI Simulator

A lightweight, portable standalone Soft-PLC and HMI simulator for the entire **Unitronics Vision & Samba Series** (including **V130, V350, V430, V570, V700, V1040, V1210**), featuring real-time **PCOM protocol** emulation over TCP/IP, interactive HMI touchscreen simulation, hardware bezel keypad, and digital/analog I/O diagnostics.

Built in C# and .NET Framework 4.0 (x86 architecture compatible with Unitronics VisiLogic driver requirements).

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows%20x86%20%7C%20x64-lightgrey.svg)
![Version](https://img.shields.io/badge/version-v0.0.16-green.svg)
![Target](https://img.shields.io/badge/target-Unitronics%20Vision%20Series-brightgreen.svg)

---

## 🎯 Vision Series Architecture & Compatibility

This simulator is designed to emulate the complete runtime environment for all programmable logic controllers in the Unitronics Vision family:

| Family | Model | Display & Touchscreen | Emulation Status |
| :--- | :--- | :--- | :--- |
| **Vision 130** | V130-33-xxx | 2.4" Monochrome Graphic STN, 128x64 px, 20-key keypad | Supported (Keypad & IO) |
| **Vision 350** | V350-35-xxx / V350-J | 3.5" Color TFT QVGA, 320x240 px, resistive touchscreen | **Fully Supported (Default)** |
| **Vision 430** | V430-J-xxx | 4.3" Color TFT WQVGA, 480x272 px, resistive touchscreen | Supported |
| **Vision 570** | V570-57-xxx | 5.7" Color TFT QVGA, 320x240 px, resistive touchscreen | Supported (Snap-in slots) |
| **Vision 700** | V700-T20BJ | 7.0" Color TFT WVGA, 800x480 px, resistive touchscreen | Supported |
| **Vision 1040** | V1040-20-xxx | 10.4" Color TFT SVGA, 800x600 px, 9 programmable function keys | Supported (800x600 TFT) |
| **Vision 1210** | V1210-T20BJ | 12.1" Color TFT XGA, 800x600 px, resistive touchscreen | Supported |

---

## 🚀 Key Features

- **100% Universal Project Emulation & Neutral Post-Download State**:
  - After loading or downloading a project from VisiLogic, all digital inputs (`I`), digital outputs (`O`), and analog inputs (`AI`) start in a **100% neutral unforced state** (`0`).
  - Zero project-specific hardcoding — executes dynamic ladder nets, HMI screens, and operand descriptions parsed directly from any `.vlp`/`.vlx`/`.udb` or PCOM download.
- **Interactive I/O & Process Simulation Wizard (`F8`)**:
  - Automatically prompts after project download/load (and accessible anytime via `Simulation -> Configure I/O & Process Simulation... (F8)`) to configure:
    - Whether the project should run in pure neutral hardware mode or with active I/O & process simulation.
    - Digital inputs (`I0..I31`): functional role/description, artificial state (`Neutral 0`, `Forced ON 1`, `Forced OFF 0`), and dynamic behavior (`Manual`, `NC Safety Healthy`, `NO Contact`, `Follow Output O(x)` feedback, `Inverted Output !O(x)`, `Periodic Pulse Generator`).
    - Digital outputs (`O0..O31`): functional role/description and process actuator type (`None`, `Heating Actuator +dT`, `Cooling Actuator -dT`, `Humidifying +dRH`, `Dehumidifying -dRH`, `Pump/Fan`).
    - Analog inputs (`AI0..AI7`): sensor type (`PT100/NI1000 Temp`, `0-10V`, `4-20mA`), linked `MI` register, neutral raw value, and closed-loop thermal/process response to heating/cooling outputs.
- **Startup Hardware Configuration Wizard**:
  - Modal startup dialog allows interactive selection of PLC family and model, Snap-In modules (for modular series V570/V1040/V1210), and up to 8 EX-A2X expansion slots.
  - Strict hardware enforcement: screen resolution, touchscreen capability, and PCOM model identification.
  - Quick presets for industrial configurations (e.g. V350-RA22 + IO-DI8-RO8 Relay expansion) and factory default standalone models.
- **Bilingual Interface (English & Polish)**:
  - Full runtime bilingual support across all menus, dialogs, and I/O status labels with instant on-the-fly language switching.
- **Configurable Logging Engine**:
  - Main Settings dialog with toggle, 5 verbosity levels (Error, Warn, Info, Debug, Trace), PCOM packet / Touch event filters, and log viewer/clearing tools.
  - Persistent state saved to `virtualplc_config.json`.
- **PCOM Protocol Server (TCP/IP)**:
  - Full bidirectional support for **PCOM ASCII** (frames starting with `_` or `/`) and **PCOM Binary** (primitives `0x65`, `0x47`, `0x42`, `0x44`, `0x50`, `0x52`).
  - Implements System Integer inspection: responds to `SI 307` model identifier query (returning `/A00GS0000FB\r`), operand read/write, RTC, and Project Download frames without `5Automation error` in `UnVsCon2.dll`.
  - Operates on standard TCP port `20256` (or configurable).
- **Soft-PLC Real-Time Engine & Persistent Memory (`rom.bin` / `ram.bin`)**:
  - Deterministic cyclic scan loop (default 20 ms) with binary Flash ROM (`rom.bin`) and live RAM (`ram.bin`) persistence.
  - Complete Unitronics operand memory map:
    - **MB** (Memory Bits): 0 – 8191
    - **MI** (Memory Integers): 0 – 4095
    - **ML** (Memory Longs): 0 – 511
    - **MD / DW** (Double Words): 0 – 255
    - **MF** (Floats): 0 – 63
    - **XB / XI / XD / XL**: Extended operand banks
    - **Inputs (I)**: 0 – 543 & **Outputs (O)**: 0 – 543
    - **Timers (T)**: 0 – 383 & **Counters (C)**: 0 – 31
- **Interactive HMI & Bezel Controls**:
  - High-performance HMI display rendering area with touch coordinate dispatcher.
  - Full Polish diacritics support (UTF-8 / CP1250 font encoding).
  - Front bezel hardware keypad: `ESC`, `ENTER`, numeric `0`–`9`, navigation arrows (`▲`, `▼`, `◄`, `►`), functional keys `F1`–`F8`.
  - Emulated digital and analog I/O status LED panel with live tooltips and click-to-toggle support.
- **Portable Single-Binary Deployment**:
  - Entire simulator bundled in a single standalone executable: `virtualplc.exe`.
  - Zero installers or third-party runtime dependencies required.
  - Binaries are distributed directly via **[GitHub Releases](https://github.com/PROCHOTKILLER/Unitronics-Vision-PLC-HMI-Simulator/releases)**.
  - Automated 5-version retention archiving script (`rotate_versions.ps1`) storing backups in `archive/`.
  - Diagnostic logging engine writing to `virtualplc_debug.log`.

---

## 📂 Repository Structure

```text
Unitronics-Vision-PLC-HMI-Simulator/
│
├── SIMULATOR_MANUAL.md          # Technical manual & operation guide
├── README.md                    # Project documentation
│
├── src/                         # Source code & build workspace
│   ├── build.bat                # 1-click build script using csc.exe
│   ├── rotate_versions.ps1      # Version rotator (max 5 historical versions)
│   ├── Program.cs               # Application entry point
│   ├── MainForm.cs              # Main window & layout
│   ├── HardwareConfigDialog.cs  # Startup hardware configuration wizard
│   ├── SimulationProfile.cs     # Universal I/O & process simulation rules
│   ├── SimulationSetupDialog.cs # Interactive post-download simulation wizard
│   ├── SettingsDialog.cs        # Simulator settings dialog
│   ├── HardwareProfile.cs       # Vision controller & EX-A2X hardware profiles
│   ├── Localization.cs          # Bilingual UI strings (English & Polish)
│   ├── SimulatorConfig.cs       # Configuration persistence (JSON)
│   ├── PlcEngine.cs             # Cyclic scan engine, ROM/RAM & dynamic ladder
│   ├── PcomServer.cs            # PCOM ASCII & Binary protocol server
│   ├── HmiRenderer.cs           # HMI graphic rendering & touch handling
│   ├── HardwarePanel.cs         # Bezel keys & I/O panel
│   ├── VlpParser.cs             # VisiLogic project file parser
│   └── AppLogger.cs             # Diagnostics file logger
│
└── archive/                     # Historical version archive (max 5 kept)
```

> 💡 **Pre-built Executable:** Download the ready-to-run `virtualplc.exe` from the [Releases](https://github.com/PROCHOTKILLER/Unitronics-Vision-PLC-HMI-Simulator/releases) tab.

---

## 🛠️ Building from Source

The project compiles with Microsoft Visual C# Compiler (`csc.exe`) bundled with .NET Framework 4.0+. No heavy Visual Studio installation is required!

1. Open PowerShell or Command Prompt.
2. Navigate to `src/`:
   ```cmd
   cd src
   build.bat
   ```
3. The script compiles the code using `/platform:x86 /target:winexe /optimize+` and outputs the updated `virtualplc.exe` into the workspace while archiving the previous release into `archive/`.

---

## 🔌 Connecting with VisiLogic

1. Start `virtualplc.exe`.
2. Open your PLC project in Unitronics **VisiLogic**.
3. In VisiLogic, navigate to **Connection** > **Communication & OS**:
   - Connection Type: **TCP/IP (Call)**
   - IP Address: `127.0.0.1` (or your local IP)
   - Port: `20256`
   - PLC Name: Leave blank or set to `Unitronics`
4. Click **Get PLC Information** or **Check Communication**.
5. You can now perform **Download**, **Online Monitor**, and debug operands directly against the simulator!

---

## 📜 Version History

- **v0.0.5**:
  - Fixed VisiLogic TCP/IP Download & Online connection compatibility across all PCOM stages:
    - Fixed PCOM ASCII `GS` (Get System Status) command to return the authentic 1-byte status format (`/A00GS02A`), resolving `ComDrv1.clsAsciiTransmit.prvParseRecievedBlock` Error `13: Type mismatch`.
    - Fixed PCOM Binary `0x0C` (Read PLC Name) and ASCII `NMR` handshakes to bypass Error `294: TCP/IP connection cannot be established due to an incorrect PLC Name` regardless of the target PLC Name configured in VisiLogic.
    - Added full 100-byte binary structure in PCOM Binary `0x0C` (ReadVersion / GetPlcInfo) and valid flash bank metadata in `0x4D` (DLP Flow Metadata) and `0x01`/`0x81` (Read/Write PLC Memory).
    - Corrected `SI 307` hardware model identification codes to match VisiLogic `HW.dat` (`35` for V350, `13` for V130, `43` for V430, `57` for V570, `104` for V1040, `121` for V1210).
- **v0.0.4**:
  - Added interactive Startup Hardware Configuration Wizard supporting Vision series models (V130, V350, V430, V570, V1040, V1210), Snap-In modules, and up to 8 EX-A2X expansion slots.
  - Added bilingual runtime UI (English default & Polish) with on-the-fly language switching.
  - Added Simulator Settings dialog with configurable diagnostic logging verbosity levels and persistent `virtualplc_config.json` storage.
- **v0.0.3**:
  - Implemented advanced PCOM protocol handling for VisiLogic Project Download and query frames (`SI 307` model code, PCOM Binary commands `0x65`, `0x47`, `0x42`, `0x44`).
  - Added debug file logging to `virtualplc_debug.log`.
  - Added automated 5-version retention archiving script (`rotate_versions.ps1`).
- **v0.0.2**:
  - Added touchscreen touch coordinate dispatcher and HMI state synchronization.
  - Added Polish diacritics encoding fix for HMI strings.
  - Streamlined front bezel controls and eliminated superfluous UI buttons.
- **v0.0.1**:
  - Initial portable standalone executable release.

---

## 📄 License

Distributed under the MIT License.
