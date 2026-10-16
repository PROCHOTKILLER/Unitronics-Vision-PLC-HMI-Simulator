$bytes = [System.IO.File]::ReadAllBytes('C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\VisiLogic Self Reg DLLs\UnVsCon2.dll')
$u = [System.Text.Encoding]::Unicode.GetString($bytes)
$idx = $u.IndexOf("START DOWNLOAD")
Write-Host "Index: $idx"
if ($idx -ge 0) {
    # Dump 4000 characters around it
    $sub = $u.Substring([Math]::Max(0, $idx - 500), 4000)
    # Extract lines/strings
    $matches = [regex]::Matches($sub, '[\x20-\x7E]{3,}')
    foreach ($m in $matches) {
        Write-Host "  $($m.Value)"
    }
}
