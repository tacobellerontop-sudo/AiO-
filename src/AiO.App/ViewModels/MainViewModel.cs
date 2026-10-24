using System.Collections.ObjectModel;
using System.Windows.Threading;
using AiO.App.Infrastructure;

namespace AiO.App.ViewModels;

public sealed class NavItem(string title, string glyph, string group, Func<object> createPage)
{
    private object? _page;

    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    /// <summary>Sidebar section heading.</summary>
    public string Group { get; } = group;
    /// <summary>Created on first visit so hardware is only probed for pages the user opens.</summary>
    public object Page => _page ??= createPage();
}

public sealed class MainViewModel : ObservableObject
{
    private NavItem? _selected;
    private string _status = "";
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(8) };

    public ObservableCollection<NavItem> Pages { get; }

    public AsusViewModel? Asus { get; private set; }

    public const string AsusPageTitle = "ASUS · G-Helper";
    private const string DisplayGroup = "DISPLAY", InputGroup = "INPUT", SystemGroup = "SYSTEM";

    /// <summary>Created up front: the keyboard hook runs whether or not its page is open.</summary>
    public KeyboardViewModel Keyboard { get; }

    public string Version => "Version " + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "");

    public MainViewModel()
    {
        LoadAsus();
        Keyboard = new KeyboardViewModel(() => Asus);

        // Segoe Fluent Icons / MDL2 glyphs.
        var pages = new List<NavItem>
        {
            new("Displays", "\uE7F4", DisplayGroup, () => new Views.DisplaysPage { DataContext = new DisplaysViewModel() }),
            new("Colour", "\uE790", DisplayGroup, () => new Views.ColorPage { DataContext = new ColorViewModel() }),
            new("Mouse", "\uE962", InputGroup, () => new Views.MousePage { DataContext = new MouseViewModel() }),
            new("Keyboard", "\uE765", InputGroup, () => new Views.KeyboardPage { DataContext = Keyboard }),
            new("Razer", "\uEA80", InputGroup, () => new Views.RazerPage { DataContext = new RazerViewModel() }),
            // Always listed so it's discoverable; explains itself when the laptop isn't supported.
            CreateAsusPage(),
            new("Settings", "\uE713", SystemGroup, () => new Views.SettingsPage { DataContext = new SettingsViewModel() }),
        };
        Pages = new ObservableCollection<NavItem>(pages);
        _selected = Pages[0];

        _statusTimer.Tick += (_, _) => { Status = ""; _statusTimer.Stop(); };
        AppServices.Instance.StatusReported += message =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => ShowStatus(message));
        if (AppServices.Instance.LastStatus.Length > 0) ShowStatus(AppServices.Instance.LastStatus);
    }

    public NavItem? SelectedPage
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            if (value?.Title == AsusPageTitle) Asus?.StartSensors();
            else Asus?.StopSensors();
        }
    }

    public string Status { get => _status; private set => Set(ref _status, value); }

    private void LoadAsus()
    {
        var services = AppServices.Instance;
        try
        {
            if (services.Asus is { } asus) Asus = new AsusViewModel(asus);
        }
        catch (Exception ex)
        {
            Asus = null;
            services.AsusLoadError = $"{ex.GetType().Name}: {ex.Message}";
            services.Report($"ASUS controls failed to load: {ex.Message}");
        }
    }

    private NavItem CreateAsusPage() => Asus is { } asus
        ? new(AsusPageTitle, "\uE7F8", SystemGroup, () => new Views.AsusPage { DataContext = asus })
        : new(AsusPageTitle, "\uE7F8", SystemGroup, () => new Views.AsusUnavailablePage { DataContext = new AsusUnavailableViewModel(ReloadAsus) });

    /// <summary>Re-detects the ASUS driver and rebuilds the ASUS page in place (no app restart).</summary>
    public void ReloadAsus()
    {
        Asus?.StopSensors();
        Asus = null;
        AppServices.Instance.ReloadAsus();
        LoadAsus();

        int index = Pages.ToList().FindIndex(p => p.Title == AsusPageTitle);
        var page = CreateAsusPage();
        Pages[index] = page;
        SelectedPage = page;
        if (Asus is not null) AppServices.Instance.Report("ASUS controls loaded.");
    }

    private void ShowStatus(string message)
    {
        Status = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
