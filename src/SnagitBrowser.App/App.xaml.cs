using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

namespace SnagitBrowser.App;

public partial class App : Application
{
    // "Global\" = gilt für alle Benutzersitzungen der Maschine, nicht nur die eigene.
    private const string MutexName = @"Global\SnagitBrowser.SingleInstance";
    // Signal an die laufende Instanz, ihr Fenster nach vorne zu holen (nur innerhalb derselben Sitzung sinnvoll).
    private const string ActivateEventName = "SnagitBrowser.Activate";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Gespeicherte Sprache (Standard Deutsch) vor allen Texten setzen
        Loc.SetLanguage(AppSettings.Load().Language);

        if (!TryAcquireSingleInstance())
        {
            SignalRunningInstance();
            Shutdown();
            return;
        }

        // Zahlen und Datumsangaben in Bindungen im Format der gewählten Sprache (de: 12.540, en: 12,540).
        // Beim Umschalten setzt Loc.SetLanguage die Sprache der offenen Fenster neu.
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(Loc.XmlLanguage));
        base.OnStartup(e);

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        ThreadPool.RegisterWaitForSingleObject(_activateEvent, (_, _) => Dispatcher.BeginInvoke(ActivateMainWindow),
            null, Timeout.Infinite, executeOnlyOnce: false);

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateEvent?.Dispose();
        if (_instanceMutex != null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
        }
        base.OnExit(e);
    }

    private bool TryAcquireSingleInstance()
    {
        try
        {
            var mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return false;
            }
            _instanceMutex = mutex;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Mutex existiert bereits, gehört aber einem anderen Benutzer -> dort läuft schon eine Instanz.
            return false;
        }
    }

    private static void SignalRunningInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var ev))
                using (ev) ev.Set();
            else
                MessageBox.Show(Loc.T("App_AlreadyRunning"),
                    "SnagitBrowser", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (UnauthorizedAccessException) { }
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is not { } w) return;
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Show();
        w.Activate();
        // Trick, damit Windows das Fenster wirklich nach vorne holt
        w.Topmost = true;
        w.Topmost = false;
        w.Focus();
    }
}
