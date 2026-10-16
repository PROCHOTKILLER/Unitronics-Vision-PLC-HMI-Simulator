$dllFiles = Get-ChildItem "C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\VisiLogic Self Reg DLLs\*.dll"
foreach ($f in $dllFiles) {
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $textA = [System.Text.Encoding]::ASCII.GetString($bytes)
    $textU = [System.Text.Encoding]::Unicode.GetString($bytes)
    
    # Check ASCII
    $matchesA = [regex]::Matches($textA, '(?i)(/00GS|GS%|GS01|%sGS|/a\w*GS|[^\w]GS[0-9A-Fa-f]{2,})')
    if ($matchesA.Count -gt 0) {
        Write-Host "File (ASCII): $($f.Name)"
        foreach ($m in $matchesA) { Write-Host "  $($m.Value)" }
    }

    # Check Unicode
    $matchesU = [regex]::Matches($textU, '(?i)(/00GS|GS%|GS01|%sGS|/a\w*GS|[^\w]GS[0-9A-Fa-f]{2,})')
    if ($matchesU.Count -gt 0) {
        Write-Host "File (Unicode): $($f.Name)"
        foreach ($m in $matchesU) { Write-Host "  $($m.Value)" }
    }
}
