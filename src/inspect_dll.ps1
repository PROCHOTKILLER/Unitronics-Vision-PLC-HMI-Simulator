$bytes = [System.IO.File]::ReadAllBytes('C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\VisiLogic Self Reg DLLs\UnVsCon2.dll')
$u = [System.Text.Encoding]::Unicode.GetString($bytes)
$matches = [regex]::Matches($u, '[\x20-\x7E]{5,}')
$unique = New-Object System.Collections.Generic.HashSet[string]
foreach ($m in $matches) {
    $v = $m.Value.Trim()
    if ($v.Length -ge 5 -and -not $unique.Contains($v)) {
        [void]$unique.Add($v)
        if ($v -match 'Download|PLC|PCOM|Stop|Run|Mode|Model|Version|GS|Send|Receive|Oprnd|Buffer|DLU|Memory|Flash|Write|Read|Check|Connect') {
            Write-Host $v
        }
    }
}
