$dllFiles = Get-ChildItem "C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\VisiLogic Self Reg DLLs\*.dll"
foreach ($f in $dllFiles) {
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $str = [System.Text.Encoding]::Unicode.GetString($bytes) + [System.Text.Encoding]::ASCII.GetString($bytes)
    if ($str.Contains("DownloadError")) {
        Write-Host "File matching DownloadError: $($f.Name)"
    }
}
