$dllPath = "C:\Program Files (x86)\Unitronics\Unitronics VisiLogic_C\VisiLogic Self Reg DLLs\UnVsCon1.dll"
# Read typelib or imports
$bytes = [System.IO.File]::ReadAllBytes($dllPath)
$str = [System.Text.Encoding]::ASCII.GetString($bytes)
$matches = [regex]::Matches($str, '([A-Za-z0-9_]+\.dll|[A-Za-z0-9_]+\.ocx)')
$set = New-Object System.Collections.Generic.HashSet[string]
foreach ($m in $matches) {
    if (-not $set.Contains($m.Value)) {
        [void]$set.Add($m.Value)
        Write-Host "Ref: $($m.Value)"
    }
}
