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
            new("Displays", "", () => new Views.DisplaysPage { DataContext = new DisplaysViewModel() }),
            new("Colour", "", () => new Views.ColorPage { DataContext = new ColorViewModel() }),
            new("Razer & RGB", "", () => new Views.RazerPage { DataContext = new RazerViewModel() }),
        };
        if (Asus is not null)
            pages.Add(new("ASUS", "", () => new Views.AsusPage { DataContext = Asus }));
        pages.Add(new("Settings", "", () => new Views.SettingsPage { DataContext = new SettingsViewModel() }));
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
            if (value?.Title == "ASUS") Asus?.StartSensors();
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
