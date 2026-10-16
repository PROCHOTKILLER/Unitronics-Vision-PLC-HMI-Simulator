$dllPath = "C:\Program Files (x86)\Common Files\Unitronics\UnCmDrv1.dll"
$bytes = [System.IO.File]::ReadAllBytes($dllPath)
$u = [System.Text.Encoding]::Unicode.GetString($bytes)
$a = [System.Text.Encoding]::ASCII.GetString($bytes)

$matchesU = [regex]::Matches($u, '[A-Za-z0-9_]{3,}')
$matchesA = [regex]::Matches($a, '[A-Za-z0-9_]{3,}')

$set = New-Object System.Collections.Generic.HashSet[string]
Write-Host "UnCmDrv1.dll symbols and strings:"
foreach ($m in ($matchesU + $matchesA)) {
    $v = $m.Value.Trim()
    if ($v -match 'Read|Write|Oper|Send|Receive|Packet|Ascii|Binary|PCOM|Get|Status|Connect|PLC|Download|GS|GF|ID|CCS|RMB|RMI' -and -not $set.Contains($v)) {
        [void]$set.Add($v)
        Write-Host "  $v"
    }
}
