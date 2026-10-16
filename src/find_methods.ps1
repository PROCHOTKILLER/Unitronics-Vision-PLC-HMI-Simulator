$bytes = [System.IO.File]::ReadAllBytes('C:\Program Files (x86)\Common Files\Unitronics\UnCmDrv1.dll')
$u = [System.Text.Encoding]::Unicode.GetString($bytes)
$a = [System.Text.Encoding]::ASCII.GetString($bytes)

# Find "Read_SysIntegers"
$idx = $u.IndexOf("Read_SysIntegers")
if ($idx -lt 0) { $idx = $a.IndexOf("Read_SysIntegers") }
Write-Host "Read_SysIntegers at: $idx"

# Find "PLC_GetStatus"
$idx2 = $u.IndexOf("PLC_GetStatus")
if ($idx2 -lt 0) { $idx2 = $a.IndexOf("PLC_GetStatus") }
Write-Host "PLC_GetStatus at: $idx2"
