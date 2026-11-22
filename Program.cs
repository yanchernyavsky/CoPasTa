using System;
using System.Windows.Forms;

namespace CoPasTa;

internal static class Program
{
    internal const string AppName = "CoPasTa";

    [STAThread]
    private static void Main()
    {
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (string.Equals(arg, "--log", StringComparison.OrdinalIgnoreCase))
                DebugLog.Enabled = true;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayContext());
    }
}
