using System;
using System.Windows.Forms;

namespace BetterFG.Installer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        ApplicationConfiguration.Initialize();
        Application.Run(new installerform());
    }
}
