using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using OBDWorkbench.Services;

namespace OBDWorkbench;

public partial class App : Application
{
    private static bool _handlingDispatcherException;

    private static string CrashLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OBDWorkbench");

    private static string CrashLogPath => Path.Combine(CrashLogDirectory, "startup_crash.txt");

    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        try
        {
            Directory.CreateDirectory(CrashLogDirectory);
            File.WriteAllText(CrashLogPath,
                $"OBD Workbench startup log\r\nStarted: {DateTime.Now:O}\r\nOS: {Environment.OSVersion}\r\n.NET: {Environment.Version}\r\n\r\n");
        }
        catch
        {
            // Logging must never prevent application startup.
        }

        // Apply shared resources before constructing any Window or control. This avoids
        // a light first frame while settings are loaded later by MainWindow.
        var settings = new AppSettingsStore(WindowsStoragePaths.Settings()).Load();
        ThemeService.ApplyApplication(ThemeService.UseDark(settings), settings.UseLcarsSkin);

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var splash = new SplashWindow();
        splash.Show();
        try
        {
            // Choose the session before constructing or initializing any transport UI.
            var chosen = new TaskCompletionSource<StartupConnectionFamily?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var proceed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            splash.ConnectionModeSelected += mode => chosen.TrySetResult(mode);
            splash.ReplayRequested += () => chosen.TrySetResult(null);
            splash.SkipRequested += () => proceed.TrySetResult(true);
            splash.Closed += (_, _) => { chosen.TrySetCanceled(); proceed.TrySetCanceled(); };
            StartupConnectionFamily? mode = await chosen.Task;
            var mainWindow = new MainWindow(replayMode: !mode.HasValue) { StartupOwner = splash };
            MainWindow = mainWindow;
            mainWindow.StartupMonitorWindow = splash;

            if (mode.HasValue)
            {
                void Ready() => proceed.TrySetResult(true);
                void Status(string text) => splash.SetStatus(text);
                mainWindow.StartupDataReady += Ready;
                mainWindow.StartupStatusChanged += Status;
                mainWindow.InitializeWorkspace();
                mainWindow.SelectStartupConnectionFamily(mode.Value);
                var panelParent = (System.Windows.Controls.Panel)mainWindow.ConnectionPanel.Parent;
                int panelIndex = panelParent.Children.IndexOf(mainWindow.ConnectionPanel);
                panelParent.Children.Remove(mainWindow.ConnectionPanel);
                mainWindow.ConnectionPanel.DataContext = mainWindow;
                var originalPanelBackground = mainWindow.ConnectionPanel.Background;
                mainWindow.ConnectionPanel.Background = System.Windows.Media.Brushes.Transparent;
                splash.ConnectionHost.Content = mainWindow.ConnectionPanel;
                mainWindow.ShowStartupConnectionPanel();
                splash.StopRequested += async () => await mainWindow.StopConnectionSearchAsync();
                async Task AttemptConnection()
                {
                    try { await mainWindow.TryStartupConnectAsync(); }
                    catch (Exception ex) { LogException("Startup connection", ex); if (!proceed.Task.IsCompleted) splash.SetStatus("Connection unavailable. Adjust settings and retry, or choose Skip to continue."); }
                }
                if (!proceed.Task.IsCompleted) _ = AttemptConnection();
                await proceed.Task;
                mainWindow.StartupDataReady -= Ready;
                mainWindow.StartupStatusChanged -= Status;
                splash.ConnectionHost.Content = null;
                mainWindow.ConnectionPanel.Background = originalPanelBackground;
                panelParent.Children.Insert(panelIndex, mainWindow.ConnectionPanel);
                mainWindow.FinishSplash();
            }
            else mainWindow.StartupOwner = null;

            // Keep the splash handle available while the main window chooses its monitor.
            mainWindow.Show();
            splash.Hide();
            mainWindow.Activate();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (OperationCanceledException)
        {
            Shutdown();
        }
        catch (Exception ex)
        {
            LogException("Startup", ex);
            MessageBox.Show($"Unable to start OBD WorkBench: {ex.Message}\n\n{CrashLogPath}", "Startup error");
            Shutdown(-1);
        }
        finally { splash.Close(); }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // WPF can raise the same layout/binding exception repeatedly while unwinding.
        // Log everything, but only ever show one error dialog.
        LogException("DispatcherUnhandledException", e.Exception);
        e.Handled = true;

        if (_handlingDispatcherException)
            return;

        _handlingDispatcherException = true;
        MessageBox.Show(
            $"OBD Workbench encountered an unexpected error.\n\n{e.Exception.Message}\n\nA crash log was written to:\n{CrashLogPath}",
            "OBD Workbench error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(-1);
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogException("AppDomain.UnhandledException", ex);
        else
            LogText($"AppDomain.UnhandledException: {e.ExceptionObject}");
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("TaskScheduler.UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void LogException(string source, Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{DateTime.Now:O}] {source}");
        sb.AppendLine(ex.ToString());
        sb.AppendLine();
        LogText(sb.ToString());
    }

    private static void LogText(string text)
    {
        try
        {
            Directory.CreateDirectory(CrashLogDirectory);
            File.AppendAllText(CrashLogPath, text + Environment.NewLine);
        }
        catch
        {
            // Last-resort logging path; do not throw while handling another exception.
        }
    }
}
