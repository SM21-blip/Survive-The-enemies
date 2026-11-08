# Survive-The-enemies
Survie the enemies code game ongoing project

## Virtual Folder Utility installer

On Windows, build the MSI from the utility archive:

```powershell
.\installer\build.ps1
```

On Windows with the Windows SDK installed, build the MSI, EXE, and MSIX release
packages from the utility archive:

```powershell
.\installer\build-release.ps1
```

The packages are written to `installer\bin`. The MSI and EXE install the archive
contents under `%LOCALAPPDATA%\VirtualFolderUtility` and create a desktop
shortcut. The MSIX is installed and managed by Windows, and adds a Start menu
shortcut. To install the MSIX, first trust its included self-signed publisher
certificate in your Windows user's Trusted Root Certification Authorities
store, then open the package:

```powershell
Import-Certificate .\VirtualFolderUtility.cer -CertStoreLocation Cert:\CurrentUser\Root
Add-AppxPackage .\VirtualFolderUtility.msix
```

The self-signed publisher certificate and its private key are created locally;
the private key is not committed. The MSI, EXE, and app shortcuts use the icon
extracted from `%WINDIR%\System32\moricons.dll`, resource `-16`. The build
downloads WiX Toolset 3.14.1 from its official GitHub release and verifies the
archive checksum.

Download installers from [GitHub Releases](https://github.com/SM21-blip/Survive-The-enemies/releases).
