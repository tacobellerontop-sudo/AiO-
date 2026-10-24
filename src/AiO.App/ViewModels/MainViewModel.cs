using System.Windows.Threading;
using AiO.App.Infrastructure;

namespace AiO.App.ViewModels;

public sealed class NavItem(string title, string glyph, Func<object> createPage)
{
    private object? _page;

    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    /// <summary>Created on first visit so hardware is only probed for pages the user opens.</summary>
    public object Page => _page ??= createPage();
}

public sealed class MainViewModel : ObservableObject
{
    private NavItem? _selected;
    private string _status = "";
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(8) };

    public IReadOnlyList<NavItem> Pages { get; }

    public AsusViewModel? Asus { get; }

    public const string AsusPageTitle = "ASUS · G-Helper";

    public MainViewModel()
    {
        try
        {
            if (AppServices.Instance.Asus is { } asus) Asus = new AsusViewModel(asus);
        }
        catch (Exception ex)
        {
            AppServices.Instance.Report($"ASUS controls unavailable: {ex.Message}");
        }

        // Segoe Fluent Icons / MDL2 glyphs.
        var pages = new List<NavItem>
        {
            new("Displays", "\uE7F4", () => new Views.DisplaysPage { DataContext = new DisplaysViewModel() }),
            new("Colour", "\uE790", () => new Views.ColorPage { DataContext = new ColorViewModel() }),
            new("Razer & RGB", "\uE962", () => new Views.RazerPage { DataContext = new RazerViewModel() }),
        };
        // Always listed so it's discoverable; explains itself when the laptop isn't supported.
        pages.Add(Asus is not null
            ? new(AsusPageTitle, "\uE7F8", () => new Views.AsusPage { DataContext = Asus })
            : new(AsusPageTitle, "\uE7F8", () => new Views.AsusUnavailablePage { DataContext = new AsusUnavailableViewModel() }));
        pages.Add(new("Settings", "\uE713", () => new Views.SettingsPage { DataContext = new SettingsViewModel() }));
        Pages = pages;
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

    private void ShowStatus(string message)
    {
        Status = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
