$bytes = [System.IO.File]::ReadAllBytes('C:\Program Files (x86)\Common Files\Unitronics\UnCmDrv1.dll')
$pattern = [System.Text.Encoding]::ASCII.GetBytes("GS")

# Search for "GS" in UnCmDrv1.dll
for ($i = 0; $i -lt $bytes.Length - 1; $i++) {
    if ($bytes[$i] -eq 0x47 -and $bytes[$i+1] -eq 0x53) {
        # Check if preceded or followed by format chars or strings
        $start = [Math]::Max(0, $i - 15)
        $len = [Math]::Min(30, $bytes.Length - $start)
        $sub = [System.Text.Encoding]::ASCII.GetString($bytes, $start, $len)
        Write-Host "Index $i : $sub"
    }
}
