$ErrorActionPreference = 'Stop'

$installerDirectory = $PSScriptRoot
$outputDirectory = Join-Path $installerDirectory 'bin'
$objectDirectory = Join-Path $installerDirectory 'obj'
$wixBinDirectory = Join-Path ([System.IO.Path]::GetTempPath()) 'Survive-The-enemies-WiX314\unpacked'
$candle = Join-Path $wixBinDirectory 'candle.exe'
$light = Join-Path $wixBinDirectory 'light.exe'
foreach ($tool in @($candle, $light)) {
    if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) {
        throw "Required WiX build tool not found: $tool. Build the MSI once to install the pinned WiX toolset."
    }
}

New-Item -ItemType Directory -Path $outputDirectory, $objectDirectory -Force | Out-Null
& (Join-Path $installerDirectory 'build.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "The MSI build failed (exit code $LASTEXITCODE)."
}

$msiPath = Join-Path $outputDirectory 'VirtualFolderUtility.msi'
$bundleSource = Join-Path $installerDirectory 'VirtualFolderUtility.Bundle.wxs'
$bundleObject = Join-Path $objectDirectory 'VirtualFolderUtility.Bundle.wixobj'
$exePath = Join-Path $outputDirectory 'VirtualFolderUtilitySetup.exe'
& $candle -nologo -ext WixBalExtension "-dIconPath=$(Join-Path $installerDirectory 'VirtualFolderUtility.ico')" "-dMsiPath=$msiPath" -out $bundleObject $bundleSource
if ($LASTEXITCODE -ne 0) {
    throw "WiX failed to compile the EXE installer (exit code $LASTEXITCODE)."
}

& $light -nologo -ext WixBalExtension -ext WixUtilExtension -out $exePath $bundleObject
if ($LASTEXITCODE -ne 0) {
    throw "WiX failed to link the EXE installer (exit code $LASTEXITCODE)."
}

Write-Output "Created $msiPath"
Write-Output "Created $exePath"
