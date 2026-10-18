using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace UnitronicsV350Simulator
{
    public class PcomServer
    {
        public const int DEFAULT_PORT = 20256; // Standard Unitronics PCOM TCP port

        private PlcEngine plc;
        private TcpListener listener;
        private Thread listenThread;
        private bool isRunning = false;

        // Event notifying the UI when a project has been downloaded from VisiLogic
        public event Action<string> OnProjectDownloaded;

        public bool IsActive { get { return isRunning; } }

        public PcomServer(PlcEngine engine)
        {
            plc = engine;
        }

        public void Start(int port = DEFAULT_PORT)
        {
            if (isRunning) return;

            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                isRunning = true;

                listenThread = new Thread(ListenLoop);
                listenThread.IsBackground = true;
                listenThread.Start();

                plc.Log(string.Format("PCOM TCP server started on port {0}. Waiting for VisiLogic (Download / Online Test)...", port));
            }
            catch (Exception ex)
            {
                plc.Log("Error starting PCOM TCP server: " + ex.Message);
            }
        }

        public void Stop()
        {
            isRunning = false;
            try
            {
                if (listener != null) listener.Stop();
            }
            catch { }
            plc.Log("PCOM TCP server stopped.");
        }

        private void ListenLoop()
        {
            while (isRunning)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleClient, client);
                }
                catch
                {
                    if (!isRunning) break;
                }
            }
        }

        private void HandleClient(object obj)
        {
            TcpClient client = (TcpClient)obj;
            string ep = client.Client.RemoteEndPoint.ToString();
            plc.Log("VisiLogic / PCOM client connected from: " + ep);

            try
            {
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] buffer = new byte[65542];
                    while (isRunning && client.Connected)
                    {
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break;

                        if (bytesRead >= 6 && (buffer[2] == 101 || buffer[2] == 102))
                        {
                            int expectedTotal = 6 + (buffer[4] | (buffer[5] << 8));
                            while (bytesRead < expectedTotal && bytesRead < buffer.Length)
                            {
                                int more = stream.Read(buffer, bytesRead, expectedTotal - bytesRead);
                                if (more <= 0) break;
                                bytesRead += more;
                            }
                        }

                        byte[] response = ProcessPcomPacket(buffer, bytesRead);
                        if (response != null && response.Length > 0)
                        {
                            stream.Write(response, 0, response.Length);
                            stream.Flush();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                plc.Log("PCOM session closed (" + ep + "): " + ex.Message);
            }
            finally
            {
                try { client.Close(); } catch { }
            }
        }

        private byte[] ProcessPcomPacket(byte[] data, int length)
        {
            if (length <= 0) return null;

            int offset = 0;
            byte[] pcomTcpHeader = null;
            bool isTcpFramed = false;

            // Check if packet contains the 6-byte PCOM/TCP header:
            // [0..1] Transaction ID
            // [2] Protocol Type: 101 (0x65) = ASCII, 102 (0x66) = Binary
            // [3] Reserved: 0
            // [4..5] PCOM Packet Length (uint16 LE)
            if (length >= 6 && (data[2] == 101 || data[2] == 102))
            {
                isTcpFramed = true;
                pcomTcpHeader = new byte[6];
                Array.Copy(data, 0, pcomTcpHeader, 0, 6);
                offset = 6;
            }
            else if (length >= 6 && data[0] != '/' && !(data[0] == '/' && data[1] == '_'))
            {
                isTcpFramed = true;
                pcomTcpHeader = new byte[6];
                Array.Copy(data, 0, pcomTcpHeader, 0, 6);
                offset = 6;
            }

            int pcomLen = length - offset;
            if (pcomLen <= 0) return null;

            // Determine whether payload is PCOM ASCII or PCOM Binary
            if (data[offset] == '/' && (pcomLen < 2 || data[offset + 1] != '_'))
            {
                // PCOM ASCII
                string asciiReq = Encoding.ASCII.GetString(data, offset, pcomLen);
                plc.Log("PCOM ASCII REQ: " + asciiReq.Trim());

                string asciiResp = ProcessPcomAsciiCommand(asciiReq);
                if (string.IsNullOrEmpty(asciiResp)) return null;

                plc.Log("PCOM ASCII RESP: " + asciiResp.Trim());
                byte[] rawResp = Encoding.ASCII.GetBytes(asciiResp);

                if (isTcpFramed && pcomTcpHeader != null)
                {
                    byte[] fullResp = new byte[6 + rawResp.Length];
                    Array.Copy(pcomTcpHeader, fullResp, 6);
                    fullResp[2] = 101; // 101 = ASCII mode
                    fullResp[3] = 0;
                    fullResp[4] = (byte)(rawResp.Length & 0xFF);
                    fullResp[5] = (byte)((rawResp.Length >> 8) & 0xFF);
                    Array.Copy(rawResp, 0, fullResp, 6, rawResp.Length);
                    return fullResp;
                }
                return rawResp;
            }
            else if (pcomLen >= 6 && data[offset] == 0x2F && data[offset + 1] == 0x5F) // /_OPLC
            {
                // PCOM Binary
                plc.Log(string.Format("PCOM Binary REQ received (length: {0} bytes)", pcomLen));
                byte[] binResp = ProcessPcomBinaryCommand(data, offset, pcomLen);
                if (binResp == null) return null;

                if (isTcpFramed && pcomTcpHeader != null)
                {
                    byte[] fullResp = new byte[6 + binResp.Length];
                    Array.Copy(pcomTcpHeader, fullResp, 6);
                    fullResp[2] = 102; // 102 = Binary mode
                    fullResp[3] = 0;
                    fullResp[4] = (byte)(binResp.Length & 0xFF);
                    fullResp[5] = (byte)((binResp.Length >> 8) & 0xFF);
                    Array.Copy(binResp, 0, fullResp, 6, binResp.Length);
                    return fullResp;
                }
                return binResp;
            }

            return null;
        }

        private string ProcessPcomAsciiCommand(string req)
        {
            req = req.Trim();
            if (req.Length < 3) return null;

            string unitId = "00";
            if (req.Length >= 3 && req.StartsWith("/"))
            {
                unitId = req.Substring(1, 2);
            }

            string cmd = req.Length >= 5 ? req.Substring(3) : "";

            // Strip trailing 2-character hex checksum if present
            // e.g. /00GS01330122 -> unitId="00", cmd="GS01330122", stripped: "GS013301"
            string cmdBody = cmd;
            if (cmdBody.Length >= 2)
            {
                cmdBody = cmdBody.Substring(0, cmdBody.Length - 2);
            }

            // 1. Read System Bits (GS)
            if (cmdBody.StartsWith("GS"))
            {
                // Format: GS<Address:4 hex><Length:2 hex>
                // e.g. GS013301 -> Read 1 SB bit at 0x0133 (SB 307 - Flash busy / readiness status)
                // Each bit is encoded as 1 ASCII character ('0' or '1')
                int addr = 0;
                int len = 1;
                if (cmdBody.Length >= 8)
                {
                    try
                    {
                        addr = Convert.ToInt32(cmdBody.Substring(2, 4), 16);
                        len = Convert.ToInt32(cmdBody.Substring(6, 2), 16);
                    }
                    catch { }
                }

                StringBuilder sbData = new StringBuilder();
                for (int i = 0; i < len; i++)
                {
                    int bitAddr = addr + i;
                    bool val = false;
                    if (bitAddr >= 0 && bitAddr < plc.SB.Length)
                    {
                        val = plc.SB[bitAddr];
                    }
                    sbData.Append(val ? "1" : "0");
                }

                plc.Log(string.Format("PCOM ASCII GS: Read SB[{0}..{1}] = {2}", addr, addr + len - 1, sbData.ToString()));
                return FormatPcomAsciiResponse(unitId, "GS" + sbData.ToString());
            }

            // 2. Controller Identification (ID, _ID, UG, GP, PW)
            else if (cmdBody.StartsWith("ID") || cmdBody.StartsWith("_ID"))
            {
                // Standard fixed-width 58/56-char OPLC ID response expected by UnCmDrv1.dll
                string payload = (plc != null && plc.Profile != null)
                    ? plc.Profile.GetPcomIdString()
                    : ("35RA22" + HardwareProfile.DefaultPcomVersionTail);
                return FormatPcomAsciiResponse(unitId, "ID" + payload);
            }
            else if (cmdBody.StartsWith("UG"))
            {
                // Get Unit ID response
                return FormatPcomAsciiResponse(unitId, "UG" + unitId);
            }
            else if (cmdBody.StartsWith("NMR"))
            {
                // Read PLC Name in ASCII mode (UnVsCon2.dll 0x1E2A48..0x1E2CFF):
                // UnVsCon2.dll extracts Mid$(sResp, 3, Len(sResp) - 4) and compares against "", "ND", or expected PLC Name.
                string plcName = ResolveExpectedPlcName();
                plc.Log("PCOM ASCII NMR: Read PLC Name -> returning '" + plcName + "'");
                return FormatPcomAsciiResponse(unitId, "NM" + plcName);
            }
            else if (cmdBody.StartsWith("NMW"))
            {
                // Write PLC Name in ASCII mode
                return FormatPcomAsciiResponse(unitId, "NM");
            }
            else if (cmdBody.StartsWith("GP"))
            {
                // Get PLC Name in ASCII mode (16 chars)
                string plcName = ResolveExpectedPlcName();
                plcName = plcName.PadRight(16, ' ').Substring(0, 16);
                return FormatPcomAsciiResponse(unitId, "GP" + plcName);
            }
            else if (cmdBody.StartsWith("PW") || cmdBody.StartsWith("EP"))
            {
                // Password / Ethernet Password check in ASCII mode -> acknowledge with 2-char prefix
                return FormatPcomAsciiResponse(unitId, cmdBody.Substring(0, 2));
            }

            // 3. Read System Integers (GF)
            else if (cmdBody.StartsWith("GF"))
            {
                // Format: GF<Address:4 hex><Length:2 hex>
                // e.g. GF01FF01 -> Read SI 511 (0x01FF), GF00FC01 -> Read SI 252 (0x00FC)
                int addr = 0;
                int len = 1;
                if (cmdBody.Length >= 8)
                {
                    try
                    {
                        addr = Convert.ToInt32(cmdBody.Substring(2, 4), 16);
                        len = Convert.ToInt32(cmdBody.Substring(6, 2), 16);
                    }
                    catch { }
                }

                StringBuilder sbData = new StringBuilder();
                for (int i = 0; i < len; i++)
                {
                    int regAddr = addr + i;
                    short val = 0;
                    if (regAddr >= 0 && regAddr < plc.SI.Length)
                    {
                        val = plc.SI[regAddr];
                    }
                    if (regAddr == 511 && val == 0)
                    {
                        val = 101; // Default communication readiness parameter
                    }
                    sbData.Append(((ushort)val).ToString("X4"));
                }

                plc.Log(string.Format("PCOM ASCII GF: Read SI[{0}..{1}] = {2}", addr, addr + len - 1, sbData.ToString()));
                return FormatPcomAsciiResponse(unitId, "GF" + sbData.ToString());
            }

            // 4. CPU Control: STOP (CCS, ST)
            else if (cmdBody.StartsWith("CCS") || cmdBody.StartsWith("ST"))
            {
                plc.Stop();
                plc.Log("PCOM: PLC engine stopped by VisiLogic request (STOP mode).");
                return FormatPcomAsciiResponse(unitId, cmdBody.Substring(0, 2));
            }

            // 5. CPU Control: RUN (CCR, RN)
            else if (cmdBody.StartsWith("CCR") || cmdBody.StartsWith("RN"))
            {
                plc.Start();
                plc.Log("PCOM: PLC engine started by VisiLogic request (RUN mode).");
                TriggerProjectAutoReload();
                return FormatPcomAsciiResponse(unitId, cmdBody.Substring(0, 2));
            }

            // 6. Clear Communication Buffer (CCB)
            else if (cmdBody.StartsWith("CCB"))
            {
                plc.Log("PCOM: Communication buffer cleared (CCB).");
                return FormatPcomAsciiResponse(unitId, "CC");
            }

            // 7. CPU Reset / Init (RS, CCI, CCE, CCM)
            else if (cmdBody.StartsWith("RS") || cmdBody.StartsWith("CCI") || cmdBody.StartsWith("CCE") || cmdBody.StartsWith("CCM"))
            {
                plc.Reset();
                return FormatPcomAsciiResponse(unitId, cmdBody.Substring(0, 2));
            }

            // 8. Read Memory Integers MI (RMI, RW, RZI, RE)
            else if (cmdBody.StartsWith("RMI") || cmdBody.StartsWith("RW") || cmdBody.StartsWith("RZI") || cmdBody.StartsWith("RE"))
            {
                string op = cmdBody.StartsWith("RMI") ? "RMI" : (cmdBody.StartsWith("RZI") ? "RZI" : cmdBody.Substring(0, 2));
                int addr = 0;
                int len = 1;
                int paramOffset = op.Length;
                if (cmdBody.Length >= paramOffset + 6)
                {
                    try
                    {
                        addr = Convert.ToInt32(cmdBody.Substring(paramOffset, 4), 16);
                        len = Convert.ToInt32(cmdBody.Substring(paramOffset + 4, 2), 16);
                    }
                    catch { }
                }

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < len; i++)
                {
                    int reg = addr + i;
                    short val = (reg >= 0 && reg < plc.MI.Length) ? plc.MI[reg] : (short)0;
                    sb.Append(((ushort)val).ToString("X4"));
                }
                return FormatPcomAsciiResponse(unitId, cmdBody.Substring(0, 2) + sb.ToString());
            }

            // 9. Read Memory Bits MB (RMB, RB, RZB, RT)
            else if (cmdBody.StartsWith("RMB") || cmdBody.StartsWith("RB") || cmdBody.StartsWith("RZB") || cmdBody.StartsWith("RT"))
            {
                string op = cmdBody.StartsWith("RMB") ? "RMB" : (cmdBody.StartsWith("RZB") ? "RZB" : cmdBody.Substring(0, 2));
                int addr = 0;
                int len = 16;
                int paramOffset = op.Length;
                if (cmdBody.Length >= paramOffset + 6)
                {
                    try
                    {
                        addr = Convert.ToInt32(cmdBody.Substring(paramOffset, 4), 16);
                        len = Convert.ToInt32(cmdBody.Substring(paramOffset + 4, 2), 16);
                    }
                    catch { }
                }

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < len; i++)
                {
                    int bitIdx = addr + i;
                    bool bitVal = (bitIdx >= 0 && bitIdx < plc.MB.Length) ? plc.MB[bitIdx] : false;
                    sb.Append(bitVal ? "1" : "0");
                }
                return FormatPcomAsciiResponse(unitId, cmdBody.Substring(0, 2) + sb.ToString());
            }

            // 10. Write Operands (SZI, SZB, SW, SB, WMI, WMB, SF, SS)
            else if (cmdBody.StartsWith("SZI") || cmdBody.StartsWith("SZB") || cmdBody.StartsWith("SW") ||
                     cmdBody.StartsWith("SB") || cmdBody.StartsWith("WMI") || cmdBody.StartsWith("WMB") ||
                     cmdBody.StartsWith("SF") || cmdBody.StartsWith("SS"))
            {
                return FormatPcomAsciiResponse(unitId, cmdBody.Substring(0, 2));
            }

            // 11. Fallback: Echo 2-character command prefix as valid PCOM ASCII ACK
            else
            {
                string op = cmdBody.Length >= 2 ? cmdBody.Substring(0, 2) : "OK";
                return FormatPcomAsciiResponse(unitId, op);
            }
        }

        private string FormatPcomAsciiResponse(string unitId, string data)
        {
            // PCOM ASCII response format: /A + UnitID + Data + Checksum (2 hex uppercase) + \r
            // UnCmDrv1.dll (0x02ECE7) computes the checksum as the sum of ASCII character codes AFTER "/A" (i.e. UnitID + Data) modulo 256
            string payload = unitId + data;

            int sum = 0;
            for (int i = 0; i < payload.Length; i++)
            {
                sum += (int)payload[i];
            }
            int chk = sum & 0xFF;

            return "/A" + payload + chk.ToString("X2") + "\r";
        }

        private byte[] ProcessPcomBinaryCommand(byte[] data, int offset, int length)
        {
            // PCOM Binary starts with /_OPLC (6 bytes: 0x2F, 0x5F, 0x4F, 0x50, 0x4C, 0x43)
            // Header size: 24 bytes
            // Byte 6: Destination ID (Unit ID in request, 0xFE in response)
            // Byte 7: Source ID (0xFE in request, Unit ID in response)
            // Bytes 8..11: Reserved / Protocol flags (echoed from request)
            // Byte 12: Opcode (e.g. 0x41 = Write Memory, 0x1A = Get Flash Info, 0x45 = Flash Commit)
            // Byte 13: Sub-opcode / Status (0x00 = OK in response)
            // Bytes 14..19: Details (6 bytes, e.g. memory address + requested length)
            // Bytes 20..21: Payload Data Length (uint16 LE)
            // Bytes 22..23: Header CRC (uint16 LE, 16-bit two's complement of sum of bytes 0..21)
            // Bytes 24..: Payload Data
            // Last 3 bytes: Data CRC (2 bytes LE) + '\' (0x5C)
            if (length < 24) return null;

            byte opcode = data[offset + 12];
            ushort dataLen = (ushort)(data[offset + 20] | (data[offset + 21] << 8));

            byte[] details = new byte[6];
            Array.Copy(data, offset + 14, details, 0, 6);

            plc.Log(string.Format("PCOM Binary REQ: Opcode=0x{0:X2}, DataLen={1}", opcode, dataLen));

            byte[] respPayload = null;

            switch (opcode)
            {
                case 0x41: // Write Memory (Program / DLU / Font / HMI resource block write)
                    plc.Log(string.Format("PCOM Binary: OPLC program memory block write (0x41, {0} bytes)", dataLen));
                    break;

                case 0x1A: // Get Flash / Buffer Info (called at start of Download Sequence)
                    // Returns 8 bytes: BufferSize = 980 (0x000003D4) at [0..3], SectorSize = 65536 (0x00010000) at [4..7]
                    plc.Log("PCOM Binary: Get Flash/Buffer Info (0x1A) -> BufferSize=980, SectorSize=65536");
                    respPayload = new byte[] { 0xD4, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00 };
                    break;

                case 0x45: // Flash & Memory Commit ("Programming Flash and Memory")
                    plc.Log("PCOM Binary: FLASH commit command (0x45) - program successfully downloaded by VisiLogic!");
                    TriggerProjectAutoReload();
                    break;

                case 0x0F: // PLC Run / Stop / Reset / Init control command
                {
                    ushort subCmd = (ushort)(details[0] | (details[1] << 8));
                    if (subCmd == 0 && dataLen >= 2 && length >= 26)
                    {
                        subCmd = (ushort)(data[offset + 24] | (data[offset + 25] << 8));
                    }
                    if (subCmd == 0x5555)
                    {
                        plc.Stop();
                        plc.Log("PCOM Binary: PLC stopped (0x0F sub=0x5555).");
                    }
                    else if (subCmd == 0xAAAA || subCmd == 0x5A5A)
                    {
                        plc.Start();
                        plc.Log("PCOM Binary: PLC started (0x0F sub=0x" + subCmd.ToString("X4") + ").");
                        TriggerProjectAutoReload();
                    }
                    else if (subCmd == 0xA5A5)
                    {
                        plc.Reset();
                        plc.Start();
                        plc.Log("PCOM Binary: PLC reset & started (0x0F sub=0xA5A5).");
                        TriggerProjectAutoReload();
                    }
                    break;
                }

                case 0x16: // Translate Index to Address (UnCmDrv1.dll 0x040736, UnVsCon2.dll 0x16F383 / 0x1EB423 / 0x204F96)
                {
                    // UnVsCon2.dll 0x16F383 (called by prvGetDataOfTopic for Topic 4 "Info Tables"),
                    // 0x1EB423, and 0x204F96 read two little-endian uint32 values from bRecievedData:
                    //   - [2..5] = uint32 FlashAddress (0x00100000)
                    //   - [6..9] = uint32 TopicByteLength (0x00000040 = 64 bytes)
                    // Then 0x16F383 reads TopicByteLength bytes via Opcode 0x01 and 0x16F29F indexes
                    // len = arr[5], arr[len - 2], arr[len - 1].
                    byte topicIdx = data[offset + 13];
                    if (topicIdx == 0) topicIdx = details[0];
                    plc.Log(string.Format("PCOM Binary: Translate OPLC memory index (0x16, topic={0}) -> Addr=0x00100000, Len=64", topicIdx));
                    respPayload = new byte[16];
                    respPayload[0] = 0x01; // valid entry flag
                    // [2..5] FlashAddress = 0x00100000 (uint32 LE: 00 00 10 00)
                    respPayload[2] = 0x00;
                    respPayload[3] = 0x00;
                    respPayload[4] = 0x10;
                    respPayload[5] = 0x00;
                    // [6..9] TopicByteLength = 0x00000040 (64 bytes, uint32 LE: 40 00 00 00)
                    respPayload[6] = 0x40;
                    respPayload[7] = 0x00;
                    respPayload[8] = 0x00;
                    respPayload[9] = 0x00;
                    break;
                }

                case 0x02: // Check Password
                    plc.Log("PCOM Binary: OPLC access password check (0x02) -> OK");
                    break;

                case 0x03: // Read ISC Table in PLC (UnVsCon2.dll clsDownloadSequence.prvGetISCInPLC 0x23E23B)
                {
                    ushort iscLen = (ushort)(details[4] | (details[5] << 8));
                    if (iscLen < 16) iscLen = 16;
                    plc.Log(string.Format("PCOM Binary: Read ISC table (0x03, len={0})", iscLen));
                    respPayload = new byte[iscLen];
                    break;
                }

                case 0x07: // Read PLC Status Block (UnVsCon2.dll 0x239EFA / 0x23ADD9 / 0x23B08B -> parsed by 0x1E01B4)
                {
                    // UnVsCon2.dll requests 106 bytes (details[4..5] = 0x006A) and 0x1E01B4 indexes up to resp[93].
                    // 0x23ADA0 requires resp[3] == 0 (no active flash error).
                    // 0x23B052 requires resp[0] == 'S' (0x53, Stop/Ready mode for download).
                    ushort statusLen = (ushort)(details[4] | (details[5] << 8));
                    if (statusLen < 106) statusLen = 106;
                    plc.Log(string.Format("PCOM Binary: Read OPLC status block (0x07, len={0}) -> Mode='S'", statusLen));
                    respPayload = new byte[statusLen];
                    respPayload[0] = (byte)'S'; // 0x53 = Stopped / Ready for download
                    respPayload[3] = 0x00;      // 0x00 = Status OK
                    break;
                }

                case 0x0C: // Read PLC Name (UnCmDrv1.dll z_ReadPlcName 0x03EDDD, called by UnVsCon2.dll 0x1E289E)
                {
                    // UnCmDrv1.dll VerifyBinaryResponse (0x04ED24) requires resp[12] == 0x8C (req[12] + 0x80).
                    // UnCmDrv1.dll z_ReadPlcName (0x03EDDD..0x03F048) converts the entire received byte array
                    // using VB6 Trim(StrConv(bRecievedData, vbUnicode)) (rtcStrConvVar + rtcTrimVar).
                    // Critically, VB6 StrConv(..., vbUnicode) converts ALL bytes in the array and does NOT stop
                    // at NUL (0x00) bytes, while Trim() only strips spaces (0x20). Therefore, respPayload must
                    // contain only the exact ASCII characters of the PLC Name (or space-padded, never 0x00-padded)
                    // so that __vbaStrCmp in UnVsCon2.dll (0x1E2942) matches the expected PLC Name.
                    string plcName = ResolveExpectedPlcName();
                    if (plcName != null)
                    {
                        plcName = plcName.Trim('\0', ' ', '\t', '\r', '\n');
                    }
                    if (string.IsNullOrEmpty(plcName))
                    {
                        plcName = "virtual";
                    }

                    plc.Log("PCOM Binary: Read PLC Name request (0x0C) -> returning '" + plcName + "' (" + plcName.Length + " bytes)");
                    respPayload = Encoding.ASCII.GetBytes(plcName);
                    break;
                }

                case 0x10: // Where Resource (UnCmDrv1.dll 0x0400E7, UnVsCon2.dll clsBackUpVlp 0x1F2BAA & 0x2278ED & 0x227B9C)
                {
                    // UnCmDrv1.dll BuildBinaryCommand (0x04CE6E) places the resource ID in header byte 13 (data[offset + 13]).
                    // UnVsCon2.dll copies 3 int32 values ([0..3]=BaseAddress, [4..7]=TotalSize, [8..11]=SectorSize)
                    // from bRecievedData.
                    byte resType = data[offset + 13];
                    if (resType == 0) resType = details[0];
                    bool isSmallResource = (resType == 0x11 || details[0] == 0x11 || details[2] == 0x11 || details[4] == 0x11);
                    respPayload = new byte[16];
                    // [0..3] BaseAddress = 0x00300000 (3 MB offset)
                    respPayload[0] = 0x00;
                    respPayload[1] = 0x00;
                    respPayload[2] = 0x30;
                    respPayload[3] = 0x00;
                    // [4..7] TotalSize = 0x00010000 (64 KB) for resource 0x11, or 0x001DE000 (~1.9 MB) for main flash/backup
                    if (isSmallResource)
                    {
                        respPayload[4] = 0x00;
                        respPayload[5] = 0x00;
                        respPayload[6] = 0x01;
                        respPayload[7] = 0x00;
                    }
                    else
                    {
                        respPayload[4] = 0x00;
                        respPayload[5] = 0xE0;
                        respPayload[6] = 0x1D;
                        respPayload[7] = 0x00;
                    }
                    // [8..11] SectorSize = 0x00010000 (64 KB)
                    respPayload[8] = 0x00;
                    respPayload[9] = 0x00;
                    respPayload[10] = 0x01;
                    respPayload[11] = 0x00;
                    plc.Log(string.Format("PCOM Binary: Locate OPLC resource (0x10, resType=0x{0:X2}, details={1:X2}-{2:X2}-{3:X2}) -> Base=0x00300000, Sector=64KB",
                        resType, details[0], details[2], details[4]));
                    break;
                }

                case 0x18: // Get Download Signature / Flash State (UnVsCon2.dll 0x1F3ACD / 0x2292DB / 0x2329C8 / 0x24EA5D)
                {
                    // 0x24EA5D indexes resp[0] and uint32 at resp[6..9].
                    plc.Log("PCOM Binary: Read OPLC download signature/state (0x18)");
                    respPayload = new byte[16];
                    break;
                }

                case 0x1E: // Get HMI / Font Flash Region (UnVsCon2.dll 0x178BFE & 0x17CC36)
                {
                    // Reads uint32 BaseAddress at [2..5] (0x0010F000) and uint32 Size at [6..9] (0x0017C000).
                    plc.Log("PCOM Binary: Get HMI/Font Flash region (0x1E) -> Base=0x0010F000, Size=0x0017C000");
                    respPayload = new byte[16];
                    respPayload[0] = 0x01;
                    respPayload[2] = 0x00;
                    respPayload[3] = 0xF0;
                    respPayload[4] = 0x10;
                    respPayload[5] = 0x00;
                    respPayload[6] = 0x00;
                    respPayload[7] = 0xC0;
                    respPayload[8] = 0x17;
                    respPayload[9] = 0x00;
                    break;
                }

                case 0x20: // Get Extended Flash / Download Status (UnVsCon2.dll 0x22896C & 0x232C9E)
                {
                    // Indexes resp[0] and uint32 at resp[8..11].
                    plc.Log("PCOM Binary: Get extended flash status (0x20)");
                    respPayload = new byte[16];
                    break;
                }

                case 0x01: // Read Memory (UnCmDrv1.dll 0x041240, UnVsCon2.dll 0x16F383 Info Tables / 0x1EF805 / 0x227D3D)
                {
                    ushort reqReadLen = (ushort)(details[4] | (details[5] << 8));
                    if (reqReadLen == 0) reqReadLen = (ushort)(details[0] | (details[1] << 8));
                    if (reqReadLen == 0) reqReadLen = dataLen;
                    if (reqReadLen == 0) reqReadLen = 256;
                    plc.Log(string.Format("PCOM Binary: Read OPLC memory (0x01, len={0})", reqReadLen));
                    respPayload = new byte[reqReadLen];
                    if (respPayload.Length >= 16)
                    {
                        // Populate valid Vision Info Table header so UnVsCon2.dll 0x16F29F (len = arr[5],
                        // arr[len - 2], arr[len - 1]) and 0x16EA39 succeed without subscript or zero-seed errors.
                        respPayload[0] = (byte)'C';
                        respPayload[1] = (byte)'I';
                        respPayload[2] = 6;
                        respPayload[3] = 10;
                        respPayload[4] = 14;
                        respPayload[5] = 16;          // total Info Table header length = 16 bytes
                        respPayload[14] = (byte)'*';  // arr[16 - 2] non-zero seed byte 1
                        respPayload[15] = (byte)'*';  // arr[16 - 1] non-zero seed byte 2
                    }
                    break;
                }

                case 0x4D: // Binary Mix Read/Write Operands (UnCmDrv1.dll 0x0358BB & clsBinaryMixHandler 0x044530)
                {
                    // In opcode 0x4D requests, details[0..1] is groupCount (number of clsBinaryMixGroup blocks in payload),
                    // while details[2..5] are 0x00. UnCmDrv1.dll clsBinaryMixGroup.vt+40 (0x045A1B) -> vt+52 (0x045E69)
                    // indexes bRecievedData up to the total byte count of all requested operand reads (e.g. Timers/MIs/MBs).
                    // Returning only 4 bytes causes VB6 Err 9 ("Subscript out of range") at UnVsCon2.dll 0x18DA74 (Erl=12).
                    ushort groupCount = (ushort)(details[0] | (details[1] << 8));
                    int estimatedReadBytes = 0;
                    int payloadStart = offset + 24;
                    int payloadEnd = payloadStart + dataLen;
                    int pos = payloadStart;
                    if (payloadEnd <= data.Length)
                    {
                        for (int g = 0; g < groupCount && pos + 6 <= payloadEnd; g++)
                        {
                            byte readCount = data[pos];
                            byte writeCount = data[pos + 1];
                            ushort extraLen = (ushort)(data[pos + 4] | (data[pos + 5] << 8));
                            // Estimate up to 8 bytes per read item (Timers/Longs/Floats/Integers)
                            estimatedReadBytes += (readCount > 0 ? readCount : 64) * 8;
                            pos += 6 + extraLen + (readCount + writeCount) * 2;
                        }
                    }
                    int replyLen = Math.Max(estimatedReadBytes + 64, 1024);
                    plc.Log(string.Format("PCOM Binary: Read/Write OPLC operands (0x4D, groups={0}, reqDataLen={1}) -> returning {2} bytes",
                        groupCount, dataLen, replyLen));
                    respPayload = new byte[replyLen];
                    break;
                }

                default:
                {
                    ushort defaultReadLen = (ushort)(details[4] | (details[5] << 8));
                    if (defaultReadLen > 0)
                    {
                        int safeLen = Math.Max((int)defaultReadLen, 16);
                        plc.Log(string.Format("PCOM Binary: Opcode 0x{0:X2} -> Returning {1} zero bytes (requested={2})",
                            opcode, safeLen, defaultReadLen));
                        respPayload = new byte[safeLen];
                    }
                    else if (opcode == 0x09 || opcode == 0x0A || opcode == 0x0B || opcode == 0x0D ||
                             opcode == 0x13 || opcode == 0x42 || opcode == 0x4A || opcode == 0x4C ||
                             opcode == 0x50 || opcode == 0x55 || opcode == 0x58)
                    {
                        plc.Log(string.Format("PCOM Binary: Query Opcode 0x{0:X2} -> Returning 32 zero bytes", opcode));
                        respPayload = new byte[32];
                    }
                    else
                    {
                        plc.Log(string.Format("PCOM Binary: Opcode 0x{0:X2} -> Returning ACK success", opcode));
                    }
                    break;
                }
            }

            return BuildBinaryResponse(data, offset, opcode, respPayload, details);
        }

        private byte[] BuildBinaryResponse(byte[] reqBuffer, int reqOffset, byte reqOpcode, byte[] respData = null, byte[] details = null)
        {
            int dataLen = (respData != null) ? respData.Length : 0;
            byte[] resp = new byte[24 + dataLen + 3];

            // 1. Magic header: /_OPLC (6 bytes)
            byte[] magic = Encoding.ASCII.GetBytes("/_OPLC");
            Array.Copy(magic, 0, resp, 0, 6);

            // 2. Swap Destination / Source IDs and echo bytes 8..11 from request
            // UnCmDrv1.dll (0x04EC39..0x04ED5E) requires:
            //   resp[6] == req[7] (0xFE)
            //   resp[7] == req[6] (Unit ID)
            //   resp[8..9] == req[8..9]
            resp[6] = reqBuffer[reqOffset + 7];
            resp[7] = reqBuffer[reqOffset + 6];
            resp[8] = reqBuffer[reqOffset + 8];
            resp[9] = reqBuffer[reqOffset + 9];
            resp[10] = reqBuffer[reqOffset + 10];
            resp[11] = reqBuffer[reqOffset + 11];

            // 3. Response Opcode (reqOpcode + 0x80) and Status (0x00 = OK)
            resp[12] = (byte)(reqOpcode | 0x80);
            resp[13] = 0x00;

            // 4. Details (bytes 14..19 echoed from request by default; UnCmDrv1.dll checks bytes 14..17)
            if (details != null && details.Length >= 6)
            {
                Array.Copy(details, 0, resp, 14, 6);
            }
            else
            {
                Array.Copy(reqBuffer, reqOffset + 14, resp, 14, 6);
            }

            // 5. Response data length (uint16 LE)
            resp[20] = (byte)(dataLen & 0xFF);
            resp[21] = (byte)((dataLen >> 8) & 0xFF);

            // 6. Header CRC (bytes 0..21)
            ushort hdrCrc = CalcBinaryCrc(resp, 0, 22);
            resp[22] = (byte)(hdrCrc & 0xFF);
            resp[23] = (byte)((hdrCrc >> 8) & 0xFF);

            // 7. Response data payload
            if (dataLen > 0)
            {
                Array.Copy(respData, 0, resp, 24, dataLen);
            }

            // 8. Footer: Data CRC + '\' (0x5C)
            ushort dataCrc = CalcBinaryCrc(resp, 24, dataLen);
            resp[24 + dataLen] = (byte)(dataCrc & 0xFF);
            resp[24 + dataLen + 1] = (byte)((dataCrc >> 8) & 0xFF);
            resp[24 + dataLen + 2] = 0x5C; // '\\' suffix

            return resp;
        }

        private static ushort CalcBinaryCrc(byte[] buf, int offset, int count)
        {
            if (buf == null || count <= 0) return 0;
            int sum = 0;
            for (int i = 0; i < count; i++)
            {
                sum += buf[offset + i];
            }
            // Exact 16-bit two's complement checksum matching UnCmDrv1.dll (0x04E6C3..0x04E6D9): ~(sum % 0x10000) + 1
            int crc = (~(sum % 0x10000) + 1) & 0xFFFF;
            return (ushort)crc;
        }

        private string ResolveExpectedPlcName()
        {
            try
            {
                string udbPath = @"C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\Data\DB\Current_OPLC.udb";
                if (File.Exists(udbPath))
                {
                    byte[] raw;
                    using (FileStream fs = new FileStream(udbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        raw = new byte[fs.Length];
                        fs.Read(raw, 0, raw.Length);
                    }

                    string text = Encoding.ASCII.GetString(raw);

                    // Priority 1: Match 127.0.0.1 or localhost entry targeting port 20256 in ProjectTable ID=86 (ePTR_EthernetSettings)
                    // Format stored by VisiLogic: "<index>\t<ip>\t<plcName>\t20256\t"
                    Match mLocal = Regex.Match(text, @"(?:127\.0\.0\.1|localhost)\t([^\t\r\n\x00]*)\t20256");
                    if (mLocal.Success)
                    {
                        return mLocal.Groups[1].Value;
                    }

                    // Priority 2: Match any IPv4 entry targeting port 20256
                    Match mAny = Regex.Match(text, @"\d+\.\d+\.\d+\.\d+\t([^\t\r\n\x00]*)\t20256");
                    if (mAny.Success)
                    {
                        return mAny.Groups[1].Value;
                    }
                }
            }
            catch (Exception ex)
            {
                if (plc != null)
                {
                    plc.Log("Warning reading expected PLC Name from Current_OPLC.udb: " + ex.Message);
                }
            }

            return "virtual";
        }

        private void TriggerProjectAutoReload()
        {
            try
            {
                // Check if VisiLogic registered the currently compiled project in OPLC.ini
                string iniPath = @"C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\Data\DB\OPLC.ini";
                string projectPath = null;

                if (File.Exists(iniPath))
                {
                    string[] lines = File.ReadAllLines(iniPath);
                    bool inSec3000 = false;
                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("[3000]"))
                        {
                            inSec3000 = true;
                            continue;
                        }
                        if (inSec3000 && trimmed.StartsWith("["))
                        {
                            break;
                        }
                        if (inSec3000 && trimmed.StartsWith("3001="))
                        {
                            string path = trimmed.Substring(5).Trim();
                            if (File.Exists(path))
                            {
                                projectPath = path;
                                break;
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(projectPath) && OnProjectDownloaded != null)
                {
                    plc.Log("PCOM: Automatically loading downloaded project into PLC engine: " + projectPath);
                    OnProjectDownloaded(projectPath);
                }
            }
            catch (Exception ex)
            {
                plc.Log("Error auto-reloading project after Download: " + ex.Message);
            }
        }
    }
}
