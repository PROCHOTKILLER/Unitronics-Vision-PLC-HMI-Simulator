Add-Type -AssemblyName System.IO.Compression.FileSystem

$vlp = (Get-ChildItem -Path "C:\Users\Panasonic\Desktop" -Filter "Komora_9_PLC.vlp" -Recurse | Select-Object -First 1).FullName
$temp = Join-Path $env:TEMP "test_vlp_inspect5"
if (Test-Path $temp) { Remove-Item -Recurse -Force $temp }
[System.IO.Compression.ZipFile]::ExtractToDirectory($vlp, $temp)
$udb = (Get-ChildItem -Path $temp -Filter "*.udb" -Recurse | Select-Object -First 1).FullName

$connStr = "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=$udb;Jet OLEDB:Database Password=UniOpenSesame;"
$conn = New-Object System.Data.OleDb.OleDbConnection($connStr)
$conn.Open()

Write-Host "--- LdrFunctions ---"
$cmdF = $conn.CreateCommand()
$cmdF.CommandText = "SELECT FuncID, FuncName FROM LdrFunctions ORDER BY FuncID"
$daF = New-Object System.Data.OleDb.OleDbDataAdapter($cmdF)
$dtF = New-Object System.Data.DataTable
$daF.Fill($dtF) | Out-Null
$dtF | Format-Table -AutoSize | Out-String | Write-Host

Write-Host "--- All rungs in 08_HMI_LOGIC ---"
$cmdHmiStrips = $conn.CreateCommand()
$cmdHmiStrips.CommandText = "SELECT StripID, FuncID, SeqNum, Comment FROM LdrStrips WHERE FuncID IN (SELECT FuncID FROM LdrFunctions WHERE FuncName LIKE '%HMI%') ORDER BY SeqNum"
$daHmiStrips = New-Object System.Data.OleDb.OleDbDataAdapter($cmdHmiStrips)
$dtHmiStrips = New-Object System.Data.DataTable
$daHmiStrips.Fill($dtHmiStrips) | Out-Null
$dtHmiStrips | Format-Table -AutoSize | Out-String | Write-Host

Write-Host "--- Elements of 08_HMI_LOGIC ---"
$cmdHmiElms = $conn.CreateCommand()
$cmdHmiElms.CommandText = "SELECT e.StripID, e.ElmntID, e.ElmntType, e.MoreParamsLng, o.Elmnt_Oprnd_Type, o.Elmnt_Oprnd_Adrs FROM LdrElements e LEFT JOIN LdrElmsOprs o ON e.ElmntID = o.ElmntID WHERE e.StripID IN (SELECT StripID FROM LdrStrips WHERE FuncID IN (SELECT FuncID FROM LdrFunctions WHERE FuncName LIKE '%HMI%')) ORDER BY e.StripID, e.Xs"
$daHmiElms = New-Object System.Data.OleDb.OleDbDataAdapter($cmdHmiElms)
$dtHmiElms = New-Object System.Data.DataTable
$daHmiElms.Fill($dtHmiElms) | Out-Null
$dtHmiElms | Format-Table -AutoSize | Out-String | Write-Host

Write-Host "--- ALL HMI_Operands for 879..883 ---"
$cmdOp = $conn.CreateCommand()
$cmdOp.CommandText = "SELECT * FROM HMI_Operands WHERE ElementID IN (879, 880, 881, 882, 883, 1082, 1109)"
$daOp = New-Object System.Data.OleDb.OleDbDataAdapter($cmdOp)
$dtOp = New-Object System.Data.DataTable
$daOp.Fill($dtOp) | Out-Null
$dtOp | Format-Table -AutoSize | Out-String | Write-Host

Write-Host "--- HMI_Operands TABLE ---"
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT TOP 30 * FROM HMI_Operands"
$da2 = New-Object System.Data.OleDb.OleDbDataAdapter($cmd2)
$dt2 = New-Object System.Data.DataTable
$da2.Fill($dt2) | Out-Null
$dt2 | Format-Table -AutoSize | Out-String | Write-Host

Write-Host "--- LdrOperands for MB 211..215 ---"
$cmdOpDesc = $conn.CreateCommand()
$cmdOpDesc.CommandText = "SELECT OprndType, OprndAddress, OprndDescription FROM LdrOperands WHERE OprndType = 1 AND OprndAddress IN (211, 212, 213, 214, 215)"
$daOpDesc = New-Object System.Data.OleDb.OleDbDataAdapter($cmdOpDesc)
$dtOpDesc = New-Object System.Data.DataTable
$daOpDesc.Fill($dtOpDesc) | Out-Null
$dtOpDesc | Format-Table -AutoSize | Out-String | Write-Host

Write-Host "--- LdrElmsOprs for MB 211..215 ---"
$cmdElmOp = $conn.CreateCommand()
$cmdElmOp.CommandText = "SELECT * FROM LdrElmsOprs WHERE Elmnt_Oprnd_Adrs IN (211, 212, 213, 214, 215)"
$daElmOp = New-Object System.Data.OleDb.OleDbDataAdapter($cmdElmOp)
$dtElmOp = New-Object System.Data.DataTable
$daElmOp.Fill($dtElmOp) | Out-Null
$dtElmOp | Format-Table -AutoSize | Out-String | Write-Host

Write-Host "--- DISTINCT OprndType in LdrOperands ---"
$cmd4 = $conn.CreateCommand()
$cmd4.CommandText = "SELECT DISTINCT OprndType FROM LdrOperands"
$da4 = New-Object System.Data.OleDb.OleDbDataAdapter($cmd4)
$dt4 = New-Object System.Data.DataTable
$da4.Fill($dt4) | Out-Null
$dt4 | Format-Table -AutoSize | Out-String | Write-Host

Write-Host "--- Sample of each OprndType in LdrOperands ---"
foreach ($row in $dt4.Rows) {
    $t = $row["OprndType"]
    $cmdSample = $conn.CreateCommand()
    $cmdSample.CommandText = "SELECT TOP 3 OprndType, OprndAddress, OprndDescription FROM LdrOperands WHERE OprndType = $t"
    $daSample = New-Object System.Data.OleDb.OleDbDataAdapter($cmdSample)
    $dtSample = New-Object System.Data.DataTable
    $daSample.Fill($dtSample) | Out-Null
    $dtSample | Format-Table -AutoSize | Out-String | Write-Host
}

$conn.Close()
Remove-Item -Recurse -Force $temp
