$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$archivePath = Join-Path $repoRoot 'VirtualFolderUtility.zip'
$iconPath = Join-Path $PSScriptRoot 'VirtualFolderUtility.ico'
$outputDirectory = Join-Path $PSScriptRoot 'bin'
$outputPath = Join-Path $outputDirectory 'VirtualFolderUtility.msi'
$stageDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "VirtualFolderUtilityInstaller-$PID"
$wixDirectory = Join-Path ([System.IO.Path]::GetTempPath()) 'Survive-The-enemies-WiX314'
$wixArchivePath = Join-Path $wixDirectory 'wix314-binaries.zip'
$wixDownloadUrl = 'https://github.com/wixtoolset/wix3/releases/download/wix3141rtm/wix314-binaries.zip'
$wixArchiveHash = '6AC824E1642D6F7277D0ED7EA09411A508F6116BA6FAE0AA5F2C7DAA2FF43D31'

if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
    throw "Required utility archive not found: $archivePath"
}
if (-not (Test-Path -LiteralPath $iconPath -PathType Leaf)) {
    throw "Required installer icon not found: $iconPath"
}

try {
    New-Item -ItemType Directory -Path $wixDirectory -Force | Out-Null
    if (-not (Test-Path -LiteralPath $wixArchivePath -PathType Leaf)) {
        Invoke-WebRequest -Uri $wixDownloadUrl -OutFile $wixArchivePath
    }

    $actualWixArchiveHash = (Get-FileHash -LiteralPath $wixArchivePath -Algorithm SHA256).Hash
    if ($actualWixArchiveHash -ne $wixArchiveHash) {
        throw "WiX archive checksum mismatch: $actualWixArchiveHash"
    }

    $wixBinDirectory = Join-Path $wixDirectory 'unpacked'
    if (-not (Test-Path -LiteralPath (Join-Path $wixBinDirectory 'candle.exe') -PathType Leaf)) {
        Expand-Archive -LiteralPath $wixArchivePath -DestinationPath $wixBinDirectory -Force
    }

    $candleExe = Join-Path $wixBinDirectory 'candle.exe'
    $lightExe = Join-Path $wixBinDirectory 'light.exe'
    foreach ($tool in @($candleExe, $lightExe)) {
        if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) {
            throw "WiX build tool not found: $tool"
        }
    }

    Expand-Archive -LiteralPath $archivePath -DestinationPath $stageDirectory
    $payloadDirectory = Join-Path $stageDirectory 'VirtualFolderUtility'
    $expectedFiles = @(
        'README.md',
        'Run Virtual Folder Utility.bat',
        'VirtualFolderApplet.cpl',
        'Source\CplEntryPoint.cs',
        'Source\NativeMethods.cs',
        'Source\UtilityWindow.cs',
        'Source\VirtualFolderApplet.csproj'
    )
    foreach ($file in $expectedFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $payloadDirectory $file) -PathType Leaf)) {
            throw "The utility archive is missing $file."
        }
    }

    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $objectDirectory = Join-Path $PSScriptRoot 'obj'
    New-Item -ItemType Directory -Path $objectDirectory -Force | Out-Null
    $packageObject = Join-Path $objectDirectory 'VirtualFolderUtility.wixobj'
    & $candleExe -nologo "-dPayloadDir=$payloadDirectory" "-dIconPath=$iconPath" -out $packageObject (Join-Path $PSScriptRoot 'VirtualFolderUtility.wxs')
    if ($LASTEXITCODE -ne 0) {
        throw "WiX candle failed to compile the installer (exit code $LASTEXITCODE)."
    }

    & $lightExe -nologo -out $outputPath $packageObject
    if ($LASTEXITCODE -ne 0) {
        throw "WiX light failed to link the MSI (exit code $LASTEXITCODE)."
    }
}
finally {
    if (Test-Path -LiteralPath $stageDirectory) {
        Remove-Item -LiteralPath $stageDirectory -Recurse -Force
    }
}

Write-Output "Created $outputPath"
