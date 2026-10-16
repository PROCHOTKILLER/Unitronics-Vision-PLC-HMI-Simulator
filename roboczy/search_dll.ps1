$files = Get-ChildItem "C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\" -Recurse -Include *.dll,*.exe
foreach ($f in $files) {
    try {
        $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
        $text = [System.Text.Encoding]::ASCII.GetString($bytes)
        if ($text.Contains("GS") -and ($text.Contains("GS01") -or $text.Contains("/00GS") -or $text.Contains("GS33"))) {
            Write-Host "Found in: $($f.FullName)"
        }
    } catch {
    }
}
