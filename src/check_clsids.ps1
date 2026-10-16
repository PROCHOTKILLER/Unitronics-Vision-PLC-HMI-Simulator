$clsids = @('{0178D9AF-D981-436A-8712-F74E25B48135}', '{2644BC56-2ABF-4B1A-83AD-57CDEB701039}', '{6BF579AD-3DAD-469A-B24C-D8539D10E028}', '{AEA08FF9-F0C0-4A79-A470-2B49D7750728}', '{E4C1A009-5A83-4452-AF3A-AAB0BB8D9ACB}')
foreach ($c in $clsids) {
    $p = (Get-ItemProperty "HKLM:\SOFTWARE\WOW6432Node\Classes\CLSID\$c\ProgID" -ErrorAction SilentlyContinue).'(default)'
    $desc = (Get-ItemProperty "HKLM:\SOFTWARE\WOW6432Node\Classes\CLSID\$c" -ErrorAction SilentlyContinue).'(default)'
    Write-Host "$c -> $p ($desc)"
}
