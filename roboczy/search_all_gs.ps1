$files = Get-ChildItem "C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\" -Recurse -Include *.dll,*.exe
foreach ($f in $files) {
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    for ($i = 0; $i -lt $bytes.Length - 1; $i++) {
        if ($bytes[$i] -eq 0x47 -and $bytes[$i+1] -eq 0x53) {
            # Check if surrounded by ASCII chars
            $start = [Math]::Max(0, $i - 10)
            $len = [Math]::Min(25, $bytes.Length - $start)
            $sub = [System.Text.Encoding]::ASCII.GetString($bytes, $start, $len)
            if ($sub -match '(/|%|CMD|Cmd|PCOM|Get|REQ|Req)') {
                Write-Host "File $($f.Name) at $i : $sub"
            }
        }
    }
}
