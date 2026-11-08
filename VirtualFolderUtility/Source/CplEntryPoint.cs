using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace VirtualFolderApplet;

internal static class CplEntryPoint
{
    private const uint CplInit = 1;
    private const uint CplGetCount = 2;
    private const uint CplInquire = 3;
    private const uint CplDoubleClick = 5;
    private const uint CplStop = 6;
    private const uint CplExit = 7;
    private const uint CplNewInquire = 8;
    private static nint _moriconsModule;
    private static nint _moriconsIcon;

    [StructLayout(LayoutKind.Sequential)]
    private struct CplInfo
    {
        public int IconId;
        public int NameId;
        public int InfoId;
        public nint Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NewCplInfo
    {
        public uint Size;
        public uint Flags;
        public uint HelpContext;
        public nint Data;
        public nint Icon;
        public fixed char Name[32];
        public fixed char Info[64];
        public fixed char HelpFile[260];
    }

    [UnmanagedCallersOnly(EntryPoint = "CPlApplet", CallConvs = [typeof(CallConvStdcall)])]
    public static unsafe int CPlApplet(nint window, uint message, nint appletIndex, nint data)
    {
        switch (message)
        {
            case CplInit:
                return 1;
            case CplGetCount:
                return 1;
            case CplInquire:
                if (data != 0)
                {
                    CplInfo* info = (CplInfo*)data;
                    info->IconId = -1;
                    info->NameId = -1;
                    info->InfoId = -1;
                    info->Data = 0;
                }
                return 0;
            case CplNewInquire:
                if (data != 0)
                {
                    NewCplInfo* info = (NewCplInfo*)data;
                    info->Size = (uint)sizeof(NewCplInfo);
                    info->Flags = 0;
                    info->HelpContext = 0;
                    info->Data = 0;
                    try
                    {
                        info->Icon = LoadMoriconsIcon();
                    }
                    catch (Win32Exception exception)
                    {
                        NativeMethods.ShowError(window, exception.Message);
                    }
                    SetFixedString(info->Name, 32, "Virtual Folder Utility");
                    SetFixedString(info->Info, 64, "Inspect virtual folders, CLSIDs, and disk storage.");
                    SetFixedString(info->HelpFile, 260, string.Empty);
                }
                return 0;
            case CplDoubleClick:
                try
                {
                    UtilityWindow.Open();
                }
                catch (Win32Exception exception)
                {
                    NativeMethods.ShowError(window, exception.Message);
                }
                return 0;
            case CplStop:
            case CplExit:
                return 0;
            default:
                return 0;
        }
    }

    private static nint LoadMoriconsIcon()
    {
        if (_moriconsIcon != 0)
        {
            return _moriconsIcon;
        }

        string iconLibrary = Path.Combine(Environment.SystemDirectory, "moricons.dll");
        _moriconsModule = NativeMethods.LoadLibrary(iconLibrary);
        if (_moriconsModule == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not load the requested Control Panel icon library: {iconLibrary}");
        }

        _moriconsIcon = NativeMethods.LoadImage(
            _moriconsModule,
            (nint)16,
            NativeMethods.ImageIcon,
            0,
            0,
            NativeMethods.LrDefaultSize | NativeMethods.LrShared);
        if (_moriconsIcon == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not load icon resource -16 from {iconLibrary}.");
        }

        return _moriconsIcon;
    }

    [UnmanagedCallersOnly(EntryPoint = "RunUtility", CallConvs = [typeof(CallConvStdcall)])]
    public static void RunUtility(nint window, nint instance, nint commandLine, int showCommand)
    {
        try
        {
            UtilityWindow.Open();
        }
        catch (Win32Exception exception)
        {
            NativeMethods.ShowError(window, exception.Message);
        }
    }

    private static unsafe void SetFixedString(char* destination, int capacity, string value)
    {
        value.AsSpan().CopyTo(new Span<char>(destination, capacity - 1));
        destination[Math.Min(value.Length, capacity - 1)] = '\0';
    }
}
