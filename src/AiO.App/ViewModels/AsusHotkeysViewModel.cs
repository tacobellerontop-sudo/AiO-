using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using AiO.App.Infrastructure;
using AiO.Core.Asus;
using AiO.Core.Keyboard;
using AiO.Core.Settings;

namespace AiO.App.ViewModels;

/// <summary>
/// Makes the ASUS M-keys and Fn hotkeys do something without Armoury Crate: listens for their
/// firmware events, shows the last key seen (for troubleshooting) and runs the bound action.
/// </summary>
public sealed class AsusHotkeysViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly ActionRunner _runner;
    private AsusKeyListener? _listener;
    private AsusKeyListener? _starting;
    private KeyboardHook? _volumeHook;
    private string _lastEvent = "Press an M-key or Fn hotkey to test it.";
    private int? _unknownCode;
    private HotkeyRowViewModel? _recording;

    public AsusHotkeysViewModel(ActionRunner runner)
    {
        _runner = runner;
        AsusServiceRunning = IsAsusServiceRunning();

        foreach (var key in Enum.GetValues<AsusHotkey>())
            Rows.Add(new HotkeyRowViewModel(this, AsusHotkeyCodes.SettingsKey(key), key, null));
        foreach (var settingsKey in Saved.Hotkeys.Keys.ToList())
            if (AsusHotkeyCodes.CodeFromSettingsKey(settingsKey) is int code)
                Rows.Add(new HotkeyRowViewModel(this, settingsKey, null, code));

        AddUnknownCommand = new RelayCommand(AddUnknown, () => _unknownCode is not null);
        Restart();
    }

    internal AsusSettings Saved => _services.Settings.Current.Asus;

    public ObservableCollection<HotkeyRowViewModel> Rows { get; } = [];

    /// <summary>ASUS's own service turns some keys into on-screen toggles; AiO then leaves M3 alone.</summary>
    public bool AsusServiceRunning { get; }

    public string ServiceNote => AsusServiceRunning
        ? "The ASUS Optimization service is running and already handles the microphone key, so AiO's M3 default is off. Pick an action to override it."
        : "";

    public bool Enabled
    {
        get => Saved.HotkeysEnabled;
        set
        {
            Saved.HotkeysEnabled = value;
            _services.SaveSettings();
            OnPropertyChanged();
            Restart();
        }
    }

    public string ListenerStatus => !Enabled
        ? "Off."
        : _listener is null
            ? "Starting…"
            : $"Listening: WMI {(_listener.WmiListening ? "on" : "unavailable")}, keyboard HID interfaces {_listener.HidInterfaces}.";

    public string LastEvent { get => _lastEvent; private set => Set(ref _lastEvent, value); }

    public bool HasUnknownKey => _unknownCode is not null;
    public string UnknownKeyText => _unknownCode is int c ? $"Add an action for key code {c}" : "";
    public ICommand AddUnknownCommand { get; }

    // ---- listener ---------------------------------------------------------------------

    /// <summary>(Re)starts listening, e.g. after the ASUS driver was reloaded.</summary>
    public void Restart()
    {
        _listener?.Dispose();
        _listener = null;
        _starting = null;
        UpdateVolumeHook();
        OnPropertyChanged(nameof(ListenerStatus));
        if (!Enabled) return;

        var listener = _starting = new AsusKeyListener();
        listener.KeyPressed += OnKey;
        var acpi = _services.Asus?.Acpi;
        // WMI setup can take a moment and wants an MTA thread.
        Task.Run(() =>
        {
            try { listener.Start(acpi); }
            catch (Exception ex) { _services.Report($"ASUS keys: {ex.Message}"); }
        }).ContinueWith(_ => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_starting != listener) { listener.Dispose(); return; } // superseded by a later restart
            _starting = null;
            _listener = listener;
            OnPropertyChanged(nameof(ListenerStatus));
            // The HID side finds its interfaces on its own thread; refresh once it has.
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) => { timer.Stop(); OnPropertyChanged(nameof(ListenerStatus)); };
            timer.Start();
        }));
    }

    private void OnKey(AsusKeyEvent e)
    {
        string settingsKey = AsusHotkeyCodes.SettingsKey(e.Code);
        var mapping = Resolve(settingsKey, e.Key);
        if (mapping is not null) _runner.Run(mapping);

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            string name = e.Key is { } k ? AsusHotkeyCodes.DisplayName(k) : "Unknown key";
            string action = mapping is null ? "nothing" : mapping.Description;
            LastEvent = $"{name} · code {e.Code} via {e.Source} → {action}";
            if (e.Key is null && !Saved.Hotkeys.ContainsKey(settingsKey))
            {
                _unknownCode = e.Code;
                OnPropertyChanged(nameof(HasUnknownKey));
                OnPropertyChanged(nameof(UnknownKeyText));
            }
        });
    }

    /// <summary>The user's binding, else the key's default; null when the key should do nothing.</summary>
    private KeyMapping? Resolve(string settingsKey, AsusHotkey? key)
    {
        if (Saved.Hotkeys.TryGetValue(settingsKey, out var bound))
            return bound.Action == KeyActionType.Disable ? null : bound;
        if (key is { } k && AsusHotkeyCodes.DefaultAction(k, AsusServiceRunning) is { } action)
            return new KeyMapping { Action = action, Target = null };
        return null;
    }

    /// <summary>
    /// M1/M2 reach Windows as Volume Down/Up. When the user rebinds them, a keyboard hook takes over
    /// those two keys.
    /// </summary>
    internal void UpdateVolumeHook()
    {
        var mappings = new List<KeyMapping>();
        if (Enabled)
        {
            foreach (var key in new[] { AsusHotkey.M1, AsusHotkey.M2 })
            {
                if (!Saved.Hotkeys.TryGetValue(AsusHotkeyCodes.SettingsKey(key), out var bound)) continue;
                mappings.Add(new KeyMapping
                {
                    Trigger = new KeyChord(KeyModifiers.None, AsusHotkeyCodes.VirtualKeyOf(key)!.Value),
                    Action = bound.Action,
                    Target = bound.Target,
                    Text = bound.Text,
                    Command = bound.Command,
                });
            }
        }

        if (mappings.Count == 0)
        {
            _volumeHook?.Dispose();
            _volumeHook = null;
        }
        else if (_volumeHook is null)
        {
            _volumeHook = new KeyboardHook(mappings);
            _volumeHook.ActionTriggered += _runner.Run;
        }
        else
        {
            _volumeHook.UpdateMappings(mappings);
        }
    }

    private static bool IsAsusServiceRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("AsusOptimization");
            foreach (var p in processes) p.Dispose();
            return processes.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private void AddUnknown()
    {
        if (_unknownCode is not int code) return;
        string settingsKey = AsusHotkeyCodes.SettingsKey(code);
        Saved.Hotkeys[settingsKey] = new KeyMapping { Action = KeyActionType.Disable, Target = null };
        _services.SaveSettings();
        Rows.Add(new HotkeyRowViewModel(this, settingsKey, null, code));
        _unknownCode = null;
        OnPropertyChanged(nameof(HasUnknownKey));
        OnPropertyChanged(nameof(UnknownKeyText));
    }

    internal void Remove(HotkeyRowViewModel row)
    {
        Saved.Hotkeys.Remove(row.SettingsKey);
        _services.SaveSettings();
        Rows.Remove(row);
    }

    // ---- recording a shortcut -----------------------------------------------------------

    public HotkeyRowViewModel? Recording
    {
        get => _recording;
        internal set
        {
            if (_recording is { } old && old != value) old.IsRecording = false;
            _recording = value;
            if (value is not null) value.IsRecording = true;
            OnPropertyChanged();
        }
    }

    /// <summary>Called by the ASUS page with the next key press while a row is recording.</summary>
    public void Record(int vk, KeyModifiers modifiers)
    {
        if (Recording is not { } row) return;
        Recording = null;
        row.IsRecording = false;
        row.SetTarget(new KeyChord(modifiers, vk));
    }

    public void CancelRecording()
    {
        if (Recording is { } row) row.IsRecording = false;
        Recording = null;
    }

    public void Dispose()
    {
        _starting = null;
        _listener?.Dispose();
        _volumeHook?.Dispose();
    }
}

public sealed record HotkeyActionOption(KeyActionType? Action, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One ASUS key and the action bound to it.</summary>
public sealed class HotkeyRowViewModel : ObservableObject
{
    private readonly AsusHotkeysViewModel _owner;
    private HotkeyActionOption _selected;
    private KeyChord? _target;
    private string _text = "", _command = "";
    private string? _preset;
    private bool _isRecording;

    public HotkeyRowViewModel(AsusHotkeysViewModel owner, string settingsKey, AsusHotkey? key, int? code)
    {
        _owner = owner;
        SettingsKey = settingsKey;
        Key = key;
        Title = key is { } k ? AsusHotkeyCodes.DisplayName(k) : $"Key code {code}";
        Hint = key switch
        {
            AsusHotkey.M1 or AsusHotkey.M2 => "Rebinding takes over every Volume key, on any keyboard.",
            null => "A key AiO doesn't know by name.",
            _ => "",
        };

        string defaultLabel = key is { } dk ? AsusHotkeyCodes.DefaultDescription(dk, owner.AsusServiceRunning) : "Nothing";
        Options =
        [
            new HotkeyActionOption(null, $"Default: {defaultLabel}"),
            .. Enum.GetValues<KeyActionType>().Select(a => new HotkeyActionOption(a, KeyMapping.DescribeAction(a))),
        ];
        PresetNames = ColorPresets.All.Select(p => p.Name).ToList();

        if (owner.Saved.Hotkeys.TryGetValue(settingsKey, out var bound))
        {
            _selected = Options.First(o => o.Action == bound.Action);
            _target = bound.Target;
            _text = bound.Action == KeyActionType.TypeText ? bound.Text ?? "" : "";
            _preset = bound.Action == KeyActionType.ApplyColorPreset ? bound.Text : PresetNames.FirstOrDefault();
            _command = bound.Command ?? "";
        }
        else
        {
            _selected = Options[0];
            _preset = PresetNames.FirstOrDefault();
        }

        RecordCommand = new RelayCommand(() => _owner.Recording = this);
        RemoveCommand = new RelayCommand(() => _owner.Remove(this));
    }

    public string SettingsKey { get; }
    public AsusHotkey? Key { get; }
    public string Title { get; }
    public string Hint { get; }
    public bool HasHint => Hint.Length > 0;
    public bool CanRemove => Key is null;

    public IReadOnlyList<HotkeyActionOption> Options { get; }
    public IReadOnlyList<string> PresetNames { get; }

    public HotkeyActionOption SelectedOption
    {
        get => _selected;
        set
        {
            if (value is null || !Set(ref _selected, value)) return;
            foreach (var name in new[] { nameof(ShowTarget), nameof(ShowText), nameof(ShowCommand), nameof(ShowPreset) })
                OnPropertyChanged(name);
            Save();
        }
    }

    public bool ShowTarget => _selected.Action == KeyActionType.SendKeys;
    public bool ShowText => _selected.Action == KeyActionType.TypeText;
    public bool ShowCommand => _selected.Action == KeyActionType.Run;
    public bool ShowPreset => _selected.Action == KeyActionType.ApplyColorPreset;

    public string TargetText => IsRecording ? "Press a key or shortcut… (Esc cancels)" : _target?.ToString() ?? "(none)";

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (Set(ref _isRecording, value)) OnPropertyChanged(nameof(TargetText));
        }
    }

    public string Text { get => _text; set { if (Set(ref _text, value ?? "")) Save(); } }
    public string Command { get => _command; set { if (Set(ref _command, value ?? "")) Save(); } }
    public string? Preset { get => _preset; set { if (Set(ref _preset, value)) Save(); } }

    public ICommand RecordCommand { get; }
    public ICommand RemoveCommand { get; }

    internal void SetTarget(KeyChord chord)
    {
        _target = chord;
        OnPropertyChanged(nameof(TargetText));
        Save();
    }

    private void Save()
    {
        var hotkeys = _owner.Saved.Hotkeys;
        if (_selected.Action is not { } action)
        {
            // Back to the default. A raw code keeps an entry so its row survives a restart.
            if (Key is null) hotkeys[SettingsKey] = new KeyMapping { Action = KeyActionType.Disable, Target = null };
            else hotkeys.Remove(SettingsKey);
        }
        else
        {
            hotkeys[SettingsKey] = new KeyMapping
            {
                Action = action,
                Target = action == KeyActionType.SendKeys ? _target : null,
                Text = action switch
                {
                    KeyActionType.TypeText => _text,
                    KeyActionType.ApplyColorPreset => _preset,
                    _ => null,
                },
                Command = action == KeyActionType.Run ? _command : null,
            };
        }
        AppServices.Instance.SaveSettings();
        if (Key is AsusHotkey.M1 or AsusHotkey.M2) _owner.UpdateVolumeHook();
    }
}
