using System.Runtime.InteropServices;

namespace VirtualFolderApplet;

internal static class NativeMethods
{
    internal const int WsOverlappedWindow = 0x00CF0000;
    internal const int WsVisible = 0x10000000;
    internal const int WsChild = 0x40000000;
    internal const int WsTabStop = 0x00010000;
    internal const int EsAutoHScroll = 0x0080;
    internal const int EsReadOnly = 0x0800;
    internal const int EsMultiline = 0x0004;
    internal const int EsWantReturn = 0x1000;
    internal const int WsVScroll = 0x00200000;
    internal const int WsBorder = 0x00800000;
    internal const int LbsNotify = 0x0001;
    internal const int LbsNointegralheight = 0x0100;
    internal const uint WmCreate = 0x0001;
    internal const uint WmDestroy = 0x0002;
    internal const uint WmCommand = 0x0111;
    internal const uint WmSetFont = 0x0030;
    internal const uint WmGetText = 0x000D;
    internal const uint WmGetTextLength = 0x000E;
    internal const uint WmSetText = 0x000C;
    internal const uint WmClose = 0x0010;
    internal const int BnClicked = 0;
    internal const int LbnDblClk = 2;
    internal const int LbAddString = 0x0180;
    internal const int LbResetContent = 0x0184;
    internal const int LbGetCurSel = 0x0188;
    internal const int LbGetText = 0x0189;
    internal const int LbGetTextLen = 0x018A;
    internal const int SwShow = 5;
    internal const int IdOk = 1;
    internal const uint ImageIcon = 1;
    internal const uint LrDefaultSize = 0x0040;
    internal const uint LrShared = 0x8000;

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public nint ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        public nint Window;
        public uint Value;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint LoadLibrary(string fileName);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint LoadImage(nint instance, nint name, uint type, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static extern nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    internal static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", EntryPoint = "UpdateWindow")]
    internal static extern bool UpdateWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    internal static extern int GetMessage(out Message message, nint window, uint filterMin, uint filterMax);

    [DllImport("user32.dll", EntryPoint = "TranslateMessage")]
    internal static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static extern nint DispatchMessage(ref Message message);

    [DllImport("user32.dll", EntryPoint = "PostQuitMessage")]
    internal static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetWindowText(nint window, string text);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", SetLastError = true)]
    internal static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowText(nint window, char[] text, int maximumCount);

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    internal static extern int MessageBox(nint window, string text, string caption, uint type);

    internal static nint CreateControl(
        string className,
        string caption,
        int style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        int controlId = 0)
    {
        return CreateWindowEx(
            0,
            className,
            caption,
            (uint)(WsChild | WsVisible | style),
            x,
            y,
            width,
            height,
            parent,
            (nint)controlId,
            0,
            0);
    }

    internal static void ShowError(nint owner, string message)
    {
        MessageBox(owner, message, "Virtual Folder Utility", 0x10);
    }
}
