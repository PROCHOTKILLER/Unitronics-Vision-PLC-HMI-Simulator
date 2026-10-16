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
                throw new FileNotFoundException("Nie znaleziono pliku projektu: " + filePath);
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
                throw new NotSupportedException("Nieobsługiwany format pliku: " + ext + ". Wymagany plik .vlp, .vlx lub .udb.");
            }
        }

        private static LoadedProject LoadFromVlpZip(string vlpPath, Action<string> log)
        {
            log("Rozpakowywanie kontenera projektu VisiLogic: " + Path.GetFileName(vlpPath));
            string tempDir = Path.Combine(Path.GetTempPath(), "V350_VLP_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                ZipFile.ExtractToDirectory(vlpPath, tempDir);
                string udbPath = Path.Combine(tempDir, "Current_OPLC.udb");
                if (!File.Exists(udbPath))
                {
                    // Szukamy dowolnego .udb w wypakowanych plikach
                    var udbs = Directory.GetFiles(tempDir, "*.udb", SearchOption.AllDirectories);
                    if (udbs.Length > 0)
                    {
                        udbPath = udbs[0];
                    }
                    else
                    {
                        throw new FileNotFoundException("Wewnątrz archiwum .vlp nie znaleziono bazy Current_OPLC.udb!");
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
            log("Nawiązywanie połączenia z bazą Jet 4.0: " + Path.GetFileName(udbPath));
            var project = new LoadedProject();
            project.ProjectName = Path.GetFileNameWithoutExtension(udbPath);
            project.FilePath = udbPath;

            string connStr = "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + udbPath + ";Jet OLEDB:Database Password=UniOpenSesame;";

            using (OleDbConnection conn = new OleDbConnection(connStr))
            {
                conn.Open();
                log("Pomyślnie uwierzytelniono z hasłem Unitronics (UniOpenSesame).");

                // 1. Odczyt konfiguracji sprzętowej HW_Elements
                ReadHardwareConfig(conn, project, log);

                // 2. Odczyt podprogramów LdrFunctions
                ReadSubroutines(conn, project, log);

                // 3. Odczyt szczebli drabinki LdrStrips
                ReadLadderStrips(conn, project, log);

                // 4. Odczyt elementów drabinki LdrElements i powiązań LdrElmsOprs
                ReadLadderElements(conn, project, log);

                // 5. Odczyt ekranów HMI Pages oraz elementów HMI_Elements
                ReadHmiPagesAndElements(conn, project, log);

                // 6. Odczyt nazw operandów LdrOperands / tblWatches
                ReadOperandNames(conn, project, log);
            }

            log(string.Format("Załadowano projekt: {0} podprogramów, {1} ekranów HMI, {2} opisów zmiennych.",
                project.Subroutines.Count, project.Pages.Count, project.OperandDescriptions.Count));

            return project;
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
                log("Wykryto konfigurację sprzętową: CPU " + project.PlcModel + ", moduły: " + string.Join(", ", project.Modules.ToArray()));
            }
            catch (Exception ex)
            {
                log("Informacja o HW_Elements: " + ex.Message);
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
                log("Załadowano listę podprogramów (" + project.Subroutines.Count + ").");
            }
            catch (Exception ex)
            {
                log("Błąd odczytu LdrFunctions: " + ex.Message);
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
                log("Błąd odczytu LdrStrips: " + ex.Message);
            }
        }

        private static void ReadLadderElements(OleDbConnection conn, LoadedProject project, Action<string> log)
        {
            try
            {
                // Mapa tymczasowa StripId -> LadderRung
                var rungMap = new Dictionary<int, LadderRung>();
                foreach (var sub in project.Subroutines.Values)
                {
                    foreach (var rung in sub.Rungs)
                    {
                        rungMap[rung.StripId] = rung;
                    }
                }

                // Odczyt elementów drabinki
                var elementMap = new Dictionary<int, LadderElement>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT ElmntID, StripID, ElmntType, Xs, Ys, Dx, Dy, MoreParamsStr, MoreParamsLng FROM LdrElements ORDER BY StripID, Xs";
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

                // Odczyt powiązań LdrElmsOprs
                using (var cmd2 = conn.CreateCommand())
                {
                    cmd2.CommandText = "SELECT ElmntID, Elmnt_Oprnd_Type, Elmnt_Oprnd_Adrs FROM LdrElmsOprs ORDER BY ElmntID";
                    using (var r2 = cmd2.ExecuteReader())
                    {
                        while (r2.Read())
                        {
                            int elId = Convert.ToInt32(r2["ElmntID"]);
                            int opTypeNum = Convert.ToInt32(r2["Elmnt_Oprnd_Type"]);
                            int opAddr = Convert.ToInt32(r2["Elmnt_Oprnd_Adrs"]);

                            string opTypeName = MapOperandTypeNumber(opTypeNum);
                            if (elementMap.ContainsKey(elId))
                            {
                                elementMap[elId].Operands.Add(new OperandRef
                                {
                                    Type = opTypeName,
                                    Address = opAddr,
                                    ConstValue = opAddr
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log("Błąd odczytu LdrElements/LdrElmsOprs: " + ex.Message);
            }
        }

        private static void ReadHmiPagesAndElements(OleDbConnection conn, LoadedProject project, Action<string> log)
        {
            try
            {
                // Odczyt stron Pages
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

                // Odczyt elementów HMI_Elements
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
                log("Załadowano ekrany HMI (" + project.Pages.Count + " stron).");
            }
            catch (Exception ex)
            {
                log("Błąd odczytu HMI: " + ex.Message);
            }
        }

        private static void ReadOperandNames(OleDbConnection conn, LoadedProject project, Action<string> log)
        {
            int count = 0;
            // 1. Podstawowe źródło w bazie Jet 4.0 VisiLogic: tabela LdrOperands
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
                log(string.Format("Załadowano {0} opisów zmiennych z tabeli LdrOperands (MB, MI, ML, I, O, T, SB, SI).", count));
            }
            catch (Exception ex)
            {
                log("Informacja o LdrOperands: " + ex.Message);
            }

            // 2. Dopełnienie z tblWatches (jeśli tabela istnieje w projekcie)
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

            // 3. Dopełnienie z pliku CSV
            TryLoadCsvOperands(project);
        }

        private static void TryLoadCsvOperands(LoadedProject project)
        {
            string csvPath = "wygenerowane_pliki_projektu\\Visilogic_operands_complete.csv";
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
                case 16:
                case 37: return "DW";
                case 36: return "SDW";
                default: return "CONST";
            }
        }
    }
}
