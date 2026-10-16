$bytes = [System.IO.File]::ReadAllBytes('C:\Program Files (x86)\Common Files\Unitronics\UnCmDrv1.dll')
$text = [System.Text.Encoding]::ASCII.GetString($bytes)
$matches = [regex]::Matches($text, 'UnCmDrv1\.[A-Za-z0-9_]+')
foreach ($m in $matches) {
    Write-Host "ProgID: $($m.Value)"
}
