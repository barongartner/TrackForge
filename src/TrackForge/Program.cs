using System.Runtime.InteropServices;
using TrackForge.UI;

namespace TrackForge;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [STAThread]
    private static int Main(string[] args)
    {
        // The djay reader needs the native SQLite provider wired up first.
        try { SQLitePCL.Batteries_V2.Init(); } catch { }

        if (args.Contains("--selftest"))
        {
            AttachConsole(-1);              // -1 = the parent console, if we were launched from one
            return Core.SelfTest.RunAsync(args).GetAwaiter().GetResult();
        }

        if (args.Contains("--install-tools"))
        {
            AttachConsole(-1);
            return InstallTools().GetAwaiter().GetResult();
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash(e.ExceptionObject as Exception);
        Application.ThreadException += (_, e) => Crash(e.Exception);

        Application.Run(new MainForm());
        return 0;
    }

    /// <summary>Headless tool bootstrap, so setup can be scripted or verified.</summary>
    private static async Task<int> InstallTools()
    {
        var progress = new Progress<(double percent, string message)>(p =>
            Console.Write($"\r  {p.message,-58}"));

        try
        {
            Console.WriteLine($"Installing into {Core.ToolInstaller.ToolsDirectory}\n");

            await Core.ToolInstaller.InstallYtDlpAsync(progress);
            Console.WriteLine();
            await Core.ToolInstaller.InstallFfmpegAsync(progress);
            Console.WriteLine();

            Console.WriteLine($"\n  yt-dlp   {(Core.ToolInstaller.HasYtDlp ? "ok" : "MISSING")}");
            Console.WriteLine($"  ffmpeg   {(Core.ToolInstaller.HasFfmpeg ? "ok" : "MISSING")}");
            return Core.ToolInstaller.HasYtDlp && Core.ToolInstaller.HasFfmpeg ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n  failed: {ex.Message}");
            return 1;
        }
    }

    private static void Crash(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            Directory.CreateDirectory(Core.AppConfig.ConfigDirectory);
            var log = Path.Combine(Core.AppConfig.ConfigDirectory, "crash.log");
            File.AppendAllText(log, $"{DateTime.Now:u}\n{ex}\n\n");
            MessageBox.Show($"{ex.Message}\n\nDetails written to:\n{log}",
                "TrackForge hit a problem", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }
}
