$bytes = [System.IO.File]::ReadAllBytes("C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\VisiLogic Self Reg DLLs\UnVsCon2.dll")
$pattern = [System.Text.Encoding]::ASCII.GetBytes("DownloadError.txt")

# Find index of DownloadError.txt in binary
for ($i = 0; $i -lt $bytes.Length - $pattern.Length; $i++) {
    $found = $true
    for ($j = 0; $j -lt $pattern.Length; $j++) {
        if ($bytes[$i + $j] -ne $pattern[$j]) {
            $found = $false
            break
        }
    }
    if ($found) {
        Write-Host "Found at index: $i"
        # Dump surrounding bytes as ASCII / Unicode
        $start = [Math]::Max(0, $i - 1000)
        $len = [Math]::Min($bytes.Length - $start, 2000)
        $slice = New-Object byte[] $len
        [Array]::Copy($bytes, $start, $slice, 0, $len)
        $textAscii = [System.Text.Encoding]::ASCII.GetString($slice)
        $textUni = [System.Text.Encoding]::Unicode.GetString($slice)
        Write-Host "Surrounding ASCII:"
        $matches = [regex]::Matches($textAscii, '[\x20-\x7E]{3,}')
        foreach ($m in $matches) { Write-Host "  $($m.Value)" }
        Write-Host "Surrounding Unicode:"
        $matchesU = [regex]::Matches($textUni, '[\x20-\x7E]{3,}')
        foreach ($m in $matchesU) { Write-Host "  $($m.Value)" }
    }
}
