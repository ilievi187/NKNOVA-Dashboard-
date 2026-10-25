param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [Parameter(Mandatory = $false)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression

function New-NknovaReproducibleZip {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    $archiveStream = [System.IO.File]::Open(
        $DestinationPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None
    )
    $archive = $null
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $archiveStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false
        )
        $fixedTimestamp = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $files = Get-ChildItem -LiteralPath $SourceDirectory -Recurse -File | Sort-Object -Property FullName
        foreach ($file in $files) {
            $entryName = [System.IO.Path]::GetRelativePath($SourceDirectory, $file.FullName).Replace('\', '/')
            $entry = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $fixedTimestamp
            $inputStream = [System.IO.File]::OpenRead($file.FullName)
            try {
                $entryStream = $entry.Open()
                try {
                    $inputStream.CopyTo($entryStream)
                }
                finally {
                    $entryStream.Dispose()
                }
            }
            finally {
                $inputStream.Dispose()
            }
        }
    }
    finally {
        if ($null -ne $archive) { $archive.Dispose() }
        $archiveStream.Dispose()
    }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $repositoryRoot 'Nolvus.Dashboard/Nolvus.Dashboard.csproj'
$projectText = Get-Content -LiteralPath $projectFile -Raw
$versionMatch = [regex]::Match($projectText, '<Version>([^<]+)</Version>')
if (-not $versionMatch.Success) { throw 'Dashboard project version was not found.' }
$projectVersion = $versionMatch.Groups[1].Value
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $projectVersion
}

# Validate before creating directories or invoking publish: this value is used in
# temporary and output paths, and must not contain separators or traversal.
$versionPattern = '^(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?(?:\+[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?
$tempRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    [System.IO.Path]::GetTempPath()
} else {
    $env:RUNNER_TEMP
}
$workRoot = Join-Path $tempRoot ("nknova-win-{0}-{1}" -f $Version, [Guid]::NewGuid().ToString('N'))
$publishDirectory = Join-Path $workRoot 'Nolvus'
$archivePath = Join-Path $OutputDirectory "NKNOVA-Windows-$Version.zip"
$checksumPath = "$archivePath.sha256"
if (Test-Path -LiteralPath $archivePath) {
    throw "Refusing to overwrite existing archive: $archivePath"
}
if (Test-Path -LiteralPath $checksumPath) {
    throw "Refusing to overwrite existing checksum: $checksumPath"
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

Push-Location $repositoryRoot
try {
    & dotnet publish $projectFile --configuration Release --runtime win-x64 `
        -p:DebugType=none -p:DebugSymbols=false --output $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

# Ship the GPL and attribution along with the executable. Keep extended guidance in a subfolder.
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $publishDirectory 'LICENSE')
foreach ($document in @('README.md', 'CHANGELOG.md', 'THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $document) -Destination $publishDirectory
}
$documentationDirectory = Join-Path $publishDirectory 'docs'
New-Item -ItemType Directory -Path $documentationDirectory -Force | Out-Null
foreach ($document in @('INSTALLATION.md', 'TROUBLESHOOTING.md', 'CREDENTIALS.md', 'RELEASING.md')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "docs/$document") -Destination $documentationDirectory
}
Get-ChildItem -LiteralPath $publishDirectory -Recurse -File -Filter '*.pdb' | Remove-Item -Force

$appHost = Join-Path $publishDirectory 'NolvusDashboard.exe'
if (-not (Test-Path -LiteralPath $appHost -PathType Leaf)) {
    throw "Windows dashboard apphost is missing: $appHost"
}
if (Test-Path -LiteralPath (Join-Path $publishDirectory 'NolvusUpdater.exe')) {
    throw 'The upstream Nolvus updater must not be included in a NKNOVA release.'
}
if (Test-Path -LiteralPath (Join-Path $publishDirectory 'NolvusLauncher.exe')) {
    throw 'The unused upstream Nolvus launcher executable must not be embedded in a NKNOVA release.'
}

$unexpectedFiles = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File |
    Where-Object { $_.Extension -in @('.pdb', '.esp', '.esm', '.bsa') -or $_.Name -eq 'SkyrimSE.exe' }
if ($unexpectedFiles.Count -gt 0) {
    throw "Unexpected debug symbols or game data found: $($unexpectedFiles.FullName -join ', ')"
}

New-NknovaReproducibleZip -SourceDirectory $publishDirectory -DestinationPath $archivePath
$zip = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $entries = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    if (-not ($entries -contains 'NolvusDashboard.exe')) {
        throw 'The Windows archive is missing NolvusDashboard.exe.'
    }
    foreach ($requiredFile in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'docs/INSTALLATION.md', 'docs/TROUBLESHOOTING.md', 'docs/CREDENTIALS.md')) {
        if (-not ($entries -contains $requiredFile)) {
            throw "The Windows archive is missing required documentation: $requiredFile"
        }
    }
    foreach ($entry in $entries) {
        if ($entry.StartsWith('/') -or $entry -match '^[A-Za-z]:' -or $entry -match '(^|/)\.\.(?:/|$)') {
            throw "Unsafe path in Windows archive: $entry"
        }
    }
    if ($entries | Where-Object { $_ -match '\.pdb$|\.esp$|\.esm$|\.bsa$|(^|/)SkyrimSE\.exe$|(^|/)NolvusLauncher\.exe$' }) {
        throw 'The Windows release archive contains debug symbols or game data.'
    }
}
finally {
    $zip.Dispose()
}

$extractionDirectory = Join-Path $workRoot 'extracted'
Expand-Archive -LiteralPath $archivePath -DestinationPath $extractionDirectory
$extractedAppHost = Join-Path $extractionDirectory 'NolvusDashboard.exe'
if (-not (Test-Path -LiteralPath $extractedAppHost -PathType Leaf)) {
    throw 'The extracted Windows release archive is missing NolvusDashboard.exe.'
}

$reproducibilityArchive = Join-Path $workRoot 'reproducibility-check.zip'
New-NknovaReproducibleZip -SourceDirectory $extractionDirectory -DestinationPath $reproducibilityArchive
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$reproducibilityHash = (Get-FileHash -LiteralPath $reproducibilityArchive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $reproducibilityHash) {
    throw 'Repackaging the extracted Windows payload did not reproduce the original archive checksum.'
}
Remove-Item -LiteralPath $reproducibilityArchive -Force
Write-Output 'Windows archive reproducibility check passed.'

$smokeHome = Join-Path $workRoot 'user-profile'
New-Item -ItemType Directory -Path $smokeHome -Force | Out-Null
$env:USERPROFILE = $smokeHome
$env:APPDATA = Join-Path $smokeHome 'AppData/Roaming'
$env:LOCALAPPDATA = Join-Path $smokeHome 'AppData/Local'
New-Item -ItemType Directory -Path $env:APPDATA,$env:LOCALAPPDATA -Force | Out-Null

$process = Start-Process -FilePath $extractedAppHost -WorkingDirectory $extractionDirectory -PassThru
try {
    Start-Sleep -Seconds 15
    $process.Refresh()
    if ($process.HasExited) {
        throw "Windows dashboard exited during startup smoke test (exit code $($process.ExitCode))."
    }
}
finally {
    # A leftover smoke-test process can keep files or user-profile state in use.
    # Treat failed cleanup as a packaging failure rather than claiming a clean test.
    try {
        $process.Refresh()
        if (-not $process.HasExited) {
            & taskkill.exe /PID $process.Id /T /F | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Could not stop the Windows dashboard smoke-test process tree (PID $($process.Id))."
            }
            $null = $process.WaitForExit(10000)
            $process.Refresh()
            if (-not $process.HasExited) {
                throw "Windows dashboard smoke-test process (PID $($process.Id)) remained alive after taskkill."
            }
        }
    }
    finally {
        $process.Dispose()
    }
}

"$hash  $(Split-Path -Leaf $archivePath)" | Set-Content -LiteralPath $checksumPath -Encoding ascii
Write-Output "Windows archive validated: $archivePath"
Write-Output "SHA256: $hash"
Write-Output 'Windows archive extraction and dashboard startup smoke test passed (15 seconds, isolated profile).'
Remove-Item -LiteralPath $workRoot -Recurse -Force

if ($Version -cnotmatch $versionPattern) {
    throw 'Invalid package version: expected a safe semantic version.'
}
if ($Version -cne $projectVersion) {
    throw "Package version does not match Dashboard project version ($projectVersion)."
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$tempRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    [System.IO.Path]::GetTempPath()
} else {
    $env:RUNNER_TEMP
}
$workRoot = Join-Path $tempRoot ("nknova-win-{0}-{1}" -f $Version, [Guid]::NewGuid().ToString('N'))
$publishDirectory = Join-Path $workRoot 'Nolvus'
$archivePath = Join-Path $OutputDirectory "NKNOVA-Windows-$Version.zip"
$checksumPath = "$archivePath.sha256"
if (Test-Path -LiteralPath $archivePath) {
    throw "Refusing to overwrite existing archive: $archivePath"
}
if (Test-Path -LiteralPath $checksumPath) {
    throw "Refusing to overwrite existing checksum: $checksumPath"
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

Push-Location $repositoryRoot
try {
    & dotnet publish $projectFile --configuration Release --runtime win-x64 `
        -p:DebugType=none -p:DebugSymbols=false --output $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

# Ship the GPL and attribution along with the executable. Keep extended guidance in a subfolder.
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $publishDirectory 'LICENSE')
foreach ($document in @('README.md', 'CHANGELOG.md', 'THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $document) -Destination $publishDirectory
}
$documentationDirectory = Join-Path $publishDirectory 'docs'
New-Item -ItemType Directory -Path $documentationDirectory -Force | Out-Null
foreach ($document in @('INSTALLATION.md', 'TROUBLESHOOTING.md', 'CREDENTIALS.md', 'RELEASING.md')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "docs/$document") -Destination $documentationDirectory
}
Get-ChildItem -LiteralPath $publishDirectory -Recurse -File -Filter '*.pdb' | Remove-Item -Force

$appHost = Join-Path $publishDirectory 'NolvusDashboard.exe'
if (-not (Test-Path -LiteralPath $appHost -PathType Leaf)) {
    throw "Windows dashboard apphost is missing: $appHost"
}
if (Test-Path -LiteralPath (Join-Path $publishDirectory 'NolvusUpdater.exe')) {
    throw 'The upstream Nolvus updater must not be included in a NKNOVA release.'
}
if (Test-Path -LiteralPath (Join-Path $publishDirectory 'NolvusLauncher.exe')) {
    throw 'The unused upstream Nolvus launcher executable must not be embedded in a NKNOVA release.'
}

$unexpectedFiles = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File |
    Where-Object { $_.Extension -in @('.pdb', '.esp', '.esm', '.bsa') -or $_.Name -eq 'SkyrimSE.exe' }
if ($unexpectedFiles.Count -gt 0) {
    throw "Unexpected debug symbols or game data found: $($unexpectedFiles.FullName -join ', ')"
}

New-NknovaReproducibleZip -SourceDirectory $publishDirectory -DestinationPath $archivePath
$zip = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $entries = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    if (-not ($entries -contains 'NolvusDashboard.exe')) {
        throw 'The Windows archive is missing NolvusDashboard.exe.'
    }
    foreach ($requiredFile in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'docs/INSTALLATION.md', 'docs/TROUBLESHOOTING.md', 'docs/CREDENTIALS.md')) {
        if (-not ($entries -contains $requiredFile)) {
            throw "The Windows archive is missing required documentation: $requiredFile"
        }
    }
    foreach ($entry in $entries) {
        if ($entry.StartsWith('/') -or $entry -match '^[A-Za-z]:' -or $entry -match '(^|/)\.\.(?:/|$)') {
            throw "Unsafe path in Windows archive: $entry"
        }
    }
    if ($entries | Where-Object { $_ -match '\.pdb$|\.esp$|\.esm$|\.bsa$|(^|/)SkyrimSE\.exe$|(^|/)NolvusLauncher\.exe$' }) {
        throw 'The Windows release archive contains debug symbols or game data.'
    }
}
finally {
    $zip.Dispose()
}

$extractionDirectory = Join-Path $workRoot 'extracted'
Expand-Archive -LiteralPath $archivePath -DestinationPath $extractionDirectory
$extractedAppHost = Join-Path $extractionDirectory 'NolvusDashboard.exe'
if (-not (Test-Path -LiteralPath $extractedAppHost -PathType Leaf)) {
    throw 'The extracted Windows release archive is missing NolvusDashboard.exe.'
}

$reproducibilityArchive = Join-Path $workRoot 'reproducibility-check.zip'
New-NknovaReproducibleZip -SourceDirectory $extractionDirectory -DestinationPath $reproducibilityArchive
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$reproducibilityHash = (Get-FileHash -LiteralPath $reproducibilityArchive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $reproducibilityHash) {
    throw 'Repackaging the extracted Windows payload did not reproduce the original archive checksum.'
}
Remove-Item -LiteralPath $reproducibilityArchive -Force
Write-Output 'Windows archive reproducibility check passed.'

$smokeHome = Join-Path $workRoot 'user-profile'
New-Item -ItemType Directory -Path $smokeHome -Force | Out-Null
$env:USERPROFILE = $smokeHome
$env:APPDATA = Join-Path $smokeHome 'AppData/Roaming'
$env:LOCALAPPDATA = Join-Path $smokeHome 'AppData/Local'
New-Item -ItemType Directory -Path $env:APPDATA,$env:LOCALAPPDATA -Force | Out-Null

$process = Start-Process -FilePath $extractedAppHost -WorkingDirectory $extractionDirectory -PassThru
try {
    Start-Sleep -Seconds 15
    $process.Refresh()
    if ($process.HasExited) {
        throw "Windows dashboard exited during startup smoke test (exit code $($process.ExitCode))."
    }
}
finally {
    # A leftover smoke-test process can keep files or user-profile state in use.
    # Treat failed cleanup as a packaging failure rather than claiming a clean test.
    try {
        $process.Refresh()
        if (-not $process.HasExited) {
            & taskkill.exe /PID $process.Id /T /F | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Could not stop the Windows dashboard smoke-test process tree (PID $($process.Id))."
            }
            $null = $process.WaitForExit(10000)
            $process.Refresh()
            if (-not $process.HasExited) {
                throw "Windows dashboard smoke-test process (PID $($process.Id)) remained alive after taskkill."
            }
        }
    }
    finally {
        $process.Dispose()
    }
}

"$hash  $(Split-Path -Leaf $archivePath)" | Set-Content -LiteralPath $checksumPath -Encoding ascii
Write-Output "Windows archive validated: $archivePath"
Write-Output "SHA256: $hash"
Write-Output 'Windows archive extraction and dashboard startup smoke test passed (15 seconds, isolated profile).'
Remove-Item -LiteralPath $workRoot -Recurse -Force
