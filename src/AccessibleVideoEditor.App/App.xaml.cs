using System.IO;
using System.Windows;
using System.Windows.Threading;
using AccessibleVideoEditor.App.Services;

namespace AccessibleVideoEditor.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppPaths.Log("Fatal: " + args.ExceptionObject);

        var args = e.Args;
        var selfTest = args.Length > 0 && args[0] == "--self-test";
        if (selfTest)
        {
            SelfTest.LogPath = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.GetFullPath("self-test.log");
        }

        // Colours first, so the window opens in the right theme.
        ThemeService.Apply(this, selfTest ? ThemeService.SystemChoice : AppSettingsTheme());

        var file = !selfTest && args.Length > 0 ? args[0] : null;
        var window = new MainWindow(file, selfTest);
        MainWindow = window;
        window.Show();
    }

    private static string AppSettingsTheme()
    {
        try
        {
            return Core.Settings.AppSettings.Load().Theme;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return ThemeService.SystemChoice;
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppPaths.Log("Unhandled: " + e.Exception);
        if (MainWindow is MainWindow { SelfTestMode: true })
        {
            File.AppendAllText(SelfTest.LogPath, "FAIL unhandled exception: " + e.Exception + Environment.NewLine);
            Shutdown(3);
            e.Handled = true;
            return;
        }

        // Keep working: tell the user, keep the project. The autosave copy protects their work.
        MessageBox.Show(MainWindow!, "Something went wrong: " + e.Exception.Message + "\n\nYour project is still open. Please save it. Details are in the log file.",
            "ClipScribe", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
