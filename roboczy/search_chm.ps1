$chmDir = "C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\Help\"
# CHM files are compressed HTML, but many text strings are visible or we can search for 307
$files = Get-ChildItem $chmDir -Filter "*.chm"
foreach ($f in $files) {
    try {
        $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
        $str = [System.Text.Encoding]::ASCII.GetString($bytes)
        if ($str.Contains("SI 307") -or $str.Contains("SI307") -or $str.Contains("SI 252") -or $str.Contains("SI252")) {
            Write-Host "Found in: $($f.Name)"
        }
    } catch {}
}
