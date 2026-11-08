$ErrorActionPreference = 'Stop'

$installerDirectory = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $installerDirectory '..')).Path
$outputDirectory = Join-Path $installerDirectory 'bin'
$objectDirectory = Join-Path $installerDirectory 'obj'
$wixDirectory = Join-Path ([System.IO.Path]::GetTempPath()) 'Survive-The-enemies-WiX314'
$wixBinDirectory = Join-Path $wixDirectory 'unpacked'
$kitsBinDirectory = 'C:\Program Files (x86)\Windows Kits\10\bin'
$windowsSdk = Get-ChildItem -LiteralPath $kitsBinDirectory -Directory |
    Where-Object {
        $sdkVersion = [version]::MinValue
        [version]::TryParse($_.Name, [ref]$sdkVersion) -and
            (Test-Path (Join-Path $_.FullName 'x64\makeappx.exe'))
    } |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1

if (-not $windowsSdk) {
    throw "The Windows SDK packaging tools were not found under $kitsBinDirectory."
}

$makeAppx = Join-Path $windowsSdk.FullName 'x64\makeappx.exe'
$signTool = Join-Path $windowsSdk.FullName 'x64\signtool.exe'
$candle = Join-Path $wixBinDirectory 'candle.exe'
$light = Join-Path $wixBinDirectory 'light.exe'
foreach ($tool in @($makeAppx, $signTool, $candle, $light)) {
    if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) {
        throw "Required packaging tool not found: $tool. Build the MSI once to install the pinned WiX toolset."
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

$packageDirectory = Join-Path $installerDirectory 'msix-package'
$stageDirectory = Join-Path $packageDirectory 'stage'
$launcherSource = Join-Path $installerDirectory 'msix\Launcher.cs'
$launcherPath = Join-Path $stageDirectory 'VirtualFolderUtilityLauncher.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw "The .NET Framework 4.x C# compiler was not found: $compiler"
}

if (Test-Path -LiteralPath $packageDirectory) {
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null
Expand-Archive -LiteralPath (Join-Path $repoRoot 'VirtualFolderUtility.zip') -DestinationPath $packageDirectory
$payloadDirectory = Join-Path $packageDirectory 'VirtualFolderUtility'
Get-ChildItem -LiteralPath $payloadDirectory -Force | Move-Item -Destination $stageDirectory
Remove-Item -LiteralPath $payloadDirectory -Force

& $compiler /nologo /target:winexe /platform:x64 /reference:System.dll /reference:System.Windows.Forms.dll "/out:$launcherPath" $launcherSource
if ($LASTEXITCODE -ne 0) {
    throw "The MSIX launcher failed to compile (exit code $LASTEXITCODE)."
}

$assetDirectory = Join-Path $stageDirectory 'Assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::ExtractAssociatedIcon((Join-Path $installerDirectory 'VirtualFolderUtility.ico'))
if (-not $icon) {
    throw 'The packaged utility icon could not be loaded.'
}
try {
    $iconBitmap = $icon.ToBitmap()
    try {
        foreach ($size in @(44, 50, 150)) {
            $bitmap = [System.Drawing.Bitmap]::new($size, $size)
            try {
                $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.Clear([System.Drawing.Color]::Transparent)
                    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                    $graphics.DrawImage($iconBitmap, 0, 0, $size, $size)
                }
                finally {
                    $graphics.Dispose()
                }

                $name = switch ($size) {
                    44 { 'Square44x44Logo.png' }
                    50 { 'StoreLogo.png' }
                    150 { 'Square150x150Logo.png' }
                }
                $bitmap.Save((Join-Path $assetDirectory $name), [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally {
                $bitmap.Dispose()
            }
        }
    }
    finally {
        $iconBitmap.Dispose()
    }
}
finally {
    $icon.Dispose()
}

Copy-Item -LiteralPath (Join-Path $installerDirectory 'msix\AppxManifest.xml') -Destination $stageDirectory
$mappingPath = Join-Path $objectDirectory 'VirtualFolderUtility.msix.map'
$mapping = Get-ChildItem -LiteralPath $stageDirectory -File -Recurse | ForEach-Object {
    $relativePath = [System.IO.Path]::GetRelativePath($stageDirectory, $_.FullName).Replace('/', '\')
    '"' + $_.FullName + '" "' + $relativePath + '"'
}
[System.IO.File]::WriteAllLines(
    $mappingPath,
    @('[Files]') + $mapping,
    [System.Text.UTF8Encoding]::new($false))

$msixPath = Join-Path $outputDirectory 'VirtualFolderUtility.msix'
$certificatePath = Join-Path $outputDirectory 'VirtualFolderUtility.cer'
& $makeAppx pack /f $mappingPath /p $msixPath /o
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed to package the MSIX (exit code $LASTEXITCODE)."
}

$publisherSubject = 'CN=SM21-blip'
$signingCertificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $publisherSubject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1
if (-not $signingCertificate) {
    $signingCertificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $publisherSubject `
        -FriendlyName 'Virtual Folder Utility MSIX signing' `
        -CertStoreLocation Cert:\CurrentUser\My `
        -KeyAlgorithm RSA `
        -KeyLength 3072 `
        -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddYears(5)
}
Export-Certificate -Cert $signingCertificate -FilePath $certificatePath -Force | Out-Null

& $signTool sign /fd SHA256 /sha1 $signingCertificate.Thumbprint /s My $msixPath
if ($LASTEXITCODE -ne 0) {
    throw "SignTool failed to sign the MSIX (exit code $LASTEXITCODE)."
}

$rootStore = [System.Security.Cryptography.X509Certificates.X509Store]::new(
    'Root',
    [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
$publisherCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
$rootStore.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
$wasTrusted = @($rootStore.Certificates | Where-Object Thumbprint -eq $publisherCertificate.Thumbprint).Count -gt 0
try {
    if (-not $wasTrusted) {
        $rootStore.Add($publisherCertificate)
    }

    & $signTool verify /pa /v $msixPath
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool could not verify the signed MSIX (exit code $LASTEXITCODE)."
    }
}
finally {
    if (-not $wasTrusted) {
        $rootStore.Remove($publisherCertificate)
    }
    $rootStore.Close()
    $publisherCertificate.Dispose()
}

Write-Output "Created $exePath"
Write-Output "Created $msixPath"
Write-Output "Created $certificatePath"
Write-Output "MSIX publisher certificate thumbprint: $($signingCertificate.Thumbprint)"
