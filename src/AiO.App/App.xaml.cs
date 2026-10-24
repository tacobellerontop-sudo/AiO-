using System.Windows;
using AiO.App.Infrastructure;
using AiO.App.ViewModels;
using AiO.App.Views;

namespace AiO.App;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\AiO.SingleInstance";
    private const string ShowEventName = @"Local\AiO.Show";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool first = AcquireInstanceLock(e.Args.Contains(SystemIntegration.RestartedArgument));
        if (!first)
        {
            // Already running: ask that instance to show itself.
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing)) existing.Set();
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        new Thread(() =>
        {
            while (_showEvent.WaitOne()) Dispatcher.BeginInvoke(ShowMainWindow);
        }) { IsBackground = true }.Start();

        DispatcherUnhandledException += (_, args) =>
        {
            AppServices.Instance.Report($"Unexpected error: {args.Exception.Message}");
            args.Handled = true;
        };

        var services = AppServices.Instance;
        services.Settings.Load();
        if (services.Settings.Current.General.ApplyOnStartup) StartupApplier.ApplyAll(services);

        _viewModel = new MainViewModel();
        _tray = new TrayIcon(services.Asus is not null);
        _tray.OpenRequested += ShowMainWindow;
        _tray.ExitRequested += ExitApp;
        _tray.PerformanceModeRequested += mode =>
        {
            if (_viewModel.Asus is { } asus) asus.PerformanceMode = mode;
        };

        bool minimized = e.Args.Contains(SystemIntegration.MinimizedArgument) && services.Settings.Current.General.StartMinimized;
        if (!minimized) ShowMainWindow();
    }

    /// <summary>
    /// Single-instance lock. After "Restart as administrator" the new process waits for the old one
    /// to exit instead of handing over to it.
    /// </summary>
    private bool AcquireInstanceLock(bool waitForPrevious)
    {
        _instanceMutex = new Mutex(false, InstanceMutexName);
        try
        {
            _ownsMutex = _instanceMutex.WaitOne(waitForPrevious ? TimeSpan.FromSeconds(10) : TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true; // the previous instance exited without releasing it
        }
        return _ownsMutex;
    }

    private bool _ownsMutex;

    private void ShowMainWindow()
    {
        if (_window is null)
        {
            _window = new MainWindow { DataContext = _viewModel };
            _window.Closing += (_, args) =>
            {
                if (AppServices.Instance.Settings.Current.General.CloseToTray && !_exiting)
                {
                    args.Cancel = true;
                    _window.Hide();
                    _viewModel?.Asus?.StopSensors();
                }
            };
            _window.Closed += (_, _) =>
            {
                _window = null;
                if (!AppServices.Instance.Settings.Current.General.CloseToTray && !_exiting) ExitApp();
            };
        }
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        if (_viewModel?.SelectedPage?.Title == "ASUS") _viewModel.Asus?.StartSensors();
    }

    private bool _exiting;

    public void ExitApp()
    {
        _exiting = true;
        _window?.Close();
        _tray?.Dispose();
        AppServices.Instance.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
