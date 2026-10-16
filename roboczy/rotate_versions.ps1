param(
    [string]$CurrentVersion = "0.0.1"
)

$baseDir = Resolve-Path ".."
$stareDir = Join-Path $baseDir "stare wersje"
$exePath = Join-Path $baseDir "virtualplc.exe"
$docPath = Join-Path $baseDir "INSTRUKCJA_SYMULATORA.md"

if (-not (Test-Path $stareDir)) {
    New-Item -ItemType Directory -Path $stareDir -Force | Out-Null
}

# Jesli virtualplc.exe juz istnieje w katalogu glownym symulatora i chcemy go zachowac w starych wersjach
if (Test-Path $exePath) {
    $verInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
    $archVer = $verInfo.FileVersion
    if ([string]::IsNullOrWhiteSpace($archVer) -or $archVer -eq "0.0.0.0") {
        $folderName = "v0.0.0"
    } else {
        $folderName = "v" + $archVer.TrimEnd(".0")
    }
    
    # Jesli wersja jest inna niz nowo budowana badz jeszcze nie ma takiego katalogu
    $targetArchive = Join-Path $stareDir $folderName
    if (-not (Test-Path $targetArchive)) {
        New-Item -ItemType Directory -Path $targetArchive -Force | Out-Null
        Copy-Item $exePath (Join-Path $targetArchive "virtualplc.exe") -Force
        if (Test-Path $docPath) {
            Copy-Item $docPath (Join-Path $targetArchive "INSTRUKCJA_SYMULATORA.md") -Force
        }
        Write-Host "[ARCHIWUM] Zabezpieczono poprzednia wersje $folderName w: $targetArchive"
    }
}

# Rotacja: zachowaj scisle maksymalnie 5 ostatnich wersji w katalogu 'stare wersje'
$allVersions = Get-ChildItem -Path $stareDir -Directory | Sort-Object LastWriteTime -Descending
if ($allVersions.Count -gt 5) {
    $toRemove = $allVersions | Select-Object -Skip 5
    foreach ($old in $toRemove) {
        Write-Host "[ROTACJA] Usuwanie wersji archiwalnej powyzej limitu 5 ostatnich: $($old.Name)"
        Remove-Item -Path $old.FullName -Recurse -Force
    }
}

$currentCount = (Get-ChildItem -Path $stareDir -Directory).Count
Write-Host "[ROTACJA] Liczba zarchiwizowanych wersji: $currentCount / max 5"
