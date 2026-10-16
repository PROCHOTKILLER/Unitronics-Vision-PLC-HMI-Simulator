# Unitronics Vision V350 - Portable Standalone PLC Simulator

A lightweight, portable Soft-PLC simulator for the **Unitronics Vision V350** series (`V350-35-RA22` / `V350-J-RA22`), featuring real-time PCOM protocol emulation over TCP/IP, HMI touchscreen simulation, hardware keypad, and I/O status panel.

Built with C# and .NET Framework 4.0 (x86 architecture compatible with Unitronics VisiLogic driver requirements).

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows%20x86%20%7C%20x64-lightgrey.svg)
![Version](https://img.shields.io/badge/version-v0.0.3-green.svg)

---

## 🚀 Features

- **PCOM Protocol Server**:
  - Full support for **PCOM ASCII** (frames starting with `_` or `/`) and **PCOM Binary** (primitives `0x65`, `0x47`, `0x42`, `0x44`, `0x50`, `0x52`).
  - Supports VisiLogic PC-PLC communication over TCP port **20256** (configurable).
  - Handles System Integers query (including `SI 307` model ID returning `/A00GS0000FB\r`), operand read/write, RTC, and Project Download commands without `5Automation error`.
- **Soft-PLC Engine**:
  - Real-time cyclic scan loop (default 20 ms cycle).
  - Full memory map for Unitronics operands:
    - **MB** (Memory Bits): 0 – 4095
    - **MI** (Memory Integers): 0 – 2047
    - **ML** (Memory Longs): 0 – 511
    - **MD** (Double Words): 0 – 255
    - **MF** (Floats): 0 – 255
    - **DW** (Double Words): 0 – 255
    - **XB / XI / XD / XL**: Expanded operands
    - **Inputs (I)**: 0 – 31 & **Outputs (O)**: 0 – 31
    - **Timers (T)**: 0 – 383
- **Integrated Touchscreen & HMI**:
  - Color 3.5" QVGA (320x240) HMI display area.
  - Interactive touch handling (dynamic touch zones, buttons, numeric displays).
  - Polish character rendering (UTF-8 / CP1250 support).
- **Physical Keypad & I/O Emulation**:
  - Emulated hardware keys: `ESC`, `ENTER`, keypad `0`–`9`, Navigation arrows (`▲`, `▼`, `◄`, `►`), functional keys `F1`–`F8`.
  - Emulated digital/analog I/O LED indicators.
- **Portability & Version Retention**:
  - Single executable (`virtualplc.exe`), no external DLL dependencies or installer required.
  - Automatic backup system storing the last 5 previous versions in `stare wersje/`.
  - Debug logging to `virtualplc_debug.log`.

---

## 📂 Project Structure

```text
unitronics-v350-simulator/
│
├── virtualplc.exe               # Latest standalone executable (v0.0.3)
├── INSTRUKCJA_SYMULATORA.md     # Detailed Polish manual & setup guide
├── README.md                    # Project documentation
│
├── roboczy/                     # Source code & build workspace
│   ├── build.bat                # 1-click build script using csc.exe
│   ├── rotate_versions.ps1      # Version rotator (max 5 historical versions)
│   └── virtualplc_src/          # C# .NET 4.0 source code
│       ├── VirtualPLC.cs        # Entry point & application lifecycle
│       ├── MainForm.cs          # GUI form layout & controls
│       ├── PlcCore.cs           # PLC scan cycle engine & task runner
│       ├── PlcMemory.cs         # Memory bank data structures & operand maps
│       ├── PcomProtocol.cs      # PCOM ASCII & Binary protocol parser & server
│       ├── HmiScreen.cs         # HMI 320x240 rendering & touch dispatcher
│       ├── HardwareKeypad.cs    # Front bezel keypad controls
│       └── IoLedPanel.cs        # Digital / Analog I/O monitoring
│
└── stare wersje/                # Historical version archive (max 5 kept)
    └── v0.0.2/
```

---

## 🛠️ Building from Source

The project compiles with Microsoft Visual C# Compiler (`csc.exe`) bundled with .NET Framework 4.0+. No heavy Visual Studio installation is required!

1. Open PowerShell or Command Prompt.
2. Navigate to `roboczy/`:
   ```cmd
   cd roboczy
   build.bat
   ```
3. The script compiles the code using `/platform:x86 /target:winexe /optimize+` and outputs the updated `virtualplc.exe` into the root directory while automatically archiving the previous release into `stare wersje/`.

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
