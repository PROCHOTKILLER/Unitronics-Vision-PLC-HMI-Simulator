$dllFiles = Get-ChildItem "C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\VisiLogic Self Reg DLLs\*.dll"
foreach ($f in $dllFiles) {
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
    if ($ascii.Contains("/A") -or $ascii.Contains("/a") -or $ascii.Contains("GS")) {
        # Check for occurrences
        $matches = [regex]::Matches($ascii, '/[Aa][0-9A-Za-z]{2,}')
        if ($matches.Count -gt 0) {
            Write-Host "File: $($f.Name)"
            foreach ($m in $matches) {
                Write-Host "  $($m.Value)"
            }
        }
    }
}
