using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using AiO.App.Infrastructure;
using AiO.Core.Asus;
using AiO.Core.Keyboard;

namespace AiO.App.ViewModels;

public sealed record KeyItem(int Vk, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Universal key remapping: works on any keyboard through a low-level hook.</summary>
public sealed class KeyboardViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly Func<AsusViewModel?> _asus;
    private KeyboardHook? _hook;
    private KeyMappingViewModel? _editing;
    private bool _isNew;

    public KeyboardViewModel(Func<AsusViewModel?> asus)
    {
        _asus = asus;
        foreach (var m in Saved.Mappings) Mappings.Add(new KeyMappingViewModel(m, this));

        AddCommand = new RelayCommand(() => BeginEdit(new KeyMapping(), isNew: true));
        SaveCommand = new RelayCommand(SaveEdit, () => Editor is not null);
        CancelCommand = new RelayCommand(() => Editor = null);
        PresetCommand = new RelayCommand(p => AddPreset(p as string));

        UpdateHook();
    }

    private KeyboardSettings Saved => _services.Settings.Current.Keyboard;

    /// <summary>Called when a mapping asks to show the AiO window.</summary>
    public Action? OpenAiORequested { get; set; }

    public ObservableCollection<KeyMappingViewModel> Mappings { get; } = [];
    public bool HasMappings => Mappings.Count > 0;

    public bool Enabled
    {
        get => Saved.Enabled;
        set
        {
            Saved.Enabled = value;
            _services.SaveSettings();
            OnPropertyChanged();
            UpdateHook();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText => !Enabled
        ? "Remapping is off."
        : _hook?.IsRunning == true
            ? $"Remapping is on: {Mappings.Count(m => m.Enabled)} active mapping(s)."
            : "Remapping is on but the keyboard hook couldn't start.";

    public string AdminNote => AppServices.IsAdministrator
        ? ""
        : "AiO isn't running as administrator, so remaps won't apply inside apps that are (Task Manager, some games and installers).";

    public ICommand AddCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand PresetCommand { get; }

    /// <summary>The mapping being added or edited, or null when the editor is closed.</summary>
    public MappingEditor? Editor
    {
        get => _editorValue;
        private set
        {
            _editorValue = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditing));
            // Pause remapping while recording keys, so the user records what they physically press.
            _hook?.UpdateMappings(value is null ? ActiveMappings() : []);
        }
    }
    private MappingEditor? _editorValue;
    public bool IsEditing => Editor is not null;

    internal void BeginEdit(KeyMappingViewModel vm) => BeginEdit(vm.Model, isNew: false, vm);

    private void BeginEdit(KeyMapping model, bool isNew, KeyMappingViewModel? existing = null)
    {
        _isNew = isNew;
        _editing = existing;
        Editor = new MappingEditor(model);
    }

    private void SaveEdit()
    {
        if (Editor is null) return;
        var model = Editor.ToModel();
        if (_isNew)
        {
            Saved.Mappings.Add(model);
            Mappings.Add(new KeyMappingViewModel(model, this));
        }
        else if (_editing is not null)
        {
            int index = Saved.Mappings.IndexOf(_editing.Model);
            if (index >= 0) Saved.Mappings[index] = model;
            Mappings[Mappings.IndexOf(_editing)] = new KeyMappingViewModel(model, this);
        }
        Editor = null;
        Changed();
    }

    internal void Delete(KeyMappingViewModel vm)
    {
        Saved.Mappings.Remove(vm.Model);
        Mappings.Remove(vm);
        Changed();
    }

    internal void Changed()
    {
        _services.SaveSettings();
        OnPropertyChanged(nameof(HasMappings));
        UpdateHook();
        OnPropertyChanged(nameof(StatusText));
    }

    private void AddPreset(string? name)
    {
        KeyMapping? m = name switch
        {
            "caps-esc" => new() { Trigger = new(KeyModifiers.None, VirtualKeys.CapsLock), Action = KeyActionType.SendKeys, Target = new(KeyModifiers.None, VirtualKeys.Escape) },
            "caps-ctrl" => new() { Trigger = new(KeyModifiers.None, VirtualKeys.CapsLock), Action = KeyActionType.SendKeys, Target = new(KeyModifiers.None, VirtualKeys.LControl) },
            "no-win" => new() { Trigger = new(KeyModifiers.None, VirtualKeys.LWin), Action = KeyActionType.Disable, Target = null },
            "terminal" => new() { Trigger = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 'T'), Action = KeyActionType.Run, Command = "wt.exe", Target = null },
            "asus-mode" => new() { Trigger = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 'M'), Action = KeyActionType.CycleAsusMode, Target = null },
            "email" => new() { Trigger = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 'E'), Action = KeyActionType.TypeText, Text = "you@example.com", Target = null },
            _ => null,
        };
        if (m is not null) BeginEdit(m, isNew: true);
    }

    private IReadOnlyList<KeyMapping> ActiveMappings() => Saved.Mappings.Where(m => m.Enabled).ToList();

    private void UpdateHook()
    {
        bool want = Saved.Enabled && Saved.Mappings.Any(m => m.Enabled);
        if (!want)
        {
            _hook?.Dispose();
            _hook = null;
            return;
        }
        if (_hook is null)
        {
            _hook = new KeyboardHook(ActiveMappings());
            _hook.ActionTriggered += OnAction;
        }
        else if (Editor is null)
        {
            _hook.UpdateMappings(ActiveMappings());
        }
    }

    private void OnAction(KeyMapping m)
    {
        switch (m.Action)
        {
            case KeyActionType.TypeText when !string.IsNullOrEmpty(m.Text):
                KeyboardHook.TypeText(m.Text);
                break;
            case KeyActionType.Run when !string.IsNullOrWhiteSpace(m.Command):
                try { Process.Start(new ProcessStartInfo(m.Command) { UseShellExecute = true }); }
                catch (Exception ex) { _services.Report($"Couldn't run \"{m.Command}\": {ex.Message}"); }
                break;
            case KeyActionType.CycleAsusMode:
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (_asus() is not { } asus) { _services.Report("ASUS controls aren't available."); return; }
                    asus.PerformanceMode = asus.PerformanceMode switch
                    {
                        AsusPerformanceMode.Silent => AsusPerformanceMode.Balanced,
                        AsusPerformanceMode.Balanced => AsusPerformanceMode.Turbo,
                        _ => AsusPerformanceMode.Silent,
                    };
                });
                break;
            case KeyActionType.OpenAiO:
                Application.Current.Dispatcher.BeginInvoke(() => OpenAiORequested?.Invoke());
                break;
        }
    }

    public void Dispose() => _hook?.Dispose();
}

public sealed class KeyMappingViewModel(KeyMapping model, KeyboardViewModel owner) : ObservableObject
{
    public KeyMapping Model { get; } = model;

    public string Trigger => Model.Trigger.ToString();
    public string Result => Model.Description;
    public string Scope => string.IsNullOrWhiteSpace(Model.AppFilter) ? "Everywhere" : $"Only in {Model.AppFilter}";

    public bool Enabled
    {
        get => Model.Enabled;
        set
        {
            Model.Enabled = value;
            OnPropertyChanged();
            owner.Changed();
        }
    }

    public ICommand EditCommand => new RelayCommand(() => owner.BeginEdit(this));
    public ICommand DeleteCommand => new RelayCommand(() => owner.Delete(this));
}

/// <summary>Editable copy of a mapping; recording fills the trigger or target from a real key press.</summary>
public sealed class MappingEditor : ObservableObject
{
    private bool _triggerCtrl, _triggerAlt, _triggerShift, _triggerWin;
    private bool _targetCtrl, _targetAlt, _targetShift, _targetWin;
    private KeyItem? _triggerKey, _targetKey;
    private KeyActionType _action;
    private string _recording = "";

    public MappingEditor(KeyMapping m)
    {
        Enabled = m.Enabled;
        (_triggerCtrl, _triggerAlt, _triggerShift, _triggerWin) = Split(m.Trigger.Modifiers);
        _triggerKey = Item(m.Trigger.Key);
        _action = m.Action;
        if (m.Target is { } t)
        {
            (_targetCtrl, _targetAlt, _targetShift, _targetWin) = Split(t.Modifiers);
            _targetKey = Item(t.Key);
        }
        Text = m.Text ?? "";
        Command = m.Command ?? "";
        AppFilter = m.AppFilter ?? "";

        RecordTriggerCommand = new RelayCommand(() => Recording = "trigger");
        RecordTargetCommand = new RelayCommand(() => Recording = "target");
    }

    private static (bool, bool, bool, bool) Split(KeyModifiers m) =>
        (m.HasFlag(KeyModifiers.Ctrl), m.HasFlag(KeyModifiers.Alt), m.HasFlag(KeyModifiers.Shift), m.HasFlag(KeyModifiers.Win));

    private static KeyModifiers Join(bool ctrl, bool alt, bool shift, bool win) =>
        (ctrl ? KeyModifiers.Ctrl : 0) | (alt ? KeyModifiers.Alt : 0) | (shift ? KeyModifiers.Shift : 0) | (win ? KeyModifiers.Win : 0);

    public static IReadOnlyList<KeyItem> Keys { get; } = VirtualKeys.Pickable.Select(vk => new KeyItem(vk, VirtualKeys.Name(vk))).ToList();
    private static KeyItem Item(int vk) => Keys.FirstOrDefault(k => k.Vk == vk) ?? new KeyItem(vk, VirtualKeys.Name(vk));

    public IReadOnlyList<KeyActionType> Actions { get; } = Enum.GetValues<KeyActionType>();

    public bool Enabled { get; set; }

    public bool TriggerCtrl { get => _triggerCtrl; set => Set(ref _triggerCtrl, value); }
    public bool TriggerAlt { get => _triggerAlt; set => Set(ref _triggerAlt, value); }
    public bool TriggerShift { get => _triggerShift; set => Set(ref _triggerShift, value); }
    public bool TriggerWin { get => _triggerWin; set => Set(ref _triggerWin, value); }
    public KeyItem? TriggerKey { get => _triggerKey; set => Set(ref _triggerKey, value); }

    public KeyActionType Action
    {
        get => _action;
        set
        {
            if (!Set(ref _action, value)) return;
            OnPropertyChanged(nameof(ShowTarget));
            OnPropertyChanged(nameof(ShowText));
            OnPropertyChanged(nameof(ShowCommand));
        }
    }

    public bool ShowTarget => Action == KeyActionType.SendKeys;
    public bool ShowText => Action == KeyActionType.TypeText;
    public bool ShowCommand => Action == KeyActionType.Run;

    public bool TargetCtrl { get => _targetCtrl; set => Set(ref _targetCtrl, value); }
    public bool TargetAlt { get => _targetAlt; set => Set(ref _targetAlt, value); }
    public bool TargetShift { get => _targetShift; set => Set(ref _targetShift, value); }
    public bool TargetWin { get => _targetWin; set => Set(ref _targetWin, value); }
    public KeyItem? TargetKey { get => _targetKey; set => Set(ref _targetKey, value); }

    public string Text { get; set; }
    public string Command { get; set; }
    public string AppFilter { get; set; }

    /// <summary>"trigger", "target" or "" — which side the next key press fills in.</summary>
    public string Recording
    {
        get => _recording;
        set
        {
            Set(ref _recording, value);
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(RecordingText));
        }
    }

    public bool IsRecording => Recording.Length > 0;
    public string RecordingText => Recording == "" ? "" : $"Press the {(Recording == "trigger" ? "key or shortcut to remap" : "key or shortcut to send")}… (Esc cancels)";

    public ICommand RecordTriggerCommand { get; }
    public ICommand RecordTargetCommand { get; }

    /// <summary>Called by the page with a key press while recording.</summary>
    public void Record(int vk, KeyModifiers modifiers)
    {
        if (Recording == "trigger")
        {
            (TriggerCtrl, TriggerAlt, TriggerShift, TriggerWin) = Split(modifiers);
            TriggerKey = Item(vk);
        }
        else if (Recording == "target")
        {
            (TargetCtrl, TargetAlt, TargetShift, TargetWin) = Split(modifiers);
            TargetKey = Item(vk);
        }
        Recording = "";
    }

    public KeyMapping ToModel() => new()
    {
        Enabled = Enabled,
        Trigger = new KeyChord(Join(TriggerCtrl, TriggerAlt, TriggerShift, TriggerWin), TriggerKey?.Vk ?? VirtualKeys.CapsLock),
        Action = Action,
        Target = Action == KeyActionType.SendKeys && TargetKey is { } key
            ? new KeyChord(Join(TargetCtrl, TargetAlt, TargetShift, TargetWin), key.Vk)
            : null,
        Text = Action == KeyActionType.TypeText ? Text : null,
        Command = Action == KeyActionType.Run ? Command : null,
        AppFilter = string.IsNullOrWhiteSpace(AppFilter) ? null : AppFilter.Trim(),
    };
}
