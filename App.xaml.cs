using System;
using System.IO;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace SonarGG;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sonargg_debug.log");

    public static void Log(string msg)
    {
        try
        {
            File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\r\n");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (s, args) =>
        {
            Log($"DispatcherUnhandledException: {args.Exception}");
            MessageBox.Show($"Erreur: {args.Exception.Message}\n\nVoir {LogFile}", "Erreur SonarGG");
            args.Handled = true;
        };

        base.OnStartup(e);
    }
}
