# Survive-The-enemies
Survie the enemies code game ongoing project

## Virtual Folder Utility installer

On Windows, build the per-user MSI from the editable utility source:

```powershell
.\installer\build.ps1
```

Build the per-user MSI and EXE release installers:

```powershell
.\installer\build-release.ps1
```

The installers are written to `installer\bin`. Both install per-user under
`%LOCALAPPDATA%\VirtualFolderUtility` and create a desktop shortcut. They do
not require administrator access. The build compiles the applet from
`VirtualFolderUtility\Source` and packages that fresh build; it does not rely
on the archived applet binary. The utility maps Explorer namespace CLSIDs,
shows their registry locations and shell metadata, opens valid CLSIDs in
Explorer, and exports the catalog as CSV.

The MSI, EXE, and app shortcuts use the icon extracted from
`%WINDIR%\System32\moricons.dll`, resource `-16`. The build downloads WiX
Toolset 3.14.1 from its official GitHub release and verifies the archive
checksum.

Download installers from [GitHub Releases](https://github.com/SM21-blip/Survive-The-enemies/releases).
