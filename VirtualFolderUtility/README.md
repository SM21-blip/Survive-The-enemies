# Virtual Folder Utility

A Windows Control Panel applet for inspecting registered virtual folders, Control Panel namespace entries, Control Panel applets and disk usage; searching CLSIDs by name or GUID; creating a user-level folder of shortcuts to discovered namespace items; and opening the built-in UAC settings slider.

The catalog reads registry and drive metadata. "Create namespace folder" writes shortcuts under `Documents\Virtual Folder Utility Namespace`; it does not register a system-wide Explorer extension or modify CLSID registry keys. The UAC button opens Windows' `UserAccountControlSettings.exe`; the applet does not modify the UAC policy. Activity and error details are logged to `%LOCALAPPDATA%\VirtualFolderUtility\Logs\utility.log`.

The Control Panel icon is loaded from `moricons.dll` resource `-16` (resource ID 16).

## Build

Requirements:

- Windows x64
- .NET 10 SDK
- Visual Studio C++ build tools (required by Native AOT)

From this directory, run:

```powershell
dotnet publish -c Release
```

The publish directory contains `VirtualFolderApplet.cpl`. On another Windows PC, extract the ZIP and double-click `Run Virtual Folder Utility.bat`. It runs the CPL's exported `RunUtility` entry point through Windows' built-in `rundll32.exe`. Alternatively, open an elevated or regular Command Prompt and run:

```cmd
rundll32.exe "C:\path\to\VirtualFolderApplet.cpl",RunUtility
```

This is a self-contained x64 native applet and does not require the .NET SDK; use it on Windows 10/11 x64. Its native CRT imports are provided by the Windows Universal CRT on supported Windows versions. The `.cpl` also exports the standard Control Panel applet entry point. For its requested Control Panel icon, the host PC supplies `%WINDIR%\System32\moricons.dll`, resource `-16`. Runtime reports and logs are stored in that PC's user profile.

## Features

- Lists CLSID registry entries with a `ShellFolder` subkey and searches those entries by display name or CLSID.
- Catalogs Control Panel namespace entries and registered `.cpl` files, with a one-click option to create user-level Explorer shortcuts for them.
- Reports total, used, and available storage for each ready drive; unavailable drives are identified without accessing their contents.
- Copies the currently displayed report to the clipboard.
- Opens the standard Windows UAC slider when requested.
- Records timestamped startup, scan, launch, shortcut creation and error details in a local activity log.
