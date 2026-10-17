# Technical Reference & User Manual: Standalone Unitronics Vision PLC/HMI Simulator v0.0.4 [virtualplc.exe]

## 1. Introduction and Architecture (Version 0.0.4)

The **`virtualplc.exe`** application (version **0.0.4**) is an autonomous, native hardware emulator for the **Unitronics Vision PLC/HMI** family (including V130, V350, V430, V570, V1040, and V1210), supporting on-board I/O, modular Snap-In units, and the industrial **EX-A2X** expansion I/O bus (up to 8 expansion modules).

The application is compiled as a 100% standalone single Windows executable (.NET Framework 4.0 x86), requiring no installation of third-party runtimes, database servers, web engines, or external COM wrappers.

### Core Operating Principle: Pure Hardware Emulation
- When launched, the controller contains **no hardcoded application logic**.
- In its initial state, the controller enters factory **OPLC Standby / STOP** mode:
  - All digital outputs (DO) are physically de-energized (low state / OFF).
  - Analog outputs (AO) produce 0.00 V.
  - The graphical display presents the factory Unitronics boot banner displaying active OPLC hardware specifications and the status message: `NO USER PROGRAM`.
  - The internal PLC ladder execution engine awaits project download from Unitronics VisiLogic or project file loading.
- Execution of control logic, timers, PID loops, or analog scaling occurs **only after loading a valid user application** (from a `.vlp`, `.vlx`, or `.udb` project file, or via PCOM TCP download).
- At any time, the user can reset the controller to factory clean state via **"Clear Program (Factory Standby)"**.

---

## 2. Hardware Configuration Wizard (Startup Panel)

Starting with version **v0.0.4**, launching the application presents a modal **Hardware Configuration Wizard** before starting emulation:

### 2.1. Configuration Capabilities:
1. **Vision Controller Family and Model Selection:**
   - **Vision 350:** V350-35-RA22 (default), V350-35-TRA22, V350-35-TA24, V350-35-R34, V350-35-T38.
   - **Vision 130:** V130-33-RA22, V130-33-TA24 (2.4" STN graphic display, physical keypad navigation).
   - **Vision 430:** V430-J-RA22, V430-J-TA24, V430-J-RH2 (4.3" WQVGA 480x272 widescreen color display).
   - **Vision 570:** V570-57-T20B (modular PLC with 5.7" QVGA 320x240 display and Snap-In I/O bay).
   - **Vision 1040:** V1040-20-T20B (modular PLC with 10.4" SVGA 800x600 display and 9 programmable function keys F1–F9).
   - **Vision 1210:** V1210-T20BJ (modular PLC with 12.1" SVGA 800x600 display, flat bezel).
2. **Snap-In Modules (for modular series V570, V1040, V1210):**
   - V200-18-E1B, V200-18-E2B, V200-18-E3XB, V200-18-E4XB, V200-18-E5B, V200-18-E6B, V200-18-E62B, V200-18-ECB.
3. **EX-A2X Expansion I/O Bus (Slots 1–8):**
   - Toggle to enable or disable the EX-A2X expansion bus adapter.
   - 8-slot modular grid supporting the complete catalog of Unitronics I/O modules:
     - `IO-DI8-RO8` (8 Digital Inputs, 8 Relay Outputs),
     - `IO-DI8-TO8` (8 Digital Inputs, 8 Transistor pnp Outputs),
     - `IO-DI16` (16 Digital Inputs 24 VDC),
     - `IO-TO16` (16 Transistor Outputs 24 VDC),
     - `IO-RO8`, `IO-RO16` (8 or 16 Relay Outputs),
     - `IO-AI4-AO2` (4 Analog Inputs 12-bit, 2 Analog Outputs),
     - `IO-PT400`, `IO-PT4K` (4 RTD Temperature Inputs PT100/PT1000),
     - `IO-AO6X` (6 Isolated 12-bit Analog Outputs),
     - `EX-D16A3-RO8`, `EX-D16A3-TO16` (16 DI, 3 AI, 8/16 DO combo).
4. **Hardware Specifications & I/O Summary:**
   - Displays live display resolution, touchscreen capabilities, operand memory boundaries (MB 4096, MI 2048, ML 512, DW 512, MF 64, T 256, C 32), and exact counts of configured DI, DO, AI, and AO channels with assigned addressing ranges.
5. **Hardware Presets:**
   - `Load Preset: V350-RA22 + IO-DI8-RO8 Relay` – Instant configuration for standard industrial controllers with relay expansion.
   - `Reset to Default (V350-RA22 Standalone)` – Factory default profile without expansion modules.
6. **Language Selection:**
   - On-the-fly language dropdown switch (English / Polish) located directly in the upper-right corner of the wizard.
7. **Startup Behavior Control:**
   - Checkbox: `Always show this configuration dialog on startup` (allows bypassing wizard on future runs to load the last saved profile immediately).
   - Reopen at any time from the main menu: `Hardware -> Hardware Configuration & Profile...`.

---

## 3. Bilingual User Interface (English & Polish)

The simulator features a native localization architecture:
- **English (EN)** – Standard international industrial automation terminology.
- **Polish (PL)** – Complete technical nomenclature, Polish I/O panel labeling, and diagnostic messages.
- **Switching Languages:**
  - In the Hardware Configuration Wizard (top-right dropdown).
  - In the simulator main menu: `Settings -> Language -> English / Polish`.
  - In the `Settings -> Simulator Settings...` dialog (General tab).
- Language switching executes **dynamically in memory without restarting the application**; all menus, toolbars, table headers, ladder trees, and UI captions update immediately.

---

## 4. Logging Engine & Diagnostic Options

Accessible via `Settings -> Simulator Settings...` (and quick submenu `Settings -> Logging`), the engine provides complete control over the `virtualplc_debug.log` file:

1. **Logging Toggle (`Logging Enabled`):**
   - Completely disables file writes for maximum throughput or enables logging for diagnostic sessions.
2. **Log Verbosity Levels (`Log Level`):**
   - `Error` – Records only critical errors, socket disconnections, and unhandled exceptions.
   - `Warn` – Records database reading warnings, Jet 4.0 fallback events, and non-fatal operational issues.
   - `Info` (default) – Standard logs for PLC start/stop events, project loads, PCOM TCP connections, and profile changes.
   - `Debug` – Detailed internal engine trace, rung evaluation passes, and VisiLogic download steps.
   - `Trace` – Byte-by-byte (HEX) dump of all incoming and outgoing PCOM packets and HMI touchscreen coordinates.
3. **Specialized Diagnostics Filters:**
   - `Log PCOM TCP communication packets` – Dedicated toggle for network traffic monitoring.
   - `Log HMI touchscreen coordinates and press events` – Tracks touch coordinates and button press/release actions.
4. **Log Utilities:**
   - `Open Log File` button – Immediately opens `virtualplc_debug.log` in the system default text editor.
   - `Clear Log File` button – Clears the log file contents instantly without closing the simulator.
5. **Configuration Persistence:**
   - All preferences (language, startup behavior, PCOM port, logging parameters, and active hardware profile) are persisted across sessions in `virtualplc_config.json`.

---

## 5. Project Download and Execution with VisiLogic

The simulator provides two direct methods for loading PLC ladder programs and HMI visual screens:

### Method 1: Loading Any VisiLogic Project File (.vlp / .vlx / .udb)
From the toolbar, click:
`[ 📁 Load Project (.vlp) ]` or select from menu: `File -> Load VisiLogic Project (*.vlp, *.vlx, *.udb)...`
Choose any project file from disk. The integrated Jet 4.0 parser extracts ladder rungs, operand symbol tables, and HMI visual pages.

### Method 2: Direct Online Download & Monitoring via VisiLogic (PCOM TCP Port 20256)
The simulator includes a native **Unitronics PCOM TCP** protocol server (supporting ASCII and Binary modes) listening on port `20256`:
1. Launch **Unitronics VisiLogic**.
2. Navigate to: `Connection -> Communication & OS`.
3. Select `TCP/IP (Call)` protocol.
4. Enter IP Address: `127.0.0.1` (localhost) and Port: `20256`.
5. Click `Get OPLC Information` – VisiLogic identifies the controller matching the active hardware profile (e.g. `V350-35-RA22`, `V570-57-T20B`, etc.) with OS version 4.04.
6. **Project Download:**
   - In VisiLogic, select `Connection -> Download -> Download` (or press `F9`).
   - VisiLogic compiles the ladder logic and sends an OPLC readiness query (`GS` for system integer `SI 307`).
   - The simulator responds with standard PCOM ASCII `/A...` and Binary confirmation frames.
   - Upon transfer completion, the simulator inspects `OPLC.ini`, locates the newly compiled project database, and dynamically reloads ladder rungs, operands, and HMI displays on the fly!

---

## 6. User Interface and Diagnostic Panels

The main window is organized into two primary work areas:

### 6.1. Left Panel: Physical Vision Controller Bezel
Replicates the physical front panel of the selected Vision controller:
- **CPU Diagnostic LEDs:**
  - `PWR` (Green) – Continuous illumination indicates active 24 VDC internal logic supply.
  - `RUN` (Green) – Steady illumination during cyclic program execution; dark in STOP mode.
  - `ERR` (Red) – Indicates halted state, fault condition, or unprogrammed CPU.
- **Color TFT Display Area:**
  - Automatically scales to the resolution of the emulated model (320x240, 480x272, or 800x600 px).
  - Renders graphical HMI screens designed in VisiLogic.
  - **Full Touchscreen Emulation:** Clicking interactive screen elements fires linked touch bits (`MB` / `SB`) and navigation triggers.
  - **Touch Keypad:** Clicking numeric input variables opens the virtual numeric keypad overlay.
- **Physical Bezel Keypad:**
  - Function keys `[F1]` through `[F5]` (or `[F1]` through `[F9]` on V1040) toggle system bits `SB 31` .. `SB 39`.
  - Navigation keys (`▲`, `▼`, `◄`, `►`, `ENTER`, `ESC`, `INFO`) manipulate linked system navigation registers.

---

### 6.2. Right Panel: Diagnostic Tabs & I/O Cabinet Simulation

#### Tab 1: Hardware I/O & EX-A2X Expansion Modules
Simulated industrial electrical cabinet dynamically rendering controls according to the configured hardware profile:
- **Digital Inputs (DI) Tab:**
  - Displays all digital input channels across CPU onboard inputs and EX-A2X expansion modules.
  - Each input features a toggle switch (latching), a momentary button (pulse), and a green LED indicator.
  - Input labels synchronize dynamically with operand symbols defined in the loaded VisiLogic project.
- **Digital Outputs (DO) Tab:**
  - Displays relay and transistor output states with amber LED indicators reflecting ladder coil energization.
- **Analog Inputs (AI) Tab:**
  - Sliders and numeric entry fields allowing real-time injection of analog process signals (0–10 V, 4–20 mA, PT100 RTD).
- **Analog Outputs (AO) Tab:**
  - Displays computed analog output voltages (0–10 V) and frequency references (0–50.0 Hz).

#### Tab 2: Operand Watch Table (Live Watch)
- Real-time inspection of all controller memory areas (`I`, `O`, `MB`, `MI`, `ML`, `DW`, `MF`, `T`, `C`, `SB`, `SI`).
- Type filtering and instant search by symbol name or operand address.
- **Force Value:** Double-clicking any row allows forcing bit states or modifying register integer values.

#### Tab 3: Ladder Logic Subroutines
- Hierarchical tree of all project subroutines (`LdrFunctions`).
- Visual rung inspection with contacts (NO/NC, positive/negative transitions), coils, math functions, and timers.

#### Tab 4: HMI Displays List
- Inventory of graphical displays in the project with direct preview selection (writing target screen index to `SI 9`).

#### Tab 5: Diagnostics & Event Log
- Real-time console recording system events, PCOM communication packets, and PLC state transitions.
- Quick buttons to open or clear the `virtualplc_debug.log` file.

---

## 7. Directory Layout, Versioning, and Distribution

Official pre-built binaries (`virtualplc.exe`) are distributed via **[GitHub Releases](https://github.com/PROCHOTKILLER/Unitronics-Vision-PLC-HMI-Simulator/releases)**.

Repository layout:
1. **`SIMULATOR_MANUAL.md`** – Complete technical manual and reference guide.
2. **`README.md`** – Overview documentation and quick start instructions.
3. **`Run_Simulator.bat`** – Convenience launcher script.
4. Directory **`src/`** – C# (.NET 4.0/4.8) source code and build automation scripts.
5. Directory **`archive/`** – Automatic rolling backup of past builds (retaining up to 5 historical versions).

### Source Directory (`src/`):
- `build.bat` – Build script invoking .NET `csc.exe` and triggering version rotation.
- `rotate_versions.ps1` – Archive rotation script maintaining the 5-version limit in `archive/`.
- `HardwareConfigDialog.cs` – Modal startup wizard for controller model, Snap-In, and EX-A2X configuration.
- `SettingsDialog.cs` – Main simulator settings dialog (language, logging, network).
- `HardwareProfile.cs` – Model catalog for Vision controllers, Snap-In units, and EX-A2X modules.
- `Localization.cs` – Bilingual localization engine (EN / PL) with dynamic runtime switching.
- `SimulatorConfig.cs` – Configuration data model with JSON serialization to `virtualplc_config.json`.
- `AppLogger.cs` – Thread-safe diagnostic logging engine with configurable verbosity levels.
- `Program.cs` – Application entry point and startup wizard orchestration.
- `MainForm.cs` – Main operator workstation and docking layout.
- `PlcEngine.cs` – Soft-PLC cyclic scan engine, memory map, and ladder evaluator.
- `VlpParser.cs` – Jet 4.0 database parser for `.vlp` and `.udb` project files.
- `HmiRenderer.cs` – High-performance graphic rendering engine for TFT displays.
- `HardwarePanel.cs` – Dynamic virtual I/O cabinet panel.
- `PcomServer.cs` – Unitronics PCOM TCP protocol server (:20256) supporting online download.

### Versioning and Archival Policy (`archive/` Directory):
- Versioning follows semantic numbers starting from **0.0.1** and increments with each functional change (current: **0.0.4**).
- Prior to compiling a new build, `build.bat` calls `rotate_versions.ps1` to archive the existing `virtualplc.exe` and manual to `archive/vX.Y.Z/`.
- The `archive/` directory retains **a maximum of 5 recent versions**; older versions are automatically pruned.
- Official user executables are published to the repository's **Releases** page on GitHub.

---

## 8. Version History

- **v0.0.4 (2026-10-17)**:
  - **Interactive Hardware Configuration Wizard:** Introduced modal startup dialog allowing explicit configuration of Vision controller family and model (V130, V350, V430, V570, V1040, V1210), Snap-In modules, and up to 8 EX-A2X expansion bus slots.
  - **Hardware Constraint Enforcement:** Simulator dynamically enforces screen resolution, touchscreen capability, I/O channel limits, and PCOM identification frames according to the active profile.
  - **Bilingual Interface (English & Polish):** Dynamic on-the-fly language switching across startup wizard, menus, and simulator settings.
  - **Comprehensive Diagnostic Logging:** Added configurable logging toggle, 5 verbosity levels (Error, Warn, Info, Debug, Trace), specialized PCOM / Touch filters, and log viewing/clearing tools.
  - **Persistent Configuration:** Saved user preferences to `virtualplc_config.json`.
  - Automatic archival of v0.0.3 to `archive/v0.0.3/`.

- **v0.0.3 (2026-10-16)**:
  - **VisiLogic Online Download Implementation:** Resolved `5Automation error` in `UnVsCon2.dll` by fully implementing the Unitronics PCOM protocol standard (PCOM ASCII and PCOM Binary).
  - Proper 6-byte PCOM TCP header encapsulation (Transaction ID, protocol type 101/102, Little-Endian length).
  - Valid PCOM ASCII response generation (`/A` prefix, modulo-256 byte checksum, `/00GS01330122` readiness query response for `SI 307`).
  - Implemented PCOM Binary primitives (`/_OPLC`, 24-byte header, 16-bit complement CRC, buffer write `0x41`, flash commit `0x1A`, index translation `0x16`, password authorization `0x02`, name query `0x0C`).
  - Automated project reload from system database `OPLC.ini` (`[3000] 3001=...`) upon download completion in VisiLogic.
  - Automatic archival of v0.0.2 to `archive/v0.0.2/`.

- **v0.0.2 (2026-10-16)**:
  - Implemented diagnostic file logging to `virtualplc_debug.log` in application execution directory.
  - Created thread-safe `AppLogger` supporting log levels (INFO, DEBUG, ERROR, PCOM, TOUCH) with automatic rotation at 15 MB.
  - Global unhandled exception catching (`ThreadException`, `UnhandledException`) with stack trace logging.
  - Added quick log viewer action in Diagnostics tab and Help menu.
  - Automatic archival of v0.0.1 to `archive/v0.0.1/`.

- **v0.0.1 (2026-10-16)**:
  - Established semantic versioning starting at 0.0.1.
  - Restructured project layout: separated `src/` build directory from root repository.
  - Introduced automated `archive/` version retention mechanism.
  - Full UTF-8 font rendering eliminating mojibake artifacts.
  - Sub-15 ms touchscreen coordinate response.
  - Complete operand database extraction for MB 0..255 and MI 0..127.
  - Front panel layout refined to physical function keys F1–F5.
