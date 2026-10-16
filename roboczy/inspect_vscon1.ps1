$bytes = [System.IO.File]::ReadAllBytes('C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\VisiLogic Self Reg DLLs\UnVsCon1.dll')
$u = [System.Text.Encoding]::Unicode.GetString($bytes)
$a = [System.Text.Encoding]::ASCII.GetString($bytes)
$matchesU = [regex]::Matches($u, '[\x20-\x7E]{4,}')
$matchesA = [regex]::Matches($a, '[\x20-\x7E]{4,}')

Write-Host "UnVsCon1.dll strings found:"
$set = New-Object System.Collections.Generic.HashSet[string]
foreach ($m in ($matchesU + $matchesA)) {
    $v = $m.Value.Trim()
    if ($v -match 'GS|GF|Download|OPLC|PCOM|Get|Read|Write' -and -not $set.Contains($v)) {
        [void]$set.Add($v)
        Write-Host "  $v"
    }
}
