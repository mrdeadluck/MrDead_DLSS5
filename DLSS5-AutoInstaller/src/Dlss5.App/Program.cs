using System.Runtime.Versioning;

namespace Dlss5.App;

[SupportedOSPlatform("windows")]
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            MessageBox.Show(
                "Erro inesperado:\r\n\r\n" + (e.ExceptionObject as Exception)?.ToString(),
                "DLSS 5 AutoInstaller", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        Application.ThreadException += (_, e) =>
        {
            MessageBox.Show(
                "Erro inesperado:\r\n\r\n" + e.Exception,
                "DLSS 5 AutoInstaller", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        Application.Run(new MainForm());
    }
}
