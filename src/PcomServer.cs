using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
                    byte[] buffer = new byte[8192];
                    while (isRunning && client.Connected)
                    {
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break;

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

            // 1. Read System Integers (GS)
            if (cmdBody.StartsWith("GS"))
            {
                // Format: GS<Address:4 hex><Length:2 hex>
                // e.g. GS013301 -> Read 1 SI register at 0x0133 (SI 307 - OPLC readiness status)
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
                    sbData.Append(val.ToString("X4"));
                }

                plc.Log(string.Format("PCOM ASCII GS: Read SI[{0}..{1}] = {2}", addr, addr + len - 1, sbData.ToString()));
                return FormatPcomAsciiResponse(unitId, "GS" + sbData.ToString());
            }

            // 2. Controller Identification (ID, _ID, UG)
            else if (cmdBody.StartsWith("ID") || cmdBody.StartsWith("_ID"))
            {
                // Standard OPLC ID response, e.g. V350-35-RA22,4.04(12) or V570-57-T20B,4.04(12)
                string payload = (plc != null && plc.Profile != null) ? plc.Profile.GetPcomIdString() : "V350-35-RA22,4.04(12)";
                return FormatPcomAsciiResponse(unitId, "ID" + payload);
            }
            else if (cmdBody.StartsWith("UG"))
            {
                // Get Unit ID response
                return FormatPcomAsciiResponse(unitId, "UG" + unitId);
            }

            // 3. OPLC Firmware Version (GF)
            else if (cmdBody.StartsWith("GF"))
            {
                string payload = "OS:4.04(12),BOOT:2.02";
                return FormatPcomAsciiResponse(unitId, "GF" + payload);
            }

            // 4. CPU Control: STOP (CCS, ST)
            else if (cmdBody.StartsWith("CCS") || cmdBody.StartsWith("ST"))
            {
                plc.Stop();
                plc.Log("PCOM: PLC engine stopped by VisiLogic request (STOP mode before Download).");
                string cmdCode = cmdBody.StartsWith("CCS") ? "CCS" : "ST";
                return FormatPcomAsciiResponse(unitId, cmdCode + "00");
            }

            // 5. CPU Control: RUN (CCR, RN)
            else if (cmdBody.StartsWith("CCR") || cmdBody.StartsWith("RN"))
            {
                plc.Start();
                plc.Log("PCOM: PLC engine started by VisiLogic request (RUN mode after Download).");
                TriggerProjectAutoReload();
                string cmdCode = cmdBody.StartsWith("CCR") ? "CCR" : "RN";
                return FormatPcomAsciiResponse(unitId, cmdCode + "00");
            }

            // 6. CPU Reset (RS, CCI, CCE, CCB)
            else if (cmdBody.StartsWith("RS") || cmdBody.StartsWith("CCI") || cmdBody.StartsWith("CCE") || cmdBody.StartsWith("CCB"))
            {
                plc.Reset();
                string op = cmdBody.Length >= 3 ? cmdBody.Substring(0, 3) : cmdBody.Substring(0, 2);
                return FormatPcomAsciiResponse(unitId, op + "00");
            }

            // 7. Read Memory Integers MI (RMI, RW, RZI)
            else if (cmdBody.StartsWith("RMI") || cmdBody.StartsWith("RW") || cmdBody.StartsWith("RZI"))
            {
                string op = cmdBody.StartsWith("RMI") ? "RMI" : (cmdBody.StartsWith("RW") ? "RW" : "RZI");
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
                    sb.Append(val.ToString("X4"));
                }
                return FormatPcomAsciiResponse(unitId, op + sb.ToString());
            }

            // 8. Read Memory Bits MB (RMB, RB, RZB)
            else if (cmdBody.StartsWith("RMB") || cmdBody.StartsWith("RB") || cmdBody.StartsWith("RZB"))
            {
                string op = cmdBody.StartsWith("RMB") ? "RMB" : (cmdBody.StartsWith("RB") ? "RB" : "RZB");
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
                return FormatPcomAsciiResponse(unitId, op + sb.ToString());
            }

            // 9. Write Operands (SZI, SZB, SW, SB, WMI, WMB)
            else if (cmdBody.StartsWith("SZI") || cmdBody.StartsWith("SZB") || cmdBody.StartsWith("SW") ||
                     cmdBody.StartsWith("SB") || cmdBody.StartsWith("WMI") || cmdBody.StartsWith("WMB"))
            {
                string op = cmdBody.Length >= 3 ? cmdBody.Substring(0, 3) : cmdBody.Substring(0, 2);
                return FormatPcomAsciiResponse(unitId, op + "00");
            }

            // 10. Fallback: Return valid PCOM ASCII response with success code 00
            else
            {
                string op = cmdBody.Length >= 2 ? cmdBody.Substring(0, Math.Min(3, cmdBody.Length)) : "OK";
                return FormatPcomAsciiResponse(unitId, op + "00");
            }
        }

        private string FormatPcomAsciiResponse(string unitId, string data)
        {
            // PCOM ASCII response format: /A + UnitID + Data + Checksum (2 hex uppercase) + \r
            // Checksum is the sum of ASCII character codes after '/' modulo 256
            string payload = "A" + unitId + data;

            int sum = 0;
            for (int i = 0; i < payload.Length; i++)
            {
                sum += (int)payload[i];
            }
            int chk = sum & 0xFF;

            return "/" + payload + chk.ToString("X2") + "\r";
        }

        private byte[] ProcessPcomBinaryCommand(byte[] data, int offset, int length)
        {
            // PCOM Binary starts with /_OPLC (6 bytes: 0x2F, 0x5F, 0x4F, 0x50, 0x4C, 0x43)
            // Header size: 24 bytes
            // Byte 6: Unit ID
            // Bytes 7..11: Sub-ID / Reserved (e.g. FE 01 01 00 00)
            // Byte 12: Opcode (e.g. 0x41 = Write Memory, 0x1A = Flash Buffer, 0x16 = Translate Index)
            // Byte 13: Sub-opcode / Reserved
            // Bytes 14..19: Details (6 bytes, e.g. memory address)
            // Bytes 20..21: Command Data Length (uint16 LE)
            // Bytes 22..23: Header CRC (uint16 LE)
            // Bytes 24..: Payload Data
            // Last 3 bytes: Data CRC (2 bytes LE) + '\' (0x5C)
            if (length < 24) return null;

            byte unitId = data[offset + 6];
            byte opcode = data[offset + 12];
            ushort dataLen = (ushort)(data[offset + 20] | (data[offset + 21] << 8));

            byte[] details = new byte[6];
            Array.Copy(data, offset + 14, details, 0, 6);

            plc.Log(string.Format("PCOM Binary REQ: Opcode=0x{0:X2}, DataLen={1}", opcode, dataLen));

            byte[] respPayload = null;

            switch (opcode)
            {
                case 0x41: // Write Memory (Program / Resource block write)
                    plc.Log(string.Format("PCOM Binary: OPLC program memory block write (0x41, {0} bytes)", dataLen));
                    break;

                case 0x1A: // Flash Memory Buffer (Commit downloaded program to FLASH)
                    plc.Log("PCOM Binary: FLASH commit command (0x1A) - program successfully downloaded by VisiLogic!");
                    TriggerProjectAutoReload();
                    break;

                case 0x16: // Translate Index to Address
                    plc.Log("PCOM Binary: Translate OPLC memory index (0x16)");
                    break;

                case 0x02: // Check Password
                    plc.Log("PCOM Binary: OPLC access password check (0x02) -> OK");
                    break;

                case 0x0C: // Get PLC Name
                    plc.Log("PCOM Binary: Get PLC Name request (0x0C)");
                    respPayload = Encoding.ASCII.GetBytes("V350-35-RA22\0");
                    break;

                case 0x10: // Where Resource
                    plc.Log("PCOM Binary: Locate OPLC resource (0x10)");
                    break;

                case 0x01: // Read Memory
                    plc.Log(string.Format("PCOM Binary: Read OPLC memory (0x01, len={0})", dataLen));
                    respPayload = new byte[Math.Max(1, (int)dataLen)];
                    break;

                case 0x4D: // Read Operand
                    plc.Log("PCOM Binary: Read OPLC operand (0x4D)");
                    respPayload = new byte[4];
                    break;

                default:
                    plc.Log(string.Format("PCOM Binary: Unhandled opcode 0x{0:X2} -> Returning ACK success", opcode));
                    break;
            }

            return BuildBinaryResponse(unitId, opcode, respPayload, details);
        }

        private byte[] BuildBinaryResponse(byte unitId, byte reqOpcode, byte[] respData = null, byte[] details = null)
        {
            int dataLen = (respData != null) ? respData.Length : 0;
            byte[] resp = new byte[24 + dataLen + 3];

            // 1. Magic header: /_OPLC (6 bytes)
            byte[] magic = Encoding.ASCII.GetBytes("/_OPLC");
            Array.Copy(magic, 0, resp, 0, 6);

            // 2. Unit ID and sub-header
            resp[6] = unitId;
            resp[7] = 0xFE;
            resp[8] = 0x01;
            resp[9] = 0x01;
            resp[10] = 0x00;
            resp[11] = 0x00;

            // 3. Response Opcode (MSB = 1)
            resp[12] = (byte)(reqOpcode | 0x80);
            resp[13] = 0x00; // Status: 0x00 = Success (OK)

            // 4. Details
            if (details != null && details.Length >= 6)
            {
                Array.Copy(details, 0, resp, 14, 6);
            }
            else
            {
                for (int i = 14; i < 20; i++) resp[i] = 0;
            }

            // 5. Response data length
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
            int crc = ((~(sum % 0x10000) + 1) % 0xFFFF + 1) & 0xFFFF;
            return (ushort)crc;
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
