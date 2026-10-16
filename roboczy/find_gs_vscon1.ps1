$bytes = [System.IO.File]::ReadAllBytes('C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\VisiLogic Self Reg DLLs\UnVsCon1.dll')
$textA = [System.Text.Encoding]::ASCII.GetString($bytes)

# Search for "GS"
$idx = 0
while (($idx = $textA.IndexOf("GS", $idx)) -ge 0) {
    $start = [Math]::Max(0, $idx - 40)
    $len = [Math]::Min(80, $bytes.Length - $start)
    $sub = $textA.Substring($start, $len)
    Write-Host "GS at $idx : $sub"
    $idx += 2
}
