using System.Runtime.InteropServices;
using System.Text;

namespace WardogsNavigator;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var smokeTest = args.Any(x =>
            x.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase));

        try
        {
            ApplicationConfiguration.Initialize();

            if (smokeTest)
                return RunSmokeTest();

            Application.SetUnhandledExceptionMode(
                UnhandledExceptionMode.CatchException);

            Application.ThreadException += (_, e) =>
                ReportStartupFailure(e.Exception, showDialog: true);

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    ReportStartupFailure(ex, showDialog: false);
            };

            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception ex)
        {
            ReportStartupFailure(ex, showDialog: !smokeTest);
            return 1;
        }
    }

    private static int RunSmokeTest()
    {
        using var form = new MainForm();

        // Force WinForms handle/control initialization without requiring
        // a user interaction. The regular CI launch below separately checks
        // that the published GUI process remains alive after Show/Shown.
        _ = form.Handle;
        form.CreateControl();
        Application.DoEvents();

        return 0;
    }

    private static void ReportStartupFailure(
        Exception exception,
        bool showDialog)
    {
        string? logPath = null;

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "WardogsNavigator");

            Directory.CreateDirectory(dir);
            logPath = Path.Combine(dir, "startup-error.log");

            var text = new StringBuilder()
                .AppendLine("WARDOGS Tactical Navigator startup failure")
                .AppendLine("UTC: " + DateTime.UtcNow.ToString("O"))
                .AppendLine("OS: " + RuntimeInformation.OSDescription)
                .AppendLine("Process: " + RuntimeInformation.ProcessArchitecture)
                .AppendLine(".NET: " + RuntimeInformation.FrameworkDescription)
                .AppendLine()
                .AppendLine(exception.ToString())
                .ToString();

            File.WriteAllText(logPath, text);
        }
        catch
        {
            // Never let diagnostics hide the original startup exception.
        }

        if (!showDialog)
            return;

        try
        {
            MessageBox.Show(
                "WARDOGS 启动失败。\r\n\r\n" +
                exception.Message +
                (string.IsNullOrWhiteSpace(logPath)
                    ? ""
                    : "\r\n\r\n错误日志：\r\n" + logPath),
                "WARDOGS Tactical Navigator",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
        }
    }
}
