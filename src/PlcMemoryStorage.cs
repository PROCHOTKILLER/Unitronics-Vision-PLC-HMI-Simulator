using System;
using System.IO;
using System.Text;

namespace UnitronicsV350Simulator
{
    /// <summary>
    /// Manages persistent binary memory images (rom.bin and ram.bin) in the simulator startup directory.
    /// 
    /// File layout:
    /// 1. rom.bin (4 MB = 0x00400000 bytes = 4,194,304 bytes):
    ///    - 0x00000000 .. 0x00000FFF (4 KB)   : ROM Header ("V350ROM\0", version, model, active project path & metadata)
    ///    - 0x00001000 .. 0x00003FFF (12 KB)  : Power-Up Values & Timer Presets table
    ///    - 0x00004000 .. 0x0000FFFF (48 KB)  : Data Tables Flash region (Opcode 0x05)
    ///    - 0x00100000 .. 0x0010EFFF (60 KB)  : Info Tables Flash region (Opcode 0x16 / 0x01)
    ///    - 0x0010F000 .. 0x0014FFFF (260 KB) : HMI Flash region (Opcode 0x1E)
    ///    - 0x00150000 .. 0x001CFFFF (512 KB) : Fonts Flash region (Opcode 0x10 resType=0x04)
    ///    - 0x00300000 .. 0x003FFFFF (1 MB)   : Main Application / HMI / Backup VLP Flash (Opcode 0x10 / 0x41)
    ///
    /// 2. ram.bin (256 KB = 0x00040000 bytes = 262,144 bytes):
    ///    - 0x00000000 .. 0x000000FF (256 B)  : RAM Header & CPU Status ("V350RAM\0", State, ScanCount, SI[9], ProjectName)
    ///    - 0x00000100 .. 0x0000017F (128 B)  : I[0..127]   Digital Inputs (1 byte per bit)
    ///    - 0x00000180 .. 0x000001FF (128 B)  : O[0..127]   Digital Outputs (1 byte per bit)
    ///    - 0x00000200 .. 0x000003FF (512 B)  : SB[0..511]  System Bits (1 byte per bit)
    ///    - 0x00000400 .. 0x000007FF (1024 B) : SI[0..511]  System Integers (int16 LE, 2B each)
    ///    - 0x00001000 .. 0x00001FFF (4096 B) : MB[0..4095] Memory Bits (1 byte per bit)
    ///    - 0x00002000 .. 0x00002FFF (4096 B) : MI[0..2047] Memory Integers (int16 LE, 2B each)
    ///    - 0x00003000 .. 0x000037FF (2048 B) : ML[0..511]  Memory Longs (int32 LE, 4B each)
    ///    - 0x00003800 .. 0x00003FFF (2048 B) : DW[0..511]  Double Words (uint32 LE, 4B each)
    ///    - 0x00004000 .. 0x000040FF (256 B)  : MF[0..63]   Memory Floats (float32 LE, 4B each)
    ///    - 0x00004100 .. 0x0000413F (64 B)   : AO_Volts[0..7] Analog Outputs (double LE, 8B each)
    ///    - 0x00005000 .. 0x00005FFF (4096 B) : T[0..255]   Timers (16B each: PresetMs, CurrentMs, Run, Done)
    ///    - 0x00006000 .. 0x000061FF (512 B)  : C[0..31]    Counters (16B each: Preset, Current, Done)
    ///    - 0x00010000 .. 0x0001FFFF (64 KB)  : PCOM Fonts RAM (physical 0x00010000 .. 0x0001FFFF)
    ///    - 0x00020000 .. 0x0003FFFF (128 KB) : PCOM Application / DLU RAM (physical 0x00C00000 .. 0x00C1FFFF)
    /// </summary>
    public class PlcMemoryStorage
    {
        public const int ROM_SIZE = 0x00400000; // 4 MB Flash ROM
        public const int RAM_SIZE = 0x00040000; // 256 KB System + Operand + PCOM RAM

        // RAM Offsets
        public const int RAM_OFS_HEADER   = 0x00000000;
        public const int RAM_OFS_I        = 0x00000100;
        public const int RAM_OFS_O        = 0x00000180;
        public const int RAM_OFS_SB       = 0x00000200;
        public const int RAM_OFS_SI       = 0x00000400;
        public const int RAM_OFS_MB       = 0x00001000;
        public const int RAM_OFS_MI       = 0x00002000;
        public const int RAM_OFS_ML       = 0x00003000;
        public const int RAM_OFS_DW       = 0x00003800;
        public const int RAM_OFS_MF       = 0x00004000;
        public const int RAM_OFS_AO       = 0x00004100;
        public const int RAM_OFS_T        = 0x00005000;
        public const int RAM_OFS_C        = 0x00006000;
        public const int RAM_OFS_PCOM_RAM = 0x00010000; // 64 KB mapped to 0x00010000..0x0001FFFF
        public const int RAM_OFS_DLU_RAM  = 0x00020000; // 128 KB mapped to 0x00C00000..0x00C1FFFF

        // ROM Offsets
        public const int ROM_OFS_HEADER      = 0x00000000;
        public const int ROM_OFS_POWERUP     = 0x00001000;
        public const int ROM_OFS_DATATABLES  = 0x00004000;
        public const int ROM_OFS_INFOTABLES  = 0x00100000;
        public const int ROM_OFS_HMI_FLASH   = 0x0010F000;
        public const int ROM_OFS_FONTS_FLASH = 0x00150000;
        public const int ROM_OFS_APP_FLASH   = 0x00300000;

        private readonly byte[] romBuffer = new byte[ROM_SIZE];
        private readonly byte[] ramBuffer = new byte[RAM_SIZE];
        private readonly object syncLock = new object();

        private string romFilePath;
        private string ramFilePath;
        private DateTime lastRamDiskFlushUtc = DateTime.MinValue;
        private DateTime lastOwnRamFileWriteUtc = DateTime.MinValue;
        private volatile bool romDirty = false;
        private volatile bool ramDirty = false;

        public string RomFilePath { get { return romFilePath; } }
        public string RamFilePath { get { return ramFilePath; } }

        /// <summary>
        /// Returns the last persisted project file path stored in the ROM header (at 0x00000080), if valid.
        /// </summary>
        public string GetPersistedProjectPath()
        {
            lock (syncLock)
            {
                try
                {
                    int len = 0;
                    while (len < 255 && romBuffer[0x80 + len] != 0) len++;
                    if (len > 0)
                    {
                        string p = Encoding.UTF8.GetString(romBuffer, 0x80, len).Trim();
                        if (!string.IsNullOrEmpty(p) && File.Exists(p))
                            return p;
                    }
                }
                catch { }
                return null;
            }
        }

        // Default initialized 46-byte Data Tables flash image (Opcode 0x05)
        public static readonly byte[] DefaultDataTablesFlashImage = new byte[]
        {
            0x00, 0x00, 0x00, 0x00, 0x2E, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x76, 0x68, 0x8A, 0xE3,
            0x00, 0x00, 0x00, 0x00,
            0x12, 0x00, 0x00, 0x00,
            0x00, 0x63, 0xE0, 0x62, 0x60, 0x60, 0x28, 0xCB,
            0xE8, 0x7A, 0xCC, 0xC0, 0x08, 0x00, 0x57, 0x02,
            0x0D, 0x0A
        };

        public PlcMemoryStorage()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            romFilePath = Path.Combine(baseDir, "rom.bin");
            ramFilePath = Path.Combine(baseDir, "ram.bin");
            InitializeMemoryFiles();
        }

        public void InitializeMemoryFiles()
        {
            lock (syncLock)
            {
                bool romLoaded = false;
                try
                {
                    if (File.Exists(romFilePath))
                    {
                        using (FileStream fs = new FileStream(romFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            if (fs.Length == ROM_SIZE)
                            {
                                int read = 0;
                                while (read < ROM_SIZE)
                                {
                                    int n = fs.Read(romBuffer, read, ROM_SIZE - read);
                                    if (n <= 0) break;
                                    read += n;
                                }
                                if (read == ROM_SIZE &&
                                    romBuffer[0] == (byte)'V' && romBuffer[1] == (byte)'3' &&
                                    romBuffer[2] == (byte)'5' && romBuffer[3] == (byte)'0' &&
                                    romBuffer[4] == (byte)'R' && romBuffer[5] == (byte)'O' &&
                                    romBuffer[6] == (byte)'M')
                                {
                                    romLoaded = true;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("PlcMemoryStorage: Could not load existing rom.bin: " + ex.Message);
                }

                if (!romLoaded)
                {
                    ResetRomDefaultsInternal();
                    FlushRomToDiskInternal();
                }
                else
                {
                    EnsureRomSystemHeadersInternal();
                }

                bool ramLoaded = false;
                try
                {
                    if (File.Exists(ramFilePath))
                    {
                        using (FileStream fs = new FileStream(ramFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            if (fs.Length == RAM_SIZE)
                            {
                                int read = 0;
                                while (read < RAM_SIZE)
                                {
                                    int n = fs.Read(ramBuffer, read, RAM_SIZE - read);
                                    if (n <= 0) break;
                                    read += n;
                                }
                                if (read == RAM_SIZE &&
                                    ramBuffer[0] == (byte)'V' && ramBuffer[1] == (byte)'3' &&
                                    ramBuffer[2] == (byte)'5' && ramBuffer[3] == (byte)'0' &&
                                    ramBuffer[4] == (byte)'R' && ramBuffer[5] == (byte)'A' &&
                                    ramBuffer[6] == (byte)'M')
                                {
                                    ramLoaded = true;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("PlcMemoryStorage: Could not load existing ram.bin: " + ex.Message);
                }

                if (!ramLoaded)
                {
                    ResetRamDefaultsInternal();
                    FlushRamToDiskInternal();
                }
                else
                {
                    try { lastOwnRamFileWriteUtc = File.GetLastWriteTimeUtc(ramFilePath); } catch { }
                }
            }
        }

        private void ResetRomDefaultsInternal()
        {
            Array.Clear(romBuffer, 0, romBuffer.Length);
            EnsureRomSystemHeadersInternal();
        }

        private void EnsureRomSystemHeadersInternal()
        {
            // Magic header "V350ROM\0" at 0x00000000
            byte[] magic = Encoding.ASCII.GetBytes("V350ROM\0");
            Array.Copy(magic, 0, romBuffer, 0, magic.Length);
            WriteUInt32LE(romBuffer, 0x08, 1); // Format version 1

            // Ensure Data Tables Flash header at 0x00004000 if uninitialized
            bool dtEmpty = true;
            for (int i = 0; i < DefaultDataTablesFlashImage.Length; i++)
            {
                if (romBuffer[ROM_OFS_DATATABLES + i] != 0) { dtEmpty = false; break; }
            }
            if (dtEmpty)
            {
                Array.Copy(DefaultDataTablesFlashImage, 0, romBuffer, ROM_OFS_DATATABLES, DefaultDataTablesFlashImage.Length);
            }

            // Ensure Info Tables header at 0x00100000 if uninitialized (required by UnVsCon2.dll 0x16F29F)
            if (romBuffer[ROM_OFS_INFOTABLES] == 0 && romBuffer[ROM_OFS_INFOTABLES + 5] == 0)
            {
                romBuffer[ROM_OFS_INFOTABLES + 0] = (byte)'C';
                romBuffer[ROM_OFS_INFOTABLES + 1] = (byte)'I';
                romBuffer[ROM_OFS_INFOTABLES + 2] = 6;
                romBuffer[ROM_OFS_INFOTABLES + 3] = 10;
                romBuffer[ROM_OFS_INFOTABLES + 4] = 14;
                romBuffer[ROM_OFS_INFOTABLES + 5] = 16;
                romBuffer[ROM_OFS_INFOTABLES + 14] = (byte)'*';
                romBuffer[ROM_OFS_INFOTABLES + 15] = (byte)'*';
            }
        }

        private void ResetRamDefaultsInternal()
        {
            Array.Clear(ramBuffer, 0, ramBuffer.Length);
            byte[] magic = Encoding.ASCII.GetBytes("V350RAM\0");
            Array.Copy(magic, 0, ramBuffer, 0, magic.Length);
            WriteUInt32LE(ramBuffer, 0x08, 1); // Format version 1
        }

        /// <summary>
        /// Writes a block of physical PLC memory (from PCOM Opcode 0x41) to either RAM or Flash ROM.
        /// </summary>
        public void WritePhysicalMemory(uint physAddr, byte[] src, int srcOffset, int count)
        {
            if (src == null || count <= 0 || srcOffset < 0 || srcOffset + count > src.Length) return;

            lock (syncLock)
            {
                // 1. PCOM Application / DLU RAM region: 0x00C00000 .. 0x00C1FFFF -> ram.bin [0x00020000 .. 0x0003FFFF]
                if (physAddr >= 0x00C00000 && physAddr < 0x00C20000)
                {
                    int dstOffset = RAM_OFS_DLU_RAM + (int)(physAddr - 0x00C00000);
                    int copyLen = Math.Min(count, RAM_SIZE - dstOffset);
                    if (copyLen > 0)
                    {
                        Array.Copy(src, srcOffset, ramBuffer, dstOffset, copyLen);
                        ramDirty = true;
                    }
                    return;
                }

                // 2. PCOM Fonts RAM region: 0x00010000 .. 0x0001FFFF -> ram.bin [0x00010000 .. 0x0001FFFF]
                if (physAddr >= 0x00010000 && physAddr < 0x00020000)
                {
                    int dstOffset = RAM_OFS_PCOM_RAM + (int)(physAddr - 0x00010000);
                    int copyLen = Math.Min(count, 0x00010000 - (int)(physAddr - 0x00010000));
                    if (copyLen > 0)
                    {
                        Array.Copy(src, srcOffset, ramBuffer, dstOffset, copyLen);
                        ramDirty = true;
                    }
                    return;
                }

                // 3. Physical Flash ROM region: 0x00000000 .. 0x003FFFFF -> rom.bin [1:1 offset]
                if (physAddr < (uint)ROM_SIZE)
                {
                    int dstOffset = (int)physAddr;
                    int copyLen = Math.Min(count, ROM_SIZE - dstOffset);
                    if (copyLen > 0)
                    {
                        Array.Copy(src, srcOffset, romBuffer, dstOffset, copyLen);
                        romDirty = true;
                    }
                }
            }
        }

        /// <summary>
        /// Reads a block of physical PLC memory (for PCOM Opcode 0x01) from either RAM or Flash ROM.
        /// </summary>
        public byte[] ReadPhysicalMemory(uint physAddr, int count)
        {
            if (count <= 0) return new byte[0];
            byte[] result = new byte[count];

            lock (syncLock)
            {
                // 1. PCOM Application / DLU RAM region: 0x00C00000 .. 0x00C1FFFF
                if (physAddr >= 0x00C00000 && physAddr < 0x00C20000)
                {
                    int srcOffset = RAM_OFS_DLU_RAM + (int)(physAddr - 0x00C00000);
                    int copyLen = Math.Min(count, RAM_SIZE - srcOffset);
                    if (copyLen > 0)
                    {
                        Array.Copy(ramBuffer, srcOffset, result, 0, copyLen);
                    }
                    return result;
                }

                // 2. PCOM Fonts RAM region: 0x00010000 .. 0x0001FFFF
                if (physAddr >= 0x00010000 && physAddr < 0x00020000)
                {
                    int srcOffset = RAM_OFS_PCOM_RAM + (int)(physAddr - 0x00010000);
                    int copyLen = Math.Min(count, 0x00010000 - (int)(physAddr - 0x00010000));
                    if (copyLen > 0)
                    {
                        Array.Copy(ramBuffer, srcOffset, result, 0, copyLen);
                    }
                    return result;
                }

                // 3. Physical Flash ROM region: 0x00000000 .. 0x003FFFFF
                if (physAddr < (uint)ROM_SIZE)
                {
                    EnsureRomSystemHeadersInternal();
                    int srcOffset = (int)physAddr;
                    int copyLen = Math.Min(count, ROM_SIZE - srcOffset);
                    if (copyLen > 0)
                    {
                        Array.Copy(romBuffer, srcOffset, result, 0, copyLen);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Reads Data Tables Flash region (Opcode 0x05) from rom.bin [0x00004000 + offset].
        /// </summary>
        public byte[] ReadDataTablesFlash(uint dtOffset, int count)
        {
            if (count <= 0) return new byte[0];
            byte[] result = new byte[count];
            lock (syncLock)
            {
                EnsureRomSystemHeadersInternal();
                long srcOffset = (long)ROM_OFS_DATATABLES + dtOffset;
                if (srcOffset >= ROM_OFS_DATATABLES && srcOffset < ROM_OFS_INFOTABLES)
                {
                    int copyLen = (int)Math.Min((long)count, ROM_OFS_INFOTABLES - srcOffset);
                    if (copyLen > 0)
                    {
                        Array.Copy(romBuffer, (int)srcOffset, result, 0, copyLen);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Updates the ROM Header and Power-Up tables when a project is loaded or downloaded, and flushes rom.bin to disk.
        /// </summary>
        public void SaveProjectToRom(LoadedProject proj, HardwareProfile profile)
        {
            lock (syncLock)
            {
                EnsureRomSystemHeadersInternal();

                // Clear metadata header area [0x0C .. 0x0FFF]
                Array.Clear(romBuffer, 0x0C, 0x1000 - 0x0C);

                if (proj != null)
                {
                    WriteInt32LE(romBuffer, 0x0C, proj.Subroutines != null ? proj.Subroutines.Count : 0);
                    WriteInt32LE(romBuffer, 0x10, proj.Pages != null ? proj.Pages.Count : 0);
                    WriteInt32LE(romBuffer, 0x14, proj.OperandDescriptions != null ? proj.OperandDescriptions.Count : 0);
                    WriteInt64LE(romBuffer, 0x18, DateTime.UtcNow.Ticks);

                    string model = (profile != null) ? profile.GetModelFullName() : (proj.PlcModel ?? "V350-35-RA22");
                    WriteFixedAscii(romBuffer, 0x20, 32, model);
                    WriteFixedAscii(romBuffer, 0x40, 64, proj.ProjectName ?? "");
                    WriteFixedAscii(romBuffer, 0x80, 256, proj.FilePath ?? "");

                    // Serialize Timer Presets at 0x00001000 (up to 256 timers * 4 bytes = 1024 bytes)
                    Array.Clear(romBuffer, ROM_OFS_POWERUP, 0x3000);
                    if (proj.TimerPresets != null)
                    {
                        foreach (var kvp in proj.TimerPresets)
                        {
                            if (kvp.Key >= 0 && kvp.Key < 256)
                            {
                                WriteInt32LE(romBuffer, ROM_OFS_POWERUP + (kvp.Key * 4), kvp.Value);
                            }
                        }
                    }

                    // Serialize PowerUpValues summary at 0x00001400 (count + entries)
                    if (proj.PowerUpValues != null)
                    {
                        int pos = ROM_OFS_POWERUP + 0x400;
                        int count = 0;
                        foreach (var kvp in proj.PowerUpValues)
                        {
                            if (pos + 16 > ROM_OFS_DATATABLES) break;
                            WriteFixedAscii(romBuffer, pos, 8, kvp.Key);
                            WriteInt32LE(romBuffer, pos + 8, kvp.Value);
                            pos += 12;
                            count++;
                        }
                        WriteInt32LE(romBuffer, ROM_OFS_POWERUP + 0x3FC, count);
                    }
                }

                FlushRomToDiskInternal();
            }
        }

        /// <summary>
        /// Synchronizes all live PLC operands from PlcEngine into ramBuffer and flushes to ram.bin (either forced or debounced).
        /// Also detects if ram.bin was modified by an external debugger/hex editor and applies the changes to PlcEngine.
        /// </summary>
        public void SyncRamFromEngine(PlcEngine plc, bool forceFlushToDisk = false)
        {
            if (plc == null) return;

            lock (syncLock)
            {
                if (!forceFlushToDisk && lastOwnRamFileWriteUtc != DateTime.MinValue)
                {
                    try
                    {
                        DateTime diskWriteUtc = File.GetLastWriteTimeUtc(ramFilePath);
                        if (diskWriteUtc > lastOwnRamFileWriteUtc.AddMilliseconds(50))
                        {
                            if (LoadOperandsFromRamFile(plc))
                            {
                                lastOwnRamFileWriteUtc = diskWriteUtc;
                                plc.Log("RAM Sync: External modification of ram.bin detected and applied to live PLC operands.");
                            }
                        }
                    }
                    catch { }
                }

                // 1. RAM Header [0x0000 .. 0x00FF]
                byte[] magic = Encoding.ASCII.GetBytes("V350RAM\0");
                Array.Copy(magic, 0, ramBuffer, 0, magic.Length);
                WriteUInt32LE(ramBuffer, 0x08, 1); // Version 1
                WriteInt32LE(ramBuffer, 0x0C, (int)plc.State);
                WriteInt64LE(ramBuffer, 0x10, plc.ScanCount);
                WriteInt64LE(ramBuffer, 0x18, DateTime.UtcNow.Ticks);
                byte[] scanMsBytes = BitConverter.GetBytes(plc.LastScanDurationMs);
                Array.Copy(scanMsBytes, 0, ramBuffer, 0x20, 8);
                WriteInt16LE(ramBuffer, 0x28, plc.SI[9]);
                WriteInt16LE(ramBuffer, 0x2A, plc.CurrentAccessLevel);

                string hwModel = (plc.Profile != null) ? plc.Profile.GetModelFullName() : "V350-35-RA22";
                WriteFixedAscii(ramBuffer, 0x2C, 20, hwModel);
                WriteFixedAscii(ramBuffer, 0x40, 64, plc.Project != null ? (plc.Project.ProjectName ?? "") : "");
                WriteFixedAscii(ramBuffer, 0x80, 128, plc.Project != null ? (plc.Project.FilePath ?? "") : "");

                // 2. Digital Inputs I[0..127] at 0x0100
                for (int i = 0; i < plc.I.Length && i < 128; i++)
                    ramBuffer[RAM_OFS_I + i] = plc.I[i] ? (byte)1 : (byte)0;

                // 3. Digital Outputs O[0..127] at 0x0180
                for (int i = 0; i < plc.O.Length && i < 128; i++)
                    ramBuffer[RAM_OFS_O + i] = plc.O[i] ? (byte)1 : (byte)0;

                // 4. System Bits SB[0..511] at 0x0200
                for (int i = 0; i < plc.SB.Length && i < 512; i++)
                    ramBuffer[RAM_OFS_SB + i] = plc.SB[i] ? (byte)1 : (byte)0;

                // 5. System Integers SI[0..511] at 0x0400
                Buffer.BlockCopy(plc.SI, 0, ramBuffer, RAM_OFS_SI, Math.Min(plc.SI.Length * 2, 1024));

                // 6. Memory Bits MB[0..4095] at 0x1000
                for (int i = 0; i < plc.MB.Length && i < 4096; i++)
                    ramBuffer[RAM_OFS_MB + i] = plc.MB[i] ? (byte)1 : (byte)0;

                // 7. Memory Integers MI[0..2047] at 0x2000
                Buffer.BlockCopy(plc.MI, 0, ramBuffer, RAM_OFS_MI, Math.Min(plc.MI.Length * 2, 4096));

                // 8. Memory Longs ML[0..511] at 0x3000
                Buffer.BlockCopy(plc.ML, 0, ramBuffer, RAM_OFS_ML, Math.Min(plc.ML.Length * 4, 2048));

                // 9. Double Words DW[0..511] at 0x3800
                Buffer.BlockCopy(plc.DW, 0, ramBuffer, RAM_OFS_DW, Math.Min(plc.DW.Length * 4, 2048));

                // 10. Memory Floats MF[0..63] at 0x4000
                Buffer.BlockCopy(plc.MF, 0, ramBuffer, RAM_OFS_MF, Math.Min(plc.MF.Length * 4, 256));

                // 11. Analog Outputs AO_Volts[0..7] at 0x4100
                Buffer.BlockCopy(plc.AO_Volts, 0, ramBuffer, RAM_OFS_AO, Math.Min(plc.AO_Volts.Length * 8, 64));

                // 12. Timers T[0..255] at 0x5000 (16 bytes each)
                for (int i = 0; i < plc.T.Length && i < 256; i++)
                {
                    int ofs = RAM_OFS_T + (i * 16);
                    TimerState t = plc.T[i];
                    if (t != null)
                    {
                        WriteInt32LE(ramBuffer, ofs + 0, t.PresetMs);
                        WriteInt32LE(ramBuffer, ofs + 4, t.CurrentMs);
                        ramBuffer[ofs + 8] = t.Run ? (byte)1 : (byte)0;
                        ramBuffer[ofs + 9] = t.Done ? (byte)1 : (byte)0;
                    }
                }

                // 13. Counters C[0..31] at 0x6000 (16 bytes each)
                for (int i = 0; i < plc.C.Length && i < 32; i++)
                {
                    int ofs = RAM_OFS_C + (i * 16);
                    CounterState c = plc.C[i];
                    if (c != null)
                    {
                        WriteInt32LE(ramBuffer, ofs + 0, c.Preset);
                        WriteInt32LE(ramBuffer, ofs + 4, c.Current);
                        ramBuffer[ofs + 8] = c.Done ? (byte)1 : (byte)0;
                    }
                }

                ramDirty = true;

                DateTime now = DateTime.UtcNow;
                if (forceFlushToDisk || (now - lastRamDiskFlushUtc).TotalMilliseconds >= 250)
                {
                    FlushRamToDiskInternal();
                }
            }
        }

        /// <summary>
        /// Loads operands from ram.bin into PlcEngine (used for live external debugging or state restore).
        /// </summary>
        public bool LoadOperandsFromRamFile(PlcEngine plc)
        {
            if (plc == null || !File.Exists(ramFilePath)) return false;

            lock (syncLock)
            {
                try
                {
                    using (FileStream fs = new FileStream(ramFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (fs.Length < RAM_SIZE) return false;
                        int read = 0;
                        while (read < RAM_SIZE)
                        {
                            int n = fs.Read(ramBuffer, read, RAM_SIZE - read);
                            if (n <= 0) break;
                            read += n;
                        }
                    }

                    if (ramBuffer[0] != (byte)'V' || ramBuffer[4] != (byte)'R' || ramBuffer[5] != (byte)'A' || ramBuffer[6] != (byte)'M')
                        return false;

                    for (int i = 0; i < plc.I.Length && i < 128; i++)
                        plc.I[i] = (ramBuffer[RAM_OFS_I + i] != 0);
                    for (int i = 0; i < plc.O.Length && i < 128; i++)
                        plc.O[i] = (ramBuffer[RAM_OFS_O + i] != 0);
                    for (int i = 0; i < plc.SB.Length && i < 512; i++)
                        plc.SB[i] = (ramBuffer[RAM_OFS_SB + i] != 0);

                    Buffer.BlockCopy(ramBuffer, RAM_OFS_SI, plc.SI, 0, Math.Min(plc.SI.Length * 2, 1024));

                    for (int i = 0; i < plc.MB.Length && i < 4096; i++)
                        plc.MB[i] = (ramBuffer[RAM_OFS_MB + i] != 0);

                    Buffer.BlockCopy(ramBuffer, RAM_OFS_MI, plc.MI, 0, Math.Min(plc.MI.Length * 2, 4096));
                    Buffer.BlockCopy(ramBuffer, RAM_OFS_ML, plc.ML, 0, Math.Min(plc.ML.Length * 4, 2048));
                    Buffer.BlockCopy(ramBuffer, RAM_OFS_DW, plc.DW, 0, Math.Min(plc.DW.Length * 4, 2048));
                    Buffer.BlockCopy(ramBuffer, RAM_OFS_MF, plc.MF, 0, Math.Min(plc.MF.Length * 4, 256));
                    Buffer.BlockCopy(ramBuffer, RAM_OFS_AO, plc.AO_Volts, 0, Math.Min(plc.AO_Volts.Length * 8, 64));

                    for (int i = 0; i < plc.T.Length && i < 256; i++)
                    {
                        int ofs = RAM_OFS_T + (i * 16);
                        if (plc.T[i] == null) plc.T[i] = new TimerState();
                        plc.T[i].PresetMs = BitConverter.ToInt32(ramBuffer, ofs + 0);
                        plc.T[i].CurrentMs = BitConverter.ToInt32(ramBuffer, ofs + 4);
                        plc.T[i].Run = (ramBuffer[ofs + 8] != 0);
                        plc.T[i].Done = (ramBuffer[ofs + 9] != 0);
                    }

                    for (int i = 0; i < plc.C.Length && i < 32; i++)
                    {
                        int ofs = RAM_OFS_C + (i * 16);
                        if (plc.C[i] == null) plc.C[i] = new CounterState();
                        plc.C[i].Preset = BitConverter.ToInt32(ramBuffer, ofs + 0);
                        plc.C[i].Current = BitConverter.ToInt32(ramBuffer, ofs + 4);
                        plc.C[i].Done = (ramBuffer[ofs + 8] != 0);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("PlcMemoryStorage: Error loading operands from ram.bin: " + ex.Message);
                    return false;
                }
            }
        }

        /// <summary>
        /// Flushes any pending changes in romBuffer and ramBuffer to rom.bin and ram.bin on disk.
        /// </summary>
        public void FlushAll(PlcEngine plc = null)
        {
            lock (syncLock)
            {
                if (plc != null)
                {
                    SyncRamFromEngine(plc, true);
                }
                else if (ramDirty)
                {
                    FlushRamToDiskInternal();
                }

                if (romDirty)
                {
                    FlushRomToDiskInternal();
                }
            }
        }

        /// <summary>
        /// Resets both rom.bin and ram.bin to clean factory state.
        /// </summary>
        public void ClearFactoryReset(PlcEngine plc = null)
        {
            lock (syncLock)
            {
                ResetRomDefaultsInternal();
                ResetRamDefaultsInternal();
                FlushRomToDiskInternal();
                if (plc != null)
                {
                    SyncRamFromEngine(plc, true);
                }
                else
                {
                    FlushRamToDiskInternal();
                }
            }
        }

        private void FlushRomToDiskInternal()
        {
            try
            {
                using (FileStream fs = new FileStream(romFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
                {
                    fs.SetLength(ROM_SIZE);
                    fs.Position = 0;
                    fs.Write(romBuffer, 0, ROM_SIZE);
                    fs.Flush();
                }
                romDirty = false;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("PlcMemoryStorage: Failed to write rom.bin: " + ex.Message);
            }
        }

        private void FlushRamToDiskInternal()
        {
            try
            {
                using (FileStream fs = new FileStream(ramFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
                {
                    fs.SetLength(RAM_SIZE);
                    fs.Position = 0;
                    fs.Write(ramBuffer, 0, RAM_SIZE);
                    fs.Flush();
                }
                ramDirty = false;
                lastRamDiskFlushUtc = DateTime.UtcNow;
                lastOwnRamFileWriteUtc = File.GetLastWriteTimeUtc(ramFilePath);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("PlcMemoryStorage: Failed to write ram.bin: " + ex.Message);
            }
        }

        private static void WriteInt16LE(byte[] buf, int offset, short val)
        {
            buf[offset + 0] = (byte)(val & 0xFF);
            buf[offset + 1] = (byte)((val >> 8) & 0xFF);
        }

        private static void WriteInt32LE(byte[] buf, int offset, int val)
        {
            buf[offset + 0] = (byte)(val & 0xFF);
            buf[offset + 1] = (byte)((val >> 8) & 0xFF);
            buf[offset + 2] = (byte)((val >> 16) & 0xFF);
            buf[offset + 3] = (byte)((val >> 24) & 0xFF);
        }

        private static void WriteUInt32LE(byte[] buf, int offset, uint val)
        {
            buf[offset + 0] = (byte)(val & 0xFF);
            buf[offset + 1] = (byte)((val >> 8) & 0xFF);
            buf[offset + 2] = (byte)((val >> 16) & 0xFF);
            buf[offset + 3] = (byte)((val >> 24) & 0xFF);
        }

        private static void WriteInt64LE(byte[] buf, int offset, long val)
        {
            byte[] b = BitConverter.GetBytes(val);
            Array.Copy(b, 0, buf, offset, 8);
        }

        private static void WriteFixedAscii(byte[] buf, int offset, int maxLen, string text)
        {
            Array.Clear(buf, offset, maxLen);
            if (string.IsNullOrEmpty(text)) return;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            int copyLen = Math.Min(bytes.Length, maxLen - 1);
            Array.Copy(bytes, 0, buf, offset, copyLen);
        }
    }
}
