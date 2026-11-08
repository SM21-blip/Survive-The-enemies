using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

internal static class Launcher
{
    private static int Main()
    {
        string appletPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VirtualFolderApplet.cpl");
        if (!File.Exists(appletPath))
        {
            MessageBox.Show("The Virtual Folder Utility applet is missing from this package.", "Virtual Folder Utility",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "rundll32.exe"),
                Arguments = "\"" + appletPath + "\",RunUtility",
                UseShellExecute = false
            });
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show("Virtual Folder Utility could not be started.\r\n\r\n" + exception.Message,
                "Virtual Folder Utility", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
