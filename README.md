# Survive-The-enemies
Survie the enemies code game ongoing project

## Virtual Folder Utility installer

On Windows, build the MSI from the utility archive:

```powershell
.\installer\build.ps1
```

The installer is written to `installer\bin\VirtualFolderUtility.msi`. It installs
the archive contents under `%LOCALAPPDATA%\VirtualFolderUtility` and creates a
desktop shortcut for the utility. The build downloads WiX Toolset 3.14.1 from
its official GitHub release and verifies the archive checksum. The MSI and
desktop shortcut use the icon extracted from `%WINDIR%\System32\moricons.dll`,
resource `-16`.
