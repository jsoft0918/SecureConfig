using SecureConfig.Editor.UI;

namespace SecureConfig.Editor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => ShowFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowFatal(e.ExceptionObject as Exception);
        Application.Run(new MainForm());
    }

    private static void ShowFatal(Exception ex)
    {
        MessageBox.Show("An unexpected error occurred.\n\n" + (ex?.Message ?? "Unknown error"),
            "SecureConfig Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
