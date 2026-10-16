$bytes = [System.IO.File]::ReadAllBytes('C:\Program Files (x86)\Common Files\Unitronics\UnCmDrv1.dll')
$start = 61500
$len = 3000
$sub = [System.Text.Encoding]::ASCII.GetString($bytes, $start, $len)
$matches = [regex]::Matches($sub, '[A-Za-z0-9_]{3,}')
foreach ($m in $matches) {
    Write-Host "  $($m.Value)"
}
