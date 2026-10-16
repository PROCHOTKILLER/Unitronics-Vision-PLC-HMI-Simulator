$bytes = [System.IO.File]::ReadAllBytes('C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\VisiLogic Self Reg DLLs\UnVsCon1.dll')
$idx = 1439591
$start = [Math]::Max(0, $idx - 150)
$len = 300
$slice = New-Object byte[] $len
[Array]::Copy($bytes, $start, $slice, 0, $len)

for ($r = 0; $r -lt $len; $r += 16) {
    $lineHex = ""
    $lineAsc = ""
    for ($c = 0; $c -lt 16; $c++) {
        $pos = $r + $c
        if ($pos -lt $len) {
            $b = $slice[$pos]
            $lineHex += "{0:X2} " -f $b
            if ($b -ge 32 -and $b -le 126) { $lineAsc += [char]$b } else { $lineAsc += "." }
        } else {
            $lineHex += "   "
        }
    }
    Write-Host ("{0:X8} : {1} | {2}" -f ($start + $r), $lineHex, $lineAsc)
}
