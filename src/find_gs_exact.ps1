$bytes = [System.IO.File]::ReadAllBytes('C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\VisiLogic Self Reg DLLs\UnVsCon2.dll')

# Search for "GS" (0x47, 0x53)
$matches = @()
for ($i = 0; $i -lt $bytes.Length - 1; $i++) {
    if ($bytes[$i] -eq 0x47 -and $bytes[$i+1] -eq 0x53) {
        # Check if preceded or followed by ASCII or Unicode chars
        $start = [Math]::Max(0, $i - 30)
        $len = [Math]::Min(60, $bytes.Length - $start)
        $subA = [System.Text.Encoding]::ASCII.GetString($bytes, $start, $len)
        $subU = [System.Text.Encoding]::Unicode.GetString($bytes, $start, $len)
        if ($subA -match '[\x20-\x7E]{6,}' -or $subU -match '[\x20-\x7E]{6,}') {
            Write-Host "Index $i :"
            Write-Host "  ASCII:   $subA"
            Write-Host "  Unicode: $subU"
        }
    }
}
