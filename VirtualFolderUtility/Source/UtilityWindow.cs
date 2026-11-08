using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace VirtualFolderApplet;

internal static class UtilityWindow
{
    private const string WindowClassName = "VirtualFolderUtilityWindow";
    private const int IdQuery = 1001;
    private const int IdFind = 1002;
    private const int IdDisk = 1003;
    private const int IdUac = 1004;
    private const int IdResults = 1005;
    private const int IdCopy = 1006;
    private const int IdCatalog = 1007;
    private const int IdCreateNamespace = 1008;
    private const int IdLogs = 1009;
    private const int IdNamespaceMap = 1010;
    private const int IdExport = 1011;
    private const int IdBrowseClsid = 1012;
    private const int MaxDisplayedEntries = 2000;
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VirtualFolderUtility",
        "Logs");
    private static readonly string LogPath = Path.Combine(LogDirectory, "utility.log");
    private static readonly object LogSync = new();
    private static nint _window;
    private static nint _query;
    private static nint _results;
    private static nint _status;
    private static string _diskReport = "DISK STORAGE\nDisk information has not been read.";
    private static string _folderReport = "VIRTUAL FOLDERS\nRegistry information has not been read.";
    private static List<CatalogEntry> _catalog = [];

    private sealed record NamespaceRegistration(string Location, string RegistryPath);

    private sealed record CatalogEntry(
        string Name,
        string Id,
        string Kind,
        string? Target,
        string? Arguments,
        IReadOnlyList<NamespaceRegistration>? NamespaceRegistrations = null,
        string? ClassRegistrationPath = null,
        string? ServerPath = null,
        string? ShellFolderAttributes = null);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == NativeMethods.WmCreate)
        {
            CreateControls(window);
            WriteLog(
                "INFO",
                "Startup",
                $"version=1; os={Environment.OSVersion}; architecture={RuntimeInformation.ProcessArchitecture}; app={Environment.ProcessPath}; log_path={LogPath}");
            RefreshDiskInformation();
            RefreshVirtualFolders(string.Empty);
            return 0;
        }

        if (message == NativeMethods.WmCommand)
        {
            int controlId = unchecked((int)(wParam & 0xFFFF));
            int notification = unchecked((int)((wParam >> 16) & 0xFFFF));
            if (controlId == IdFind && notification == NativeMethods.BnClicked)
            {
                RefreshVirtualFolders(GetText(_query));
                return 0;
            }

            if (controlId == IdDisk && notification == NativeMethods.BnClicked)
            {
                RefreshDiskInformation();
                return 0;
            }

            if (controlId == IdUac && notification == NativeMethods.BnClicked)
            {
                OpenUacSettings(window);
                return 0;
            }

            if (controlId == IdCopy && notification == NativeMethods.BnClicked)
            {
                CopyResults(window);
                return 0;
            }

            if (controlId == IdCatalog && notification == NativeMethods.BnClicked)
            {
                RefreshControlPanelCatalog();
                return 0;
            }

            if (controlId == IdCreateNamespace && notification == NativeMethods.BnClicked)
            {
                CreateNamespaceFolder(window);
                return 0;
            }

            if (controlId == IdLogs && notification == NativeMethods.BnClicked)
            {
                OpenLogs(window);
                return 0;
            }

            if (controlId == IdNamespaceMap && notification == NativeMethods.BnClicked)
            {
                ShowNamespaceMap();
                return 0;
            }

            if (controlId == IdExport && notification == NativeMethods.BnClicked)
            {
                ExportCatalog(window);
                return 0;
            }

            if (controlId == IdBrowseClsid && notification == NativeMethods.BnClicked)
            {
                OpenClsid(window, GetText(_query));
                return 0;
            }
        }

        if (message == NativeMethods.WmClose)
        {
            DestroyWindow(window);
            return 0;
        }

        if (message == NativeMethods.WmDestroy)
        {
            _window = 0;
            NativeMethods.PostQuitMessage(0);
            return 0;
        }

        return NativeMethods.DefWindowProc(window, message, wParam, lParam);
    }

    internal static void Open()
    {
        if (_window != 0)
        {
            NativeMethods.ShowWindow(_window, NativeMethods.SwShow);
            return;
        }

        nint instance = NativeMethods.GetModuleHandle("VirtualFolderApplet.cpl");
        RegisterWindowClass(instance);
        _window = NativeMethods.CreateWindowEx(
            0,
            WindowClassName,
            "Virtual Folder Utility",
            (uint)NativeMethods.WsOverlappedWindow,
            100,
            100,
            940,
            690,
            0,
            0,
            instance,
            0);
        if (_window == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the utility window.");
        }

        NativeMethods.ShowWindow(_window, NativeMethods.SwShow);
        NativeMethods.UpdateWindow(_window);

        while (true)
        {
            int result = NativeMethods.GetMessage(out NativeMethods.Message message, 0, 0, 0);
            if (result == 0)
            {
                break;
            }

            if (result == -1)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The utility window message loop failed.");
            }

            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }
    }

    private static unsafe void RegisterWindowClass(nint instance)
    {
        NativeMethods.WindowClass windowClass = new()
        {
            Size = (uint)sizeof(NativeMethods.WindowClass),
            WindowProcedure = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&WindowProcedure,
            Instance = instance,
            Background = (nint)6
        };

        fixed (char* name = WindowClassName)
        {
            windowClass.ClassName = (nint)name;
            if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 1410)
                {
                    throw new Win32Exception(error, "Could not register the utility window.");
                }
            }
        }
    }

    private static void CreateControls(nint window)
    {
        NativeMethods.CreateControl("STATIC", "Find a CLSID or virtual folder name:", 0, 18, 16, 750, 22, window);
        _query = NativeMethods.CreateControl(
            "EDIT",
            string.Empty,
            NativeMethods.WsBorder | NativeMethods.WsTabStop | NativeMethods.EsAutoHScroll,
            18,
            42,
            690,
            28,
            window,
            IdQuery);
        NativeMethods.CreateControl("BUTTON", "Find", NativeMethods.WsTabStop, 720, 42, 90, 28, window, IdFind);
        NativeMethods.CreateControl("BUTTON", "Refresh disks", NativeMethods.WsTabStop, 18, 82, 125, 30, window, IdDisk);
        NativeMethods.CreateControl("BUTTON", "Control Panel catalog", NativeMethods.WsTabStop, 151, 82, 170, 30, window, IdCatalog);
        NativeMethods.CreateControl("BUTTON", "Create namespace folder", NativeMethods.WsTabStop, 329, 82, 190, 30, window, IdCreateNamespace);
        NativeMethods.CreateControl("BUTTON", "Open UAC slider", NativeMethods.WsTabStop, 18, 120, 145, 30, window, IdUac);
        NativeMethods.CreateControl("BUTTON", "Copy report", NativeMethods.WsTabStop, 171, 120, 112, 30, window, IdCopy);
        NativeMethods.CreateControl("BUTTON", "Open logs", NativeMethods.WsTabStop, 291, 120, 100, 30, window, IdLogs);
        NativeMethods.CreateControl("BUTTON", "Browse CLSID", NativeMethods.WsTabStop, 399, 120, 115, 30, window, IdBrowseClsid);
        NativeMethods.CreateControl("BUTTON", "Namespace map", NativeMethods.WsTabStop, 522, 120, 130, 30, window, IdNamespaceMap);
        NativeMethods.CreateControl("BUTTON", "Export CSV", NativeMethods.WsTabStop, 660, 120, 105, 30, window, IdExport);

        _results = NativeMethods.CreateControl(
            "EDIT",
            string.Empty,
            NativeMethods.WsBorder | NativeMethods.EsMultiline | NativeMethods.EsReadOnly | NativeMethods.WsVScroll | NativeMethods.EsWantReturn,
            18,
            160,
            890,
            430,
            window,
            IdResults);
        _status = NativeMethods.CreateControl("STATIC", "Ready", 0, 18, 600, 890, 30, window);
    }

    private static void RefreshDiskInformation()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            List<string> lines = ["DISK STORAGE"];
            List<string> driveDetails = [];
            int readyDrives = 0;
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady)
                {
                    lines.Add($"{drive.Name}  (not ready)");
                    driveDetails.Add($"{drive.Name}:not_ready");
                    continue;
                }

                readyDrives++;
                long used = drive.TotalSize - drive.AvailableFreeSpace;
                lines.Add($"{drive.Name}  {FormatBytes(used)} used / {FormatBytes(drive.TotalSize)} total  ({FormatBytes(drive.AvailableFreeSpace)} free)");
                driveDetails.Add($"{drive.Name}:total={drive.TotalSize};used={used};free={drive.AvailableFreeSpace};format={drive.DriveFormat};type={drive.DriveType}");
            }

            _diskReport = string.Join(Environment.NewLine, lines);
            RefreshResults();
            SetStatus("Disk usage refreshed. No files were changed.");
            stopwatch.Stop();
            WriteLog("INFO", "DiskScanCompleted", $"drives_ready={readyDrives}; drives_seen={lines.Count - 1}; elapsed_ms={stopwatch.ElapsedMilliseconds}; details={string.Join(" || ", driveDetails)}");
        }
        catch (IOException exception)
        {
            _diskReport = $"DISK STORAGE{Environment.NewLine}Could not read disk information: {exception.Message}";
            RefreshResults();
            SetStatus($"Could not read disk information: {exception.Message}");
            WriteLog("ERROR", "DiskScanFailed", exception.ToString());
        }
        catch (UnauthorizedAccessException exception)
        {
            _diskReport = $"DISK STORAGE{Environment.NewLine}Access denied: {exception.Message}";
            RefreshResults();
            SetStatus($"Access denied while reading disk information: {exception.Message}");
            WriteLog("ERROR", "DiskScanFailed", exception.ToString());
        }
    }

    private static void RefreshVirtualFolders(string query)
    {
        try
        {
            WriteLog("INFO", "ClsidSearchStarted", $"query_length={query.Length}");
            _catalog = DiscoverCatalog();
            IEnumerable<CatalogEntry> matches = query.Length == 0
                ? _catalog.Where(IsNamespaceEntry)
                : _catalog.Where(entry => entry.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (entry.ServerPath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (entry.ClassRegistrationPath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (entry.NamespaceRegistrations?.Any(registration =>
                        registration.Location.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || registration.RegistryPath.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? false));
            List<CatalogEntry> orderedMatches = matches.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToList();
            string heading = query.Length == 0 ? "VIRTUAL FOLDERS" : "CLSID SEARCH RESULTS";
            int displayedCount = Math.Min(orderedMatches.Count, MaxDisplayedEntries);
            _folderReport = orderedMatches.Count == 0
                ? $"{heading}{Environment.NewLine}{Environment.NewLine}No matching registry entries were found."
                : $"{heading}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, orderedMatches.Take(displayedCount).Select(FormatCatalogEntry))}"
                    + (orderedMatches.Count > displayedCount ? $"{Environment.NewLine}{Environment.NewLine}Showing the first {displayedCount} of {orderedMatches.Count} results." : string.Empty);
            RefreshResults();
            SetStatus(query.Length == 0
                ? $"Found {orderedMatches.Count} registered namespace item(s). Search by name, CLSID, server DLL, namespace, or registry path."
                : $"Found {orderedMatches.Count} CLSID/CPL item(s) matching your search; showing {displayedCount}. Search by name, CLSID, server DLL, namespace, or registry path.");
            WriteLog("INFO", "ClsidSearchCompleted", $"results={orderedMatches.Count}; displayed={displayedCount}; query_length={query.Length}");
        }
        catch (UnauthorizedAccessException exception)
        {
            _folderReport = $"CLSID SEARCH{Environment.NewLine}Could not read the registry: {exception.Message}";
            RefreshResults();
            SetStatus("Access denied while reading registered CLSIDs.");
            WriteLog("ERROR", "ClsidSearchFailed", exception.ToString());
        }
        catch (IOException exception)
        {
            _folderReport = $"CLSID SEARCH{Environment.NewLine}Could not read the registry: {exception.Message}";
            RefreshResults();
            SetStatus("The CLSID registry query failed.");
            WriteLog("ERROR", "ClsidSearchFailed", exception.ToString());
        }
        catch (SecurityException exception)
        {
            _folderReport = $"CLSID SEARCH{Environment.NewLine}Could not read the registry: {exception.Message}";
            RefreshResults();
            SetStatus("Access was denied while reading registered CLSIDs.");
            WriteLog("ERROR", "ClsidSearchFailed", exception.ToString());
        }
    }

    private static List<CatalogEntry> DiscoverCatalog()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        Dictionary<string, CatalogEntry> entries = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<NamespaceRegistration>> namespaceRegistrations = new(StringComparer.OrdinalIgnoreCase);
        AddNamespaceRegistrations(Registry.LocalMachine, "HKLM", namespaceRegistrations);
        AddNamespaceRegistrations(Registry.CurrentUser, "HKCU", namespaceRegistrations);
        using RegistryKey? clsidRoot = Registry.ClassesRoot.OpenSubKey("CLSID");
        if (clsidRoot is null)
        {
            throw new IOException("HKEY_CLASSES_ROOT\\CLSID is unavailable.");
        }

        foreach (string clsid in clsidRoot.GetSubKeyNames())
        {
            if (!Guid.TryParse(clsid, out _))
            {
                continue;
            }

            using RegistryKey? key = clsidRoot.OpenSubKey(clsid);
            using RegistryKey? shellFolder = key?.OpenSubKey("ShellFolder");
            bool isShellFolder = shellFolder is not null;
            namespaceRegistrations.TryGetValue(NormalizeClsid(clsid), out List<NamespaceRegistration>? registrations);
            if (!isShellFolder && registrations is null)
            {
                continue;
            }

            using RegistryKey? inprocServer = key?.OpenSubKey("InprocServer32");
            using RegistryKey? localServer = key?.OpenSubKey("LocalServer32");
            string name = ResolveRegistryName(key?.GetValue(null) as string, clsid)
                ?? ResolveRegistryName(inprocServer?.GetValue("LocalizedString") as string, clsid)
                ?? ResolveRegistryName(shellFolder?.GetValue("LocalizedResourceName") as string, clsid)
                ?? ResolveRegistryName(localServer?.GetValue("LocalizedString") as string, clsid)
                ?? (registrations is not null ? "Shell namespace item" : "Shell virtual folder");
            string kind = registrations is not null
                ? "Explorer namespace"
                : "Shell virtual folder";
            string? serverPath = NormalizeServerPath(
                inprocServer?.GetValue(null) as string
                ?? localServer?.GetValue(null) as string);
            object? attributesValue = shellFolder?.GetValue("Attributes");
            string? attributes = attributesValue switch
            {
                int value => $"0x{unchecked((uint)value):X8}",
                uint value => $"0x{value:X8}",
                long value => $"0x{unchecked((ulong)value):X16}",
                string value => value,
                _ => null
            };
            string classPath = $@"HKCR\CLSID\{{{NormalizeClsid(clsid)}}}";
            entries[NormalizeClsid(clsid)] = new CatalogEntry(
                name,
                NormalizeClsid(clsid),
                kind,
                null,
                null,
                registrations,
                classPath,
                serverPath,
                attributes);
        }

        foreach (KeyValuePair<string, List<NamespaceRegistration>> registration in namespaceRegistrations)
        {
            if (entries.ContainsKey(registration.Key))
            {
                continue;
            }

            string name = ResolveClsidDisplayName(registration.Key) ?? "Unregistered namespace CLSID";
            entries[registration.Key] = new CatalogEntry(
                name,
                registration.Key,
                "Unresolved namespace registration",
                null,
                null,
                registration.Value,
                $@"HKCR\CLSID\{{{registration.Key}}} (not registered)");
        }

        AddKnownNamespace(entries, namespaceRegistrations, "21EC2020-3AEA-1069-A2DD-08002B30309D", "All Control Panel Items", "Control Panel");
        AddKnownNamespace(entries, namespaceRegistrations, "26EE0668-A00A-44D7-9371-BEB064C98683", "Control Panel Categories", "Control Panel");

        AddRegisteredCplFiles(Registry.LocalMachine, entries);
        AddRegisteredCplFiles(Registry.CurrentUser, entries);
        AddCplFilesFromDirectory(Environment.SystemDirectory, entries);
        string sysWow64 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64");
        if (Directory.Exists(sysWow64))
        {
            AddCplFilesFromDirectory(sysWow64, entries);
        }

        stopwatch.Stop();
        WriteLog(
            "INFO",
            "CatalogScanCompleted",
            $"entries={entries.Count}; shell_namespaces={entries.Values.Count(item => IsNamespaceEntry(item))}; unresolved_namespaces={entries.Values.Count(item => item.Kind == "Unresolved namespace registration")}; cpl_files={entries.Values.Count(item => item.Kind == "Control Panel applet")}; elapsed_ms={stopwatch.ElapsedMilliseconds}");
        return entries.Values.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddNamespaceRegistrations(
        RegistryKey root,
        string hive,
        Dictionary<string, List<NamespaceRegistration>> registrations)
    {
        AddNamespaceSubkeys(root, hive, registrations, "Desktop", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace");
        AddNamespaceSubkeys(root, hive, registrations, "This PC", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace");
        AddNamespaceSubkeys(root, hive, registrations, "Control Panel", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel\NameSpace");
        AddNamespaceSubkeys(root, hive, registrations, "Network", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\NetworkNeighborhood\NameSpace");
        AddNamespaceValueNames(root, hive, registrations, "Desktop icons (current)", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel");
        AddNamespaceValueNames(root, hive, registrations, "Desktop icons (classic)", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\ClassicStartMenu");
    }

    private static void AddNamespaceSubkeys(
        RegistryKey root,
        string hive,
        Dictionary<string, List<NamespaceRegistration>> registrations,
        string location,
        string subkeyPath)
    {
        using RegistryKey? namespaceKey = root.OpenSubKey(subkeyPath);
        if (namespaceKey is null)
        {
            return;
        }

        foreach (string name in namespaceKey.GetSubKeyNames())
        {
            if (Guid.TryParse(name, out _))
            {
                AddNamespaceRegistration(
                    registrations,
                    NormalizeClsid(name),
                    location,
                    $@"{hive}\{subkeyPath}\{{{NormalizeClsid(name)}}}");
            }
        }
    }

    private static void AddNamespaceValueNames(
        RegistryKey root,
        string hive,
        Dictionary<string, List<NamespaceRegistration>> registrations,
        string location,
        string subkeyPath)
    {
        using RegistryKey? namespaceKey = root.OpenSubKey(subkeyPath);
        if (namespaceKey is null)
        {
            return;
        }

        foreach (string name in namespaceKey.GetValueNames())
        {
            if (Guid.TryParse(name, out _))
            {
                AddNamespaceRegistration(
                    registrations,
                    NormalizeClsid(name),
                    location,
                    $@"{hive}\{subkeyPath}\{{{NormalizeClsid(name)}}}");
            }
        }
    }

    private static void AddNamespaceRegistration(
        Dictionary<string, List<NamespaceRegistration>> registrations,
        string clsid,
        string location,
        string registryPath)
    {
        if (!registrations.TryGetValue(clsid, out List<NamespaceRegistration>? locations))
        {
            locations = [];
            registrations.Add(clsid, locations);
        }

        if (!locations.Any(item => item.Location.Equals(location, StringComparison.OrdinalIgnoreCase)
            && item.RegistryPath.Equals(registryPath, StringComparison.OrdinalIgnoreCase)))
        {
            locations.Add(new NamespaceRegistration(location, registryPath));
        }
    }

    private static void AddKnownNamespace(
        Dictionary<string, CatalogEntry> entries,
        Dictionary<string, List<NamespaceRegistration>> registrations,
        string clsid,
        string name,
        string location)
    {
        string normalizedClsid = NormalizeClsid(clsid);
        if (entries.ContainsKey(normalizedClsid))
        {
            return;
        }

        registrations.TryGetValue(normalizedClsid, out List<NamespaceRegistration>? locations);
        locations ??= [new NamespaceRegistration(location, "Built-in Explorer namespace")];
        entries.Add(
            normalizedClsid,
            new CatalogEntry(name, normalizedClsid, "Explorer namespace", null, null, locations));
    }

    private static string NormalizeClsid(string value) =>
        Guid.TryParse(value, out Guid clsid) ? clsid.ToString("D").ToUpperInvariant() : value;

    private static string? NormalizeServerPath(string? server)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            return null;
        }

        string normalized = Environment.ExpandEnvironmentVariables(server.Trim());
        if (normalized.StartsWith('"'))
        {
            int closingQuote = normalized.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                normalized = normalized[1..closingQuote];
            }
        }
        else
        {
            int comma = normalized.IndexOf(',');
            if (comma > 0 && File.Exists(normalized[..comma].Trim()))
            {
                normalized = normalized[..comma].Trim();
            }
        }

        return normalized;
    }

    private static bool IsNamespaceEntry(CatalogEntry entry) =>
        entry.NamespaceRegistrations is { Count: > 0 }
        || entry.Kind.Equals("Shell virtual folder", StringComparison.OrdinalIgnoreCase);

    private static void AddRegisteredCplFiles(RegistryKey root, Dictionary<string, CatalogEntry> entries)
    {
        using RegistryKey? cplKey = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\Cpls");
        if (cplKey is null)
        {
            return;
        }

        foreach (string valueName in cplKey.GetValueNames())
        {
            if (cplKey.GetValue(valueName) is not string registeredPath)
            {
                continue;
            }

            string expandedPath = Environment.ExpandEnvironmentVariables(registeredPath.Trim().Trim('"'));
            string resolvedPath = ResolveCplPath(expandedPath);
            string name = Path.GetFileNameWithoutExtension(resolvedPath);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileNameWithoutExtension(valueName);
            }

            string id = $"CPL:{root.Name}:{valueName}";
            entries[id] = new CatalogEntry(name, id, "Control Panel applet", resolvedPath, null);
        }
    }

    private static string ResolveCplPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        string systemPath = Path.Combine(Environment.SystemDirectory, path);
        if (File.Exists(systemPath))
        {
            return systemPath;
        }

        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string sysWow64Path = Path.Combine(windowsDirectory, "SysWOW64", path);
        return File.Exists(sysWow64Path) ? sysWow64Path : systemPath;
    }

    private static void AddCplFilesFromDirectory(string directory, Dictionary<string, CatalogEntry> entries)
    {
        foreach (string path in Directory.EnumerateFiles(directory, "*.cpl", SearchOption.TopDirectoryOnly))
        {
            string fullPath = Path.GetFullPath(path);
            string id = $"CPL-FILE:{fullPath}";
            entries.TryAdd(
                id,
                new CatalogEntry(Path.GetFileNameWithoutExtension(path), id, "Control Panel applet", fullPath, null));
        }
    }

    private static string? ResolveClsidDisplayName(string clsid)
    {
        using RegistryKey? key = Registry.ClassesRoot.OpenSubKey($"CLSID\\{{{clsid}}}");
        using RegistryKey? shellFolder = key?.OpenSubKey("ShellFolder");
        using RegistryKey? inprocServer = key?.OpenSubKey("InprocServer32");
        return ResolveRegistryName(key?.GetValue(null) as string, clsid)
            ?? ResolveRegistryName(inprocServer?.GetValue("LocalizedString") as string, clsid)
            ?? ResolveRegistryName(shellFolder?.GetValue("LocalizedResourceName") as string, clsid);
    }

    private static string FormatCatalogEntry(CatalogEntry entry)
    {
        StringBuilder text = new();
        text.Append(entry.Name).Append("  [").Append(entry.Kind).AppendLine("]");
        if (Guid.TryParse(entry.Id, out _))
        {
            text.Append("  CLSID: {").Append(entry.Id).AppendLine("}");
        }
        else
        {
            text.Append("  ID: ").AppendLine(entry.Id);
        }

        if (entry.NamespaceRegistrations is { Count: > 0 })
        {
            text.Append("  Namespace: ")
                .AppendLine(string.Join(", ", entry.NamespaceRegistrations.Select(item => item.Location).Distinct(StringComparer.OrdinalIgnoreCase)));
        }

        if (entry.ClassRegistrationPath is not null)
        {
            text.Append("  Registry: ").AppendLine(entry.ClassRegistrationPath);
        }

        if (entry.ServerPath is not null)
        {
            text.Append("  COM server: ").Append(entry.ServerPath);
            if (File.Exists(entry.ServerPath))
            {
                text.AppendLine(" (present)");
            }
            else
            {
                text.AppendLine(" (missing)");
            }
        }

        if (entry.ShellFolderAttributes is not null)
        {
            text.Append("  ShellFolder Attributes: ").AppendLine(entry.ShellFolderAttributes);
        }

        if (entry.Target is not null)
        {
            text.Append("  Path: ").AppendLine(entry.Target);
        }

        if (entry.NamespaceRegistrations is { Count: > 0 })
        {
            foreach (NamespaceRegistration registration in entry.NamespaceRegistrations)
            {
                text.Append("  Registered at: ").Append(registration.RegistryPath).AppendLine();
            }
        }

        return text.ToString().TrimEnd();
    }

    private static void RefreshControlPanelCatalog()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            _catalog = DiscoverCatalog();
            List<CatalogEntry> displayed = _catalog.Take(MaxDisplayedEntries).ToList();
            string report = $"EXPLORER SHELL + CONTROL PANEL CATALOG{Environment.NewLine}Found {_catalog.Count} namespace items, ShellFolder CLSIDs, and CPLs. Registry metadata only; nothing was changed.{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, displayed.Select(FormatCatalogEntry))}";
            if (_catalog.Count > displayed.Count)
            {
                report += $"{Environment.NewLine}{Environment.NewLine}Showing the first {displayed.Count} of {_catalog.Count} entries.";
            }

            _folderReport = report;
            RefreshResults();
            stopwatch.Stop();
            SetStatus($"Cataloged {_catalog.Count} shell namespace, ShellFolder, and CPL items in {stopwatch.ElapsedMilliseconds} ms.");
            WriteLog("INFO", "ControlPanelCatalogDisplayed", $"entries={_catalog.Count}; displayed={displayed.Count}; elapsed_ms={stopwatch.ElapsedMilliseconds}");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            _folderReport = $"CONTROL PANEL CATALOG{Environment.NewLine}Catalog scan failed: {exception.Message}";
            RefreshResults();
            SetStatus("Could not complete the Control Panel catalog; see the activity log.");
            WriteLog("ERROR", "ControlPanelCatalogFailed", exception.ToString());
        }
    }

    private static void ShowNamespaceMap()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            _catalog = DiscoverCatalog();
            List<CatalogEntry> mapped = _catalog
                .Where(entry => entry.NamespaceRegistrations is { Count: > 0 })
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            StringBuilder report = new();
            report.AppendLine("EXPLORER NAMESPACE MAP");
            report.AppendLine($"Mapped {mapped.Count} CLSID(s) to Explorer registration locations.");
            report.AppendLine("Each CLSID is read from the registry; no registration settings were changed.");
            report.AppendLine();

            foreach (IGrouping<string, (CatalogEntry Entry, NamespaceRegistration Registration)> location in
                mapped.SelectMany(entry => entry.NamespaceRegistrations!.Select(registration => (Entry: entry, Registration: registration)))
                    .GroupBy(item => item.Registration.Location, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                report.AppendLine($"=== {location.Key} ({location.Count()}) ===");
                foreach ((CatalogEntry entry, NamespaceRegistration registration) in location.OrderBy(
                    item => item.Entry.Name,
                    StringComparer.OrdinalIgnoreCase))
                {
                    report.AppendLine($"{entry.Name}  {{{entry.Id}}}");
                    report.AppendLine($"  Registry: {registration.RegistryPath}");
                    if (entry.ClassRegistrationPath is not null)
                    {
                        report.AppendLine($"  Class: {entry.ClassRegistrationPath}");
                    }

                    if (entry.ServerPath is not null)
                    {
                        report.AppendLine($"  COM server: {entry.ServerPath} ({(File.Exists(entry.ServerPath) ? "present" : "missing")})");
                    }

                    if (entry.ShellFolderAttributes is not null)
                    {
                        report.AppendLine($"  ShellFolder Attributes: {entry.ShellFolderAttributes}");
                    }
                }

                report.AppendLine();
            }

            if (mapped.Count == 0)
            {
                report.AppendLine("No namespace registrations were found in the current or local-machine Explorer registry views.");
            }

            _folderReport = report.ToString().TrimEnd();
            RefreshResults();
            stopwatch.Stop();
            SetStatus($"Mapped {mapped.Count} namespace CLSID(s) in {stopwatch.ElapsedMilliseconds} ms. No administrator access required.");
            WriteLog("INFO", "NamespaceMapDisplayed", $"clsids={mapped.Count}; elapsed_ms={stopwatch.ElapsedMilliseconds}");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            _folderReport = $"EXPLORER NAMESPACE MAP{Environment.NewLine}Map scan failed: {exception.Message}";
            RefreshResults();
            SetStatus("Could not complete the namespace map; see the activity log.");
            WriteLog("ERROR", "NamespaceMapFailed", exception.ToString());
        }
    }

    private static void ExportCatalog(nint owner)
    {
        try
        {
            _catalog = DiscoverCatalog();
            string exportDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VirtualFolderUtility",
                "Exports");
            Directory.CreateDirectory(exportDirectory);
            string exportPath = Path.Combine(exportDirectory, $"namespace-catalog-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            StringBuilder csv = new();
            csv.AppendLine("Name,CLSID,Type,Namespace locations,Namespace registry paths,Class registry path,COM server,Server exists,ShellFolder attributes,CPL path");
            foreach (CatalogEntry entry in _catalog)
            {
                string[] fields =
                [
                    entry.Name,
                    Guid.TryParse(entry.Id, out _) ? $"{{{entry.Id}}}" : entry.Id,
                    entry.Kind,
                    string.Join("; ", entry.NamespaceRegistrations?.Select(item => item.Location).Distinct(StringComparer.OrdinalIgnoreCase) ?? []),
                    string.Join("; ", entry.NamespaceRegistrations?.Select(item => item.RegistryPath) ?? []),
                    entry.ClassRegistrationPath ?? string.Empty,
                    entry.ServerPath ?? string.Empty,
                    entry.ServerPath is not null && File.Exists(entry.ServerPath) ? "Yes" : entry.ServerPath is null ? string.Empty : "No",
                    entry.ShellFolderAttributes ?? string.Empty,
                    entry.Kind == "Control Panel applet" ? entry.Target ?? string.Empty : string.Empty
                ];
                csv.AppendLine(string.Join(",", fields.Select(QuoteCsvField)));
            }

            File.WriteAllText(exportPath, csv.ToString(), new UTF8Encoding(true));
            SetStatus($"Exported {_catalog.Count} catalog entries to {exportPath}");
            WriteLog("INFO", "CatalogExported", $"entries={_catalog.Count}; path={exportPath}");
            try
            {
                Process.Start(new ProcessStartInfo(exportPath) { UseShellExecute = true });
            }
            catch (Win32Exception exception)
            {
                WriteLog("ERROR", "CatalogExportOpenFailed", exception.ToString());
                NativeMethods.ShowError(owner, $"The CSV was exported, but Windows could not open it: {exception.Message}{Environment.NewLine}{exportPath}");
            }
            catch (InvalidOperationException exception)
            {
                WriteLog("ERROR", "CatalogExportOpenFailed", exception.ToString());
                NativeMethods.ShowError(owner, $"The CSV was exported, but Windows could not open it: {exception.Message}{Environment.NewLine}{exportPath}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            SetStatus($"Could not export the catalog: {exception.Message}");
            WriteLog("ERROR", "CatalogExportFailed", exception.ToString());
            NativeMethods.ShowError(owner, $"Could not export the namespace catalog: {exception.Message}");
        }
    }

    private static string QuoteCsvField(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static void OpenClsid(nint owner, string input)
    {
        if (!Guid.TryParse(input, out Guid clsid))
        {
            SetStatus("Enter a valid CLSID or GUID before browsing.");
            NativeMethods.ShowError(owner, "Enter a valid CLSID, for example 21EC2020-3AEA-1069-A2DD-08002B30309D.");
            return;
        }

        string normalizedClsid = clsid.ToString("D").ToUpperInvariant();
        string arguments = $"shell:::{normalizedClsid}";
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), arguments)
            {
                UseShellExecute = true
            });
            SetStatus($"Asked Explorer to open CLSID {{{normalizedClsid}}}. This works for CLSIDs that expose a shell namespace.");
            WriteLog("INFO", "ClsidBrowseRequested", $"clsid={normalizedClsid}");
        }
        catch (Win32Exception exception)
        {
            SetStatus($"Explorer could not open CLSID {{{normalizedClsid}}}: {exception.Message}");
            WriteLog("ERROR", "ClsidBrowseFailed", exception.ToString());
            NativeMethods.ShowError(owner, $"Explorer could not open CLSID {{{normalizedClsid}}}: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            SetStatus($"Explorer could not open CLSID {{{normalizedClsid}}}: {exception.Message}");
            WriteLog("ERROR", "ClsidBrowseFailed", exception.ToString());
            NativeMethods.ShowError(owner, $"Explorer could not open CLSID {{{normalizedClsid}}}: {exception.Message}");
        }
    }

    private static string? ResolveRegistryName(string? value, string clsid)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, clsid, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        StringBuilder localized = new(512);
        int result = LoadIndirectString(value, localized, (uint)localized.Capacity, 0);
        string resolved = result >= 0 ? localized.ToString() : value;
        return string.IsNullOrWhiteSpace(resolved) || string.Equals(resolved, clsid, StringComparison.OrdinalIgnoreCase)
            ? null
            : resolved;
    }

    private static void OpenUacSettings(nint owner)
    {
        WriteLog("INFO", "UacSettingsLaunchRequested", "Launching the standard Windows UAC settings UI; no policy changes are made by this applet.");
        try
        {
            Process.Start(new ProcessStartInfo("UserAccountControlSettings.exe")
            {
                UseShellExecute = true
            });
            SetStatus("Opened Windows User Account Control settings. The slider is unchanged until you choose an option.");
            WriteLog("INFO", "UacSettingsLaunchStarted", "UserAccountControlSettings.exe launched.");
        }
        catch (Win32Exception exception)
        {
            WriteLog("ERROR", "UacSettingsLaunchFailed", exception.ToString());
            NativeMethods.ShowError(owner, $"Could not open Windows UAC settings: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            WriteLog("ERROR", "UacSettingsLaunchFailed", exception.ToString());
            NativeMethods.ShowError(owner, $"Could not open Windows UAC settings: {exception.Message}");
        }
    }

    private static void CopyResults(nint owner)
    {
        string text = GetText(_results);
        if (text.Length == 0)
        {
            SetStatus("There is no report to copy yet.");
            return;
        }

        if (!OpenClipboard(owner))
        {
            int error = Marshal.GetLastWin32Error();
            WriteLog("ERROR", "ClipboardOpenFailed", $"win32_error={error}");
            NativeMethods.ShowError(owner, $"Could not open the clipboard (Windows error {error}).");
            return;
        }

        try
        {
            EmptyClipboard();
            nuint bytes = (nuint)((text.Length + 1) * sizeof(char));
            nint memory = GlobalAlloc(0x0002, bytes);
            if (memory == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not allocate clipboard memory.");
            }

            nint pointer = GlobalLock(memory);
            if (pointer == 0)
            {
                GlobalFree(memory);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not lock clipboard memory.");
            }

            Marshal.Copy((text + '\0').ToCharArray(), 0, pointer, text.Length + 1);
            GlobalUnlock(memory);
            if (SetClipboardData(13, memory) == 0)
            {
                GlobalFree(memory);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not place the report on the clipboard.");
            }

            SetStatus("Report copied to the clipboard.");
            WriteLog("INFO", "ReportCopied", $"characters={text.Length}");
        }
        catch (Win32Exception exception)
        {
            WriteLog("ERROR", "ClipboardCopyFailed", exception.ToString());
            NativeMethods.ShowError(owner, exception.Message);
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static void CreateNamespaceFolder(nint owner)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string? scriptPath = null;
        try
        {
            _catalog = DiscoverCatalog();
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string root = Path.Combine(documents, "Virtual Folder Utility Namespace");
            string shellFolder = Path.Combine(root, "Shell Virtual Folders");
            string controlPanelFolder = Path.Combine(root, "Control Panel Items");
            List<CatalogEntry> shortcutEntries = _catalog
                .Where(entry => IsNamespaceEntry(entry) || entry.Kind == "Control Panel applet")
                .ToList();
            string shortcutManifest = Path.Combine(LogDirectory, "namespace-shortcuts.manifest");
            HashSet<string> previouslyManagedPaths = File.Exists(shortcutManifest)
                ? new HashSet<string>(File.ReadAllLines(shortcutManifest), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Directory.CreateDirectory(shellFolder);
            Directory.CreateDirectory(controlPanelFolder);

            StringBuilder script = new();
            script.AppendLine("$ErrorActionPreference = 'Stop'");
            script.AppendLine("$shell = New-Object -ComObject WScript.Shell");
            script.AppendLine($"$appLog = {QuotePowerShellString(LogPath)}");
            script.AppendLine("function Write-AppLog([string]$Level, [string]$Event, [string]$Details) { Add-Content -LiteralPath $appLog -Value \"$(Get-Date -Format o) [$Level] $Event | $Details\" -Encoding UTF8 }");
            int shortcutCount = 0;
            HashSet<string> usedPaths = new(StringComparer.OrdinalIgnoreCase);
            List<string> managedPaths = [];
            HashSet<string> expectedArguments = new(StringComparer.OrdinalIgnoreCase);
            foreach (CatalogEntry entry in shortcutEntries)
            {
                bool isControlPanelEntry = entry.Kind == "Control Panel applet"
                    || entry.NamespaceRegistrations?.Any(registration =>
                        registration.Location.Equals("Control Panel", StringComparison.OrdinalIgnoreCase)) == true;
                string destination = isControlPanelEntry ? controlPanelFolder : shellFolder;
                string filePath = ReserveShortcutPath(destination, entry.Name, usedPaths, previouslyManagedPaths);
                managedPaths.Add(filePath);
                string target;
                string arguments;
                if (entry.Kind == "Control Panel applet" && entry.Target is not null)
                {
                    target = Path.Combine(Environment.SystemDirectory, "control.exe");
                    arguments = QuoteWindowsArgument(entry.Target);
                }
                else
                {
                    target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    arguments = $"shell:::{NormalizeClsid(entry.Id)}";
                }

                AppendShortcutScript(script, filePath, target, arguments, entry.Name);
                expectedArguments.Add(arguments);
                shortcutCount++;
            }

            string allControlPanel = ReserveShortcutPath(root, "All Control Panel Items", usedPaths, previouslyManagedPaths);
            managedPaths.Add(allControlPanel);
            const string allControlPanelArguments = "shell:::{21EC2020-3AEA-1069-A2DD-08002B30309D}";
            AppendShortcutScript(
                script,
                allControlPanel,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                allControlPanelArguments,
                "Windows All Control Panel Items");
            expectedArguments.Add(allControlPanelArguments);
            shortcutCount++;

            script.AppendLine("$expectedArguments = @{}");
            foreach (string arguments in expectedArguments)
            {
                script.AppendLine($"$expectedArguments[{QuotePowerShellString(arguments)}] = $true");
            }

            script.AppendLine(
                $"$allowedTargets = @({QuotePowerShellString(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))}, {QuotePowerShellString(Path.Combine(Environment.SystemDirectory, "explorer.exe"))}, {QuotePowerShellString(Path.Combine(Environment.SystemDirectory, "control.exe"))})");
            string fullRootPrefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            foreach (string stalePath in previouslyManagedPaths.Except(managedPaths, StringComparer.OrdinalIgnoreCase))
            {
                string fullStalePath = Path.GetFullPath(stalePath);
                if (!fullStalePath.StartsWith(fullRootPrefix, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Path.GetExtension(fullStalePath), ".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    WriteLog("WARN", "StaleShortcutOutsideManagedFolder", $"path={fullStalePath}");
                    continue;
                }

                script.AppendLine($"$stalePath = {QuotePowerShellString(fullStalePath)}");
                script.AppendLine("if (Test-Path -LiteralPath $stalePath -PathType Leaf) {");
                script.AppendLine("  $oldLink = $shell.CreateShortcut($stalePath)");
                script.AppendLine("  if (($allowedTargets -contains $oldLink.TargetPath) -and $expectedArguments.ContainsKey($oldLink.Arguments)) {");
                script.AppendLine("    Remove-Item -LiteralPath $stalePath -Force");
                script.AppendLine("    Write-AppLog 'INFO' 'DuplicateShortcutRemoved' (\"path=$stalePath; arguments=$($oldLink.Arguments)\")");
                script.AppendLine("  } else { Write-AppLog 'WARN' 'UnrecognizedShortcutPreserved' (\"path=$stalePath; target=$($oldLink.TargetPath)\") }");
                script.AppendLine("}");
            }

            scriptPath = Path.Combine(Path.GetTempPath(), $"VirtualFolderUtility-{Guid.NewGuid():N}.ps1");
            File.WriteAllText(scriptPath, script.ToString(), new UTF8Encoding(false));
            WriteLog("INFO", "NamespaceShortcutScriptPrepared", $"catalog_entries={_catalog.Count}; shortcuts={shortcutCount}; script_bytes={new FileInfo(scriptPath).Length}");

            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.StartInfo.ArgumentList.Add("-NoLogo");
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-NonInteractive");
            process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
            process.StartInfo.ArgumentList.Add("Bypass");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(scriptPath);
            if (!process.Start())
            {
                throw new Win32Exception("Could not start Windows PowerShell to create namespace shortcuts.");
            }

            if (!process.WaitForExit(120_000))
            {
                process.Kill();
                throw new TimeoutException("Creating the namespace shortcuts exceeded the two-minute limit.");
            }

            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"PowerShell exited with code {process.ExitCode}.{Environment.NewLine}{standardError}{Environment.NewLine}{standardOutput}");
            }

            string manifestTempPath = shortcutManifest + ".tmp";
            File.WriteAllLines(manifestTempPath, managedPaths, new UTF8Encoding(false));
            File.Move(manifestTempPath, shortcutManifest, true);
            stopwatch.Stop();
            int controlPanelShortcutCount = shortcutEntries.Count(entry => entry.Kind == "Control Panel applet"
                || entry.NamespaceRegistrations?.Any(registration =>
                    registration.Location.Equals("Control Panel", StringComparison.OrdinalIgnoreCase)) == true) + 1;
            _folderReport = $"VIRTUAL NAMESPACE FOLDER CREATED{Environment.NewLine}{Environment.NewLine}Location: {root}{Environment.NewLine}Shortcuts created: {shortcutCount}{Environment.NewLine}Control Panel and CPL shortcuts: {controlPanelShortcutCount}{Environment.NewLine}Other shell namespace shortcuts: {shortcutCount - controlPanelShortcutCount}{Environment.NewLine}{Environment.NewLine}This is a user-level folder of Windows shortcuts; no system-wide Explorer extensions or registry settings were changed.";
            RefreshResults();
            SetStatus($"Created {shortcutCount} shortcuts in Documents\\Virtual Folder Utility Namespace.");
            WriteLog("INFO", "NamespaceFolderCreated", $"path={root}; entries={shortcutEntries.Count}; shortcuts={shortcutCount}; elapsed_ms={stopwatch.ElapsedMilliseconds}; powershell_exit={process.ExitCode}");
            try
            {
                Process.Start(new ProcessStartInfo(root) { UseShellExecute = true });
                WriteLog("INFO", "NamespaceFolderOpened", $"path={root}");
            }
            catch (Win32Exception exception)
            {
                SetStatus($"Created the namespace folder, but could not open it automatically: {exception.Message}");
                WriteLog("ERROR", "NamespaceFolderOpenFailed", exception.ToString());
                NativeMethods.ShowError(owner, $"The shortcuts were created at {root}, but Explorer could not open the folder: {exception.Message}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or Win32Exception or InvalidOperationException or TimeoutException)
        {
            SetStatus($"Could not create the namespace folder: {exception.Message}");
            WriteLog("ERROR", "NamespaceFolderCreationFailed", exception.ToString());
            NativeMethods.ShowError(owner, $"Could not create the namespace folder: {exception.Message}");
        }
        finally
        {
            if (scriptPath is not null && File.Exists(scriptPath))
            {
                try
                {
                    File.Delete(scriptPath);
                    WriteLog("INFO", "NamespaceShortcutScriptRemoved", "Temporary shortcut-generation script removed.");
                }
                catch (IOException exception)
                {
                    WriteLog("ERROR", "NamespaceShortcutScriptCleanupFailed", exception.ToString());
                }
                catch (UnauthorizedAccessException exception)
                {
                    WriteLog("ERROR", "NamespaceShortcutScriptCleanupFailed", exception.ToString());
                }
            }
        }
    }

    private static void AppendShortcutScript(StringBuilder script, string shortcutPath, string target, string arguments, string description)
    {
        script.AppendLine($"$link = $shell.CreateShortcut({QuotePowerShellString(shortcutPath)})");
        script.AppendLine($"$link.TargetPath = {QuotePowerShellString(target)}");
        script.AppendLine($"$link.Arguments = {QuotePowerShellString(arguments)}");
        script.AppendLine($"$link.Description = {QuotePowerShellString(description)}");
        script.AppendLine($"$link.WorkingDirectory = {QuotePowerShellString(Path.GetDirectoryName(target) ?? Environment.SystemDirectory)}");
        script.AppendLine("$link.Save()");
        string details = $"path={shortcutPath}; name={description}; target={target}; arguments={arguments}";
        script.AppendLine($"Write-AppLog 'INFO' 'ShortcutCreated' {QuotePowerShellString(details)}");
    }

    private static string ReserveShortcutPath(
        string directory,
        string displayName,
        HashSet<string> usedPaths,
        HashSet<string> previouslyManagedPaths)
    {
        HashSet<char> invalidCharacters = [.. Path.GetInvalidFileNameChars()];
        HashSet<string> reservedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };
        string safeName = new(displayName.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray());
        safeName = safeName.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "Windows item";
        }
        else if (reservedNames.Contains(safeName.Split('.')[0]))
        {
            safeName += " item";
        }

        if (safeName.Length > 120)
        {
            safeName = safeName[..120].TrimEnd();
        }

        string baseName = Path.Combine(directory, safeName);
        int suffix = 2;
        while (true)
        {
            string candidate = suffix == 2 ? baseName + ".lnk" : $"{baseName} ({suffix - 1}).lnk";
            bool alreadyUsed = !usedPaths.Add(candidate);
            bool existingUnmanagedFile = File.Exists(candidate) && !previouslyManagedPaths.Contains(candidate);
            if (!alreadyUsed && !existingUnmanagedFile)
            {
                return candidate;
            }

            suffix++;
        }
    }

    private static string QuotePowerShellString(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string QuoteWindowsArgument(string argument) => $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static void OpenLogs(nint owner)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            WriteLog("INFO", "LogFolderOpened", $"path={LogDirectory}");
            Process.Start(new ProcessStartInfo(LogDirectory) { UseShellExecute = true });
            SetStatus($"Activity log: {LogPath}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            WriteLog("ERROR", "LogFolderOpenFailed", exception.ToString());
            NativeMethods.ShowError(owner, $"Could not open the activity log folder: {exception.Message}");
        }
    }

    private static void WriteLog(string level, string eventName, string details)
    {
        string line = $"{DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)} [{level}] {eventName} | {details.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}{Environment.NewLine}";
        try
        {
            Directory.CreateDirectory(LogDirectory);
            lock (LogSync)
            {
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
        }
        catch (IOException exception)
        {
            NativeMethods.ShowError(_window, $"Activity logging failed: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            NativeMethods.ShowError(_window, $"Activity logging was denied: {exception.Message}");
        }
    }

    private static string GetText(nint control)
    {
        int length = NativeMethods.GetWindowTextLength(control);
        char[] buffer = new char[length + 1];
        int copied = NativeMethods.GetWindowText(control, buffer, buffer.Length);
        return new string(buffer, 0, copied);
    }

    private static void RefreshResults()
    {
        NativeMethods.SetWindowText(_results, $"{_folderReport}{Environment.NewLine}{Environment.NewLine}{_diskReport}");
    }

    private static void SetStatus(string text)
    {
        NativeMethods.SetWindowText(_status, text);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:N1} {units[unit]}";
    }

    [DllImport("shlwapi.dll", EntryPoint = "SHLoadIndirectString", CharSet = CharSet.Unicode)]
    private static extern int LoadIndirectString(string source, StringBuilder output, uint outputLength, nint reserved);

    [DllImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "OpenClipboard", SetLastError = true)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll", EntryPoint = "EmptyClipboard", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", EntryPoint = "CloseClipboard", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", EntryPoint = "SetClipboardData", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("kernel32.dll", EntryPoint = "GlobalAlloc", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", EntryPoint = "GlobalLock", SetLastError = true)]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll", EntryPoint = "GlobalUnlock", SetLastError = true)]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll", EntryPoint = "GlobalFree", SetLastError = true)]
    private static extern nint GlobalFree(nint memory);
}
