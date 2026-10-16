$dllPath = "C:\Program Files (x86)\Common Files\Unitronics\UnCmDrv1.dll"
$bytes = [System.IO.File]::ReadAllBytes($dllPath)
$textA = [System.Text.Encoding]::ASCII.GetString($bytes)

# Let's search for "GS" or "GF" or other command strings
$patterns = @("GS", "GF", "ID", "UG", "CCS", "CRN", "RN", "ST", "RS")
foreach ($p in $patterns) {
    # Look for the pattern followed by % or formatting or as literal
    $idx = 0
    while (($idx = $textA.IndexOf($p, $idx)) -ge 0) {
        # Check context
        $start = [Math]::Max(0, $idx - 20)
        $len = [Math]::Min(40, $bytes.Length - $start)
        $sub = $textA.Substring($start, $len)
        if ($sub -match "(/|%|PCOM|ID|Unit|Sys|Read|Write)") {
            Write-Host "Pattern '$p' at $idx : $sub"
        }
        $idx += $p.Length
    }
}
