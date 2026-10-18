using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.IO.Compression;

namespace UnitronicsV350Simulator
{
    public class VlpParser
    {
        public static LoadedProject LoadProjectFile(string filePath, Action<string> log)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException("Project file not found: " + filePath);
            }

            string ext = Path.GetExtension(filePath).ToLower();
            if (ext == ".vlp")
            {
                return LoadFromVlpZip(filePath, log);
            }
            else if (ext == ".udb" || ext == ".vlx")
            {
                return LoadFromUdbDatabase(filePath, log);
            }
            else
            {
                throw new NotSupportedException("Unsupported file format: " + ext + ". Expected .vlp, .vlx, or .udb.");
            }
        }

        private static LoadedProject LoadFromVlpZip(string vlpPath, Action<string> log)
        {
            // Check magic bytes: VisiLogic .vlp can be a ZIP archive (PK\x03\x04) or a raw Jet 4.0 database (\x00\x01\x00\x00)
            byte[] header = new byte[4];
            using (FileStream fs = new FileStream(vlpPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length >= 4)
                {
                    fs.Read(header, 0, 4);
                }
            }
            if (header[0] == 0x00 && header[1] == 0x01 && header[2] == 0x00 && header[3] == 0x00)
            {
                var directProj = LoadFromUdbDatabase(vlpPath, log);
                directProj.ProjectName = Path.GetFileNameWithoutExtension(vlpPath);
                directProj.FilePath = vlpPath;
                return directProj;
            }

            log("Extracting VisiLogic project container: " + Path.GetFileName(vlpPath));
            string tempDir = Path.Combine(Path.GetTempPath(), "V350_VLP_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                using (FileStream fs = new FileStream(vlpPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    archive.ExtractToDirectory(tempDir);
                }
                string udbPath = Path.Combine(tempDir, "Current_OPLC.udb");
                if (!File.Exists(udbPath))
                {
                    // Search for any .udb file inside the extracted archive
                    var udbs = Directory.GetFiles(tempDir, "*.udb", SearchOption.AllDirectories);
                    if (udbs.Length > 0)
                    {
                        udbPath = udbs[0];
                    }
                    else
                    {
                        throw new FileNotFoundException("Current_OPLC.udb database was not found inside the .vlp archive!");
                    }
                }

                var project = LoadFromUdbDatabase(udbPath, log);
                project.ProjectName = Path.GetFileNameWithoutExtension(vlpPath);
                project.FilePath = vlpPath;
                return project;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch { }
            }
        }

        private static LoadedProject LoadFromUdbDatabase(string udbPath, Action<string> log)
        {
            log("Connecting to Jet 4.0 database: " + Path.GetFileName(udbPath));
            var project = new LoadedProject();
            project.ProjectName = Path.GetFileNameWithoutExtension(udbPath);
            project.FilePath = udbPath;

            // If loading directly from VisiLogic's live Current_OPLC.udb, resolve the human-readable project name from OPLC.ini
            string resolvedVlp = null;
            if (string.Equals(Path.GetFileName(udbPath), "Current_OPLC.udb", StringComparison.OrdinalIgnoreCase))
            {
                string udbDir = Path.GetDirectoryName(udbPath) ?? "";
                resolvedVlp = TryResolveProjectPathFromIni(Path.Combine(udbDir, "OPLC.ini"));
                if (string.IsNullOrEmpty(resolvedVlp) && !string.IsNullOrEmpty(udbDir))
                {
                    try
                    {
                        resolvedVlp = TryResolveProjectPathFromIni(Path.GetFullPath(Path.Combine(udbDir, "..", "Data", "OPLC.ini")));
                    }
                    catch { }
                }
                if (string.IsNullOrEmpty(resolvedVlp))
                {
                    resolvedVlp = TryResolveProjectPathFromIni(@"C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\Data\DB\OPLC.ini");
                }
                if (!string.IsNullOrEmpty(resolvedVlp))
                {
                    project.ProjectName = Path.GetFileNameWithoutExtension(resolvedVlp);
                }
            }

            // Copy the .udb file to an isolated temp path using FileShare.ReadWrite so Jet 4.0 OLEDB never fails
            // with file-locking (.ldb) or Program Files directory permission errors while VisiLogic is open.
            string tempUdbDir = Path.Combine(Path.GetTempPath(), "V350_UDB_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempUdbDir);
            string tempUdbPath = Path.Combine(tempUdbDir, "ProjectCopy.udb");

            try
            {
                using (FileStream srcStream = new FileStream(udbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (FileStream dstStream = new FileStream(tempUdbPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    srcStream.CopyTo(dstStream);
                }

                string connStr = "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + tempUdbPath + ";Jet OLEDB:Database Password=UniOpenSesame;";

                using (OleDbConnection conn = new OleDbConnection(connStr))
                {
                    conn.Open();
                    log("Authenticated successfully with Unitronics Jet 4.0 database.");

                    // 1. Read hardware configuration from HW_Elements
                    ReadHardwareConfig(conn, project, log);

                    // 2. Read ladder subroutines from LdrFunctions
                    ReadSubroutines(conn, project, log);

                    // 3. Read ladder rungs from LdrStrips
                    ReadLadderStrips(conn, project, log);

                    // 4. Read ladder elements from LdrElements and bindings from LdrElmsOprs
                    ReadLadderElements(conn, project, log);

                    // 5. Read HMI displays from Pages and elements from HMI_Elements
                    ReadHmiPagesAndElements(conn, project, log);

                    // 6. Read operand symbol descriptions from LdrOperands / tblWatches
                    ReadOperandNames(conn, project, log);
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempUdbDir))
                    {
                        Directory.Delete(tempUdbDir, true);
                    }
                }
                catch { }
            }

            if (project.Subroutines.Count <= 1 && project.Pages.Count <= 1 && !string.IsNullOrEmpty(resolvedVlp) && File.Exists(resolvedVlp))
            {
                log("Current_OPLC.udb is empty; falling back to active .vlp archive: " + resolvedVlp);
                return LoadFromVlpZip(resolvedVlp, log);
            }

            log(string.Format("Project loaded: {0} subroutines, {1} HMI displays, {2} operand descriptions.",
                project.Subroutines.Count, project.Pages.Count, project.OperandDescriptions.Count));

            return project;
        }

        public static string TryResolveProjectPathFromIni(string iniPath)
        {
            if (string.IsNullOrEmpty(iniPath) || !File.Exists(iniPath)) return null;

            System.Text.Encoding[] encodings = new System.Text.Encoding[]
            {
                System.Text.Encoding.Default,
                System.Text.Encoding.UTF8
            };

            foreach (var enc in encodings)
            {
                try
                {
                    string[] lines = File.ReadAllLines(iniPath, enc);
                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("3001=", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("LastLoadedFileName=", StringComparison.OrdinalIgnoreCase))
                        {
                            int eqIdx = trimmed.IndexOf('=');
                            if (eqIdx > 0)
                            {
                                string candidate = trimmed.Substring(eqIdx + 1).Trim().Trim('"');
                                int driveColon = candidate.IndexOf(":\\");
                                if (driveColon > 1)
                                {
                                    candidate = candidate.Substring(driveColon - 1).Trim();
                                }
                                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                                {
                                    return candidate;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        public static string ResolveActiveVisiLogicProjectPath()
        {
            string progFilesDb = @"C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\Data\DB";
            string virtualStoreDb = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"VirtualStore\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\Data\DB");

            string iniPath = Path.Combine(progFilesDb, "OPLC.ini");
            string vsIniPath = Path.Combine(virtualStoreDb, "OPLC.ini");
            if (File.Exists(vsIniPath) && (!File.Exists(iniPath) || File.GetLastWriteTimeUtc(vsIniPath) >= File.GetLastWriteTimeUtc(iniPath)))
            {
                iniPath = vsIniPath;
            }

            string udbPath = Path.Combine(progFilesDb, "Current_OPLC.udb");
            string vsUdbPath = Path.Combine(virtualStoreDb, "Current_OPLC.udb");
            if (File.Exists(vsUdbPath) && (!File.Exists(udbPath) || File.GetLastWriteTimeUtc(vsUdbPath) >= File.GetLastWriteTimeUtc(udbPath)))
            {
                udbPath = vsUdbPath;
            }

            string vlpPath = TryResolveProjectPathFromIni(iniPath);
            if (string.IsNullOrEmpty(vlpPath) && iniPath != Path.Combine(progFilesDb, "OPLC.ini"))
            {
                vlpPath = TryResolveProjectPathFromIni(Path.Combine(progFilesDb, "OPLC.ini"));
            }

            string projectPath = vlpPath;

            if (File.Exists(udbPath))
            {
                if (string.IsNullOrEmpty(vlpPath) ||
                    File.GetLastWriteTimeUtc(udbPath) > File.GetLastWriteTimeUtc(vlpPath))
                {
                    projectPath = udbPath;
                }
            }

            return projectPath;
        }

        private static void ReadHardwareConfig(OleDbConnection conn, LoadedProject project, Action<string> log)
        {
            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT ElementID, ElementModel, ElementPosition, RailIndex FROM HW_Elements";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            string model = r["ElementModel"] != DBNull.Value ? r["ElementModel"].ToString() : "";
                            int pos = r["ElementPosition"] != DBNull.Value ? Convert.ToInt32(r["ElementPosition"]) : -1;
                            if (pos == -1 && !string.IsNullOrEmpty(model))
                            {
                                project.PlcModel = model;
                            }
                            else if (!string.IsNullOrEmpty(model))
                            {
                                project.Modules.Add(string.Format("Slot {0}: {1}", pos, model));
                            }
                        }
                    }
                }
                log("Detected hardware configuration: CPU " + project.PlcModel + ", modules: " + string.Join(", ", project.Modules.ToArray()));
            }
            catch (Exception ex)
            {
                log("HW_Elements info: " + ex.Message);
            }
        }

        private static void ReadSubroutines(OleDbConnection conn, LoadedProject project, Action<string> log)
        {
            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT FuncID, FuncName FROM LdrFunctions ORDER BY FuncID";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            int id = Convert.ToInt32(r["FuncID"]);
                            string name = r["FuncName"].ToString();
                            project.Subroutines[id] = new Subroutine { FuncId = id, Name = name };
                        }
                    }
                }
                log("Loaded subroutine list (" + project.Subroutines.Count + ").");
            }
            catch (Exception ex)
            {
                log("Error reading LdrFunctions: " + ex.Message);
            }
        }

        private static void ReadLadderStrips(OleDbConnection conn, LoadedProject project, Action<string> log)
        {
            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT StripID, FuncID, SeqNum, Comment, IsReturnStrip FROM LdrStrips ORDER BY FuncID, SeqNum";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            int stripId = Convert.ToInt32(r["StripID"]);
                            int funcId = Convert.ToInt32(r["FuncID"]);
                            int seq = Convert.ToInt32(r["SeqNum"]);
                            string comment = r["Comment"] != DBNull.Value ? r["Comment"].ToString() : "";
                            bool isRet = r["IsReturnStrip"] != DBNull.Value && Convert.ToInt32(r["IsReturnStrip"]) != 0;

                            if (project.Subroutines.ContainsKey(funcId))
                            {
                                project.Subroutines[funcId].Rungs.Add(new LadderRung
                                {
                                    StripId = stripId,
                                    FuncId = funcId,
                                    SeqNum = seq,
                                    Comment = comment,
                                    IsReturnStrip = isRet
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log("Error reading LdrStrips: " + ex.Message);
            }
        }

        private static void ReadLadderElements(OleDbConnection conn, LoadedProject project, Action<string> log)
        {
            try
            {
                // 0. Preload direct constants (OprndType 15, 34, 35, 37, 38), Timer Presets (OprndType 11),
                // and Power-Up register values from LdrOperands
                var constValues = new Dictionary<long, int>();
                try
                {
                    using (var cmdConst = conn.CreateCommand())
                    {
                        cmdConst.CommandText = "SELECT OprndType, OprndAddress, OprndPowerUp, OprndEnablePowerUp FROM LdrOperands";
                        using (var rc = cmdConst.ExecuteReader())
                        {
                            while (rc.Read())
                            {
                                int opType = rc["OprndType"] != DBNull.Value ? Convert.ToInt32(rc["OprndType"]) : 0;
                                int opAddr = rc["OprndAddress"] != DBNull.Value ? Convert.ToInt32(rc["OprndAddress"]) : 0;
                                string rawPwr = rc["OprndPowerUp"] != DBNull.Value ? rc["OprndPowerUp"].ToString().Replace("\0", "").Trim() : "";
                                bool enPwr = rc["OprndEnablePowerUp"] != DBNull.Value && Convert.ToInt32(rc["OprndEnablePowerUp"]) != 0;

                                int parsedVal = 0;
                                double parsedDbl = 0;
                                bool hasVal = int.TryParse(rawPwr, out parsedVal);
                                if (!hasVal && double.TryParse(rawPwr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out parsedDbl))
                                {
                                    parsedVal = (int)parsedDbl;
                                    hasVal = true;
                                }

                                if (opType == 15 || opType == 34 || opType == 35 || opType == 37 || opType == 38)
                                {
                                    long constKey = (((long)opType) << 32) | ((uint)opAddr);
                                    constValues[constKey] = hasVal ? parsedVal : 0;
                                }
                                else if (opType == 11 && hasVal && parsedVal > 0)
                                {
                                    // Timer preset in LdrOperands is stored in 10 ms units (e.g. 500 = 5000 ms = 5.0 s)
                                    project.TimerPresets[opAddr] = parsedVal * 10;
                                }
                                else if (enPwr && hasVal)
                                {
                                    string tName = MapOperandTypeNumber(opType);
                                    if (!string.IsNullOrEmpty(tName) && tName != "CONST")
                                    {
                                        project.PowerUpValues[tName + opAddr] = parsedVal;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception exConst)
                {
                    log("LdrOperands constants/presets info: " + exConst.Message);
                }

                // Temporary lookup map StripId -> LadderRung
                var rungMap = new Dictionary<int, LadderRung>();
                foreach (var sub in project.Subroutines.Values)
                {
                    foreach (var rung in sub.Rungs)
                    {
                        rungMap[rung.StripId] = rung;
                    }
                }

                // Read ladder elements ordered by StripID, Ys, Xs
                var elementMap = new Dictionary<int, LadderElement>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT ElmntID, StripID, ElmntType, Xs, Ys, Dx, Dy, MoreParamsStr, MoreParamsLng FROM LdrElements ORDER BY StripID, Ys, Xs";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            int elId = Convert.ToInt32(r["ElmntID"]);
                            int stripId = Convert.ToInt32(r["StripID"]);
                            int type = Convert.ToInt32(r["ElmntType"]);
                            int xs = Convert.ToInt32(r["Xs"]);
                            int ys = Convert.ToInt32(r["Ys"]);
                            int dx = Convert.ToInt32(r["Dx"]);
                            int dy = Convert.ToInt32(r["Dy"]);
                            string paramStr = r["MoreParamsStr"] != DBNull.Value ? r["MoreParamsStr"].ToString() : "";
                            int paramLng = r["MoreParamsLng"] != DBNull.Value ? Convert.ToInt32(r["MoreParamsLng"]) : 0;

                            var elm = new LadderElement
                            {
                                ElementId = elId,
                                ElementType = type,
                                Xs = xs,
                                Ys = ys,
                                Dx = dx,
                                Dy = dy,
                                MoreParamsStr = paramStr,
                                MoreParamsLng = paramLng
                            };

                            elementMap[elId] = elm;
                            if (rungMap.ContainsKey(stripId))
                            {
                                rungMap[stripId].Elements.Add(elm);
                            }
                        }
                    }
                }

                // Read operand bindings from LdrElmsOprs
                // Ordering by OprndOutIdx, OprndInIdx guarantees that input operands (OprndOutIdx=0, OprndInIdx=1..N)
                // precede output operands (OprndOutIdx=1..M, OprndInIdx=0).
                using (var cmd2 = conn.CreateCommand())
                {
                    cmd2.CommandText = "SELECT ElmntID, Elmnt_Oprnd_Type, Elmnt_Oprnd_Adrs FROM LdrElmsOprs ORDER BY ElmntID, OprndOutIdx, OprndInIdx";
                    using (var r2 = cmd2.ExecuteReader())
                    {
                        while (r2.Read())
                        {
                            int elId = Convert.ToInt32(r2["ElmntID"]);
                            int opTypeNum = Convert.ToInt32(r2["Elmnt_Oprnd_Type"]);
                            int opAddr = Convert.ToInt32(r2["Elmnt_Oprnd_Adrs"]);

                            string opTypeName = MapOperandTypeNumber(opTypeNum);
                            int constVal = opAddr;

                            if (opTypeNum == 15 || opTypeNum == 34 || opTypeNum == 35 || opTypeNum == 37 || opTypeNum == 38)
                            {
                                opTypeName = "CONST";
                                long constKey = (((long)opTypeNum) << 32) | ((uint)opAddr);
                                int lookedUp;
                                if (constValues.TryGetValue(constKey, out lookedUp))
                                {
                                    constVal = lookedUp;
                                }
                            }
                            else if (opTypeNum == 30 || opTypeNum == 31)
                            {
                                // 30 = JHP (Jump to HMI Page ID), 31 = CS (Call Subroutine ID)
                                opTypeName = "CONST";
                                constVal = opAddr;
                            }

                            if (elementMap.ContainsKey(elId))
                            {
                                elementMap[elId].Operands.Add(new OperandRef
                                {
                                    Type = opTypeName,
                                    Address = opAddr,
                                    ConstValue = constVal
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log("Error reading LdrElements/LdrElmsOprs: " + ex.Message);
            }
        }

        private static void ReadHmiPagesAndElements(OleDbConnection conn, LoadedProject project, Action<string> log)
        {
            try
            {
                // Read HMI pages from Pages table
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id, Description, ColorBack FROM Pages ORDER BY Id";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            int id = Convert.ToInt32(r["Id"]);
                            string desc = r["Description"] != DBNull.Value ? r["Description"].ToString() : ("Page " + id);
                            int cBack = r["ColorBack"] != DBNull.Value ? Convert.ToInt32(r["ColorBack"]) : 0;

                            project.Pages[id] = new HmiPageDef
                            {
                                Id = id,
                                Name = desc,
                                ColorBack = cBack
                            };
                        }
                    }
                }

                // Read HMI elements from HMI_Elements table
                using (var cmd2 = conn.CreateCommand())
                {
                    cmd2.CommandText = @"SELECT ID, DisplayID, Type, VarType, Text, Xs, Ys, Dx, Dy, 
                                        ColorBack, ColorFore, VarOprndType, VarOprndAddr, TouchOprndAddr, 
                                        VisibleOpAddr, VarKeyPadEntry FROM HMI_Elements ORDER BY DisplayID, ID";
                    using (var r2 = cmd2.ExecuteReader())
                    {
                        while (r2.Read())
                        {
                            int dispId = Convert.ToInt32(r2["DisplayID"]);
                            if (!project.Pages.ContainsKey(dispId)) continue;

                            var hElm = new HmiElementDef
                            {
                                Id = Convert.ToInt32(r2["ID"]),
                                DisplayId = dispId,
                                Type = r2["Type"] != DBNull.Value ? Convert.ToInt32(r2["Type"]) : 1,
                                VarType = r2["VarType"] != DBNull.Value ? Convert.ToInt32(r2["VarType"]) : 0,
                                Text = r2["Text"] != DBNull.Value ? r2["Text"].ToString() : "",
                                Xs = r2["Xs"] != DBNull.Value ? Convert.ToInt32(r2["Xs"]) : 0,
                                Ys = r2["Ys"] != DBNull.Value ? Convert.ToInt32(r2["Ys"]) : 0,
                                Dx = r2["Dx"] != DBNull.Value ? Convert.ToInt32(r2["Dx"]) : 50,
                                Dy = r2["Dy"] != DBNull.Value ? Convert.ToInt32(r2["Dy"]) : 20,
                                ColorBack = r2["ColorBack"] != DBNull.Value ? Convert.ToInt32(r2["ColorBack"]) : 0,
                                ColorFore = r2["ColorFore"] != DBNull.Value ? Convert.ToInt32(r2["ColorFore"]) : 0,
                                VarOprndType = r2["VarOprndType"] != DBNull.Value ? Convert.ToInt32(r2["VarOprndType"]) : 0,
                                VarOprndAddr = r2["VarOprndAddr"] != DBNull.Value ? Convert.ToInt32(r2["VarOprndAddr"]) : 0,
                                TouchOprndAddr = r2["TouchOprndAddr"] != DBNull.Value ? Convert.ToInt32(r2["TouchOprndAddr"]) : -1,
                                VisibleOpAddr = r2["VisibleOpAddr"] != DBNull.Value ? Convert.ToInt32(r2["VisibleOpAddr"]) : -1,
                                VarKeyPadEntry = r2["VarKeyPadEntry"] != DBNull.Value ? Convert.ToInt32(r2["VarKeyPadEntry"]) : 0
                            };

                            project.Pages[dispId].Elements.Add(hElm);
                        }
                    }
                }
                log("Loaded HMI displays (" + project.Pages.Count + " pages).");
            }
            catch (Exception ex)
            {
                log("Error reading HMI tables: " + ex.Message);
            }
        }

        private static void ReadOperandNames(OleDbConnection conn, LoadedProject project, Action<string> log)
        {
            int count = 0;
            // 1. Primary operand description table in VisiLogic Jet 4.0 DB: LdrOperands
            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT OprndType, OprndAddress, OprndDescription FROM LdrOperands WHERE OprndDescription IS NOT NULL";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            int type = Convert.ToInt32(r["OprndType"]);
                            int addr = Convert.ToInt32(r["OprndAddress"]);
                            string rawDesc = r["OprndDescription"] != DBNull.Value ? r["OprndDescription"].ToString() : "";
                            string desc = rawDesc.Replace("\0", "").Trim();
                            if (!string.IsNullOrEmpty(desc))
                            {
                                string typeStr = MapOperandTypeNumber(type);
                                if (!string.IsNullOrEmpty(typeStr) && typeStr != "CONST")
                                {
                                    string key = string.Format("{0}{1}", typeStr, addr);
                                    project.OperandDescriptions[key] = desc;
                                    count++;
                                }
                            }
                        }
                    }
                }
                log(string.Format("Loaded {0} operand descriptions from LdrOperands (MB, MI, ML, I, O, T, SB, SI).", count));
            }
            catch (Exception ex)
            {
                log("LdrOperands info: " + ex.Message);
            }

            // 2. Fallback/supplement from tblWatches (if present in the project database)
            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Name, Description, Address, Type FROM tblWatches";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            string name = r["Name"] != DBNull.Value ? r["Name"].ToString().Replace("\0", "").Trim() : "";
                            int addr = r["Address"] != DBNull.Value ? Convert.ToInt32(r["Address"]) : 0;
                            int type = r["Type"] != DBNull.Value ? Convert.ToInt32(r["Type"]) : 0;
                            string typeStr = MapOperandTypeNumber(type);
                            string key = string.Format("{0}{1}", typeStr, addr);
                            if (!string.IsNullOrEmpty(name) && !project.OperandDescriptions.ContainsKey(key))
                            {
                                project.OperandDescriptions[key] = name;
                            }
                        }
                    }
                }
            }
            catch { }

            // 3. Optional supplement from adjacent operands.csv file if present
            TryLoadCsvOperands(project);
        }

        private static void TryLoadCsvOperands(LoadedProject project)
        {
            if (string.IsNullOrEmpty(project.FilePath)) return;
            string dir = Path.GetDirectoryName(project.FilePath) ?? "";
            string csvPath = Path.Combine(dir, "operands.csv");
            if (File.Exists(csvPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(csvPath);
                    if (lines.Length > 1)
                    {
                        string[] headers = lines[0].Split(',');
                        for (int i = 1; i < lines.Length; i++)
                        {
                            string[] cols = lines[i].Split(',');
                            if (cols.Length < 2) continue;
                            int addr = 0;
                            if (int.TryParse(cols[0], out addr))
                            {
                                for (int c = 1; c < cols.Length && c < headers.Length; c++)
                                {
                                    string val = cols[c].Trim();
                                    if (!string.IsNullOrEmpty(val))
                                    {
                                        string key = headers[c] + addr;
                                        if (!project.OperandDescriptions.ContainsKey(key))
                                        {
                                            project.OperandDescriptions[key] = val;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        public static string MapOperandTypeNumber(int typeNum)
        {
            switch (typeNum)
            {
                case 1: return "MB";
                case 2: return "SB";
                case 3: return "MI";
                case 4: return "SI";
                case 5: return "ML";
                case 6: return "SL";
                case 7: return "MF";
                case 9: return "I";
                case 10: return "O";
                case 11: return "T";
                case 12: return "C";
                case 16: return "DW";
                case 36: return "SDW";
                default: return "CONST";
            }
        }
    }
}
