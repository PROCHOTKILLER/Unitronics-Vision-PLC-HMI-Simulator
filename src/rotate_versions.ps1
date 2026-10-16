param(
    [string]$CurrentVersion = "0.0.1"
)

$baseDir = Resolve-Path ".."
$archiveDir = Join-Path $baseDir "archive"
$exePath = Join-Path $baseDir "virtualplc.exe"
$docPath = Join-Path $baseDir "SIMULATOR_MANUAL.md"

if (-not (Test-Path $archiveDir)) {
    New-Item -ItemType Directory -Path $archiveDir -Force | Out-Null
}

# If virtualplc.exe exists in root, archive previous release
if (Test-Path $exePath) {
    $verInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
    $archVer = $verInfo.FileVersion
    if ([string]::IsNullOrWhiteSpace($archVer) -or $archVer -eq "0.0.0.0") {
        $folderName = "v0.0.0"
    } else {
        $folderName = "v" + $archVer.TrimEnd(".0")
    }
    
    $targetArchive = Join-Path $archiveDir $folderName
    if (-not (Test-Path $targetArchive)) {
        New-Item -ItemType Directory -Path $targetArchive -Force | Out-Null
        Copy-Item $exePath (Join-Path $targetArchive "virtualplc.exe") -Force
        if (Test-Path $docPath) {
            Copy-Item $docPath (Join-Path $targetArchive "SIMULATOR_MANUAL.md") -Force
        }
        Write-Host "[ARCHIVE] Saved previous version $folderName in: $targetArchive"
    }
}

# Rotation: keep strictly max 5 last versions in 'archive'
$allVersions = Get-ChildItem -Path $archiveDir -Directory | Sort-Object LastWriteTime -Descending
if ($allVersions.Count -gt 5) {
    $toRemove = $allVersions | Select-Object -Skip 5
    foreach ($old in $toRemove) {
        Write-Host "[ROTATION] Removing archived version beyond 5-version limit: $($old.Name)"
        Remove-Item -Path $old.FullName -Recurse -Force
    }
}

$currentCount = (Get-ChildItem -Path $archiveDir -Directory).Count
Write-Host "[ROTATION] Archived versions count: $currentCount / max 5"
