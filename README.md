# Unitronics Vision PLC-HMI Simulator v2

A desktop Soft-PLC and HMI simulator for **Unitronics Vision** controllers (default memory layout: **V350**). It loads VisiLogic projects, runs their ladder logic in a real scan loop, renders the HMI screens with touch and value entry, and exposes the controller memory over **PCOM TCP**.

Version 2 is a complete rewrite in **.NET 10 / WPF (MVVM)**. The previous .NET Framework 4.0 line (tags `v0.0.3`–`v0.0.17`) is preserved on the [`legacy/net40`](../../tree/legacy/net40) branch.

![Version](https://img.shields.io/badge/version-v0.2.0-green.svg)
![Platform](https://img.shields.io/badge/platform-Windows%20x64%20%7C%20x86-lightgrey.svg)
![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)

> **Status:** early development (`0.x`). The UI is in Polish. Behaviour is verified against VisiLogic projects, but it is not a certified replacement for a real controller — never use it to validate safety functions.

## Features
- **Project import:** `.vlp` (ZIP container) and `.udb` (Jet database) — hardware, subroutines, rungs and LAD elements, operand descriptions, HMI displays, display jumps and touch operands. A schema export (Markdown, structure only, no data values) helps with unsupported tables.
- **Ladder interpreter:** contacts, coils, SET/RESET, timers, counters, comparisons, MOV, arithmetic, CALL/RET and JUMP-HMI. The program is compiled once on load, and unsupported elements and runtime anomalies are reported without stopping the scan.
- **Scan cycle** on a dedicated thread (default 25 ms): plant simulation → input forces → system bits → HMI display status (SI 252 current, SI 251 previous) → ladder → output forces → HMI display jumps.
- **Live ladder view:** energised wires, contacts, coils and blocks with the current operand values, one subroutine at a time.
- **HMI panel** at 320×240, with touch (minimum 150 ms hold, so a short click is not lost between scans), numeric value entry with range validation and display navigation. It shows a banner in STOP, where navigation driven by the ladder cannot work.
- **Operand table:** built automatically from the project (described operands, ladder operands and HMI operands). It has filters, LAD/HMI usage counts, bit toggling, forcing and register writes.
- **Force table** for bit operands, which take precedence over the ladder and the plant model.
- **PCOM ASCII server** on TCP, `127.0.0.1:20256` by default. It reads and writes MB/MI/ML and handles RUN/STOP/RESET.
- **Project profile (JSON):** the memory layout, initial values, digital feedback with ON/OFF delays and first-order analog followers. Project-specific behaviour lives in the profile, never in the engine. See [`examples/profile-example.json`](examples/profile-example.json).

## Requirements
- Windows 10/11, **.NET 10 SDK**, Visual Studio 2026 (or the `dotnet` CLI).
- A database provider for reading VisiLogic projects:
  - **x64** (default) needs the *Microsoft Access Database Engine 2016 Redistributable (x64)*, which provides ACE OLEDB 16.0. If it is missing, the app offers an "Install ACE…" button. This only works when you place `accessdatabaseengine_X64.exe` (downloaded from Microsoft) in `src/VisionSimulator.App/redist/` before building. The installer's Authenticode signature is verified before it runs.
  - **x86** uses Microsoft Jet OLEDB 4.0, which ships with Windows.

## Build, test and publish
```powershell
dotnet build "Vision PLC-HMI Simulator Test.slnx"
dotnet test  "Vision PLC-HMI Simulator Test.slnx"

# Single-file, self-contained executables
dotnet publish src/VisionSimulator.App -c Release -r win-x64 --self-contained true -o publish\VisionSimulator-x64
dotnet publish src/VisionSimulator.App -c Release -r win-x86 --self-contained true -o publish\VisionSimulator-x86
```
In Visual Studio, set `VisionSimulator.App` as the startup project (F5 runs the x64 build).

## Configuration
`src/VisionSimulator.App/appsettings.json`, overridden by an optional `appsettings.Local.json` (git-ignored) and by environment variables (for example `Pcom__Port`):

| Key | Default | Meaning |
| :--- | :--- | :--- |
| `Pcom:Enabled` / `Pcom:BindAddress` / `Pcom:Port` | `true` / `127.0.0.1` / `20256` | PCOM server. It has no authentication, so bind to a LAN address only on purpose. |
| `VisiLogicDatabase:Provider` | auto | Empty selects ACE 16.0 on x64 and Jet 4.0 on x86. |
| `VisiLogicDatabase:Password` | VisiLogic default | Override the project database password only if needed. |
| `Simulation:ScanIntervalMs` | `25` | Scan interval in milliseconds. |
| `Simulation:ProfilePath` | — | Project profile JSON. A relative path is resolved from the executable directory, and the memory layout is applied at startup. |

## Repository layout
| Path | Content |
| :--- | :--- |
| `src/VisionSimulator.Core` | Memory, scan engine and forces, ladder interpreter, HMI model, PCOM server, profiles, VisiLogic project reader (AnyCPU) |
| `src/VisionSimulator.App` | WPF UI (CommunityToolkit.Mvvm, Generic Host + DI), win-x64 / win-x86 |
| `tests/VisionSimulator.Tests` | xUnit tests of the logic that does not need Jet |
| `examples/` | Sample project profile |

Package versions are managed centrally in `Directory.Packages.props`, and the product version in `Directory.Build.props`.

## Versioning
[SemVer](https://semver.org/). The v2 line starts at `0.2.0`, and every iteration increments PATCH (`0.2.1`, `0.2.2`, …). MINOR is raised only for selected releases, and `1.0.0` marks the first production-accepted release. Each release is tagged `v<version>`.
