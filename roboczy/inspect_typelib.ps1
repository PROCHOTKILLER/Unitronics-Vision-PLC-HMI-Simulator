$t = [Type]::GetTypeFromProgID("UnCmDrv1.clsCommDriver")
if ($t -ne $null) {
    Write-Host "Found Type: $($t.FullName)"
    $methods = $t.GetMethods()
    foreach ($m in $methods) {
        $params = ($m.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
        Write-Host "$($m.ReturnType.Name) $($m.Name)($params)"
    }
} else {
    Write-Host "Could not get TypeFromProgID"
}
