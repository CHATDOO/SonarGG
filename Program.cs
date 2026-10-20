using System;
using System.IO;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace SonarGG;

public static class Program
{
    private static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sonargg_startup.log");

    public static void Log(string msg)
    {
        try
        {
            File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\r\n");
        }
        catch { }
    }

    [STAThread]
    public static void Main()
    {
        Log("=== SonarGG Started ===");
        try
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Log($"AppDomain UnhandledException: {e.ExceptionObject}");
            };

            var app = new App();
            app.InitializeComponent();

            var win = new MainWindow();
            win.Show();

            app.Run(win);
        }
        catch (Exception ex)
        {
            Log($"Fatal crash in Main: {ex}");
            MessageBox.Show($"Crash critique SonarGG:\n\n{ex}", "Erreur SonarGG", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
