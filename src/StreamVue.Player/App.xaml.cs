using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Application = System.Windows.Application;

namespace StreamVue.Player;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrashLog(args.ExceptionObject as Exception);
        Core.Initialize();
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        WriteCrashLog(e.Exception);

    private static void WriteCrashLog(Exception? exception)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamVue");
            Directory.CreateDirectory(directory);
            var text = new StringBuilder()
                .AppendLine($"[{DateTimeOffset.Now:O}] StreamVue unhandled exception")
                .AppendLine(exception?.ToString() ?? "Unknown exception")
                .AppendLine()
                .ToString();
            File.AppendAllText(Path.Combine(directory, "crash.log"), text);
        }
        catch
        {
            // Crash logging must never mask the original failure.
        }
    }
}
