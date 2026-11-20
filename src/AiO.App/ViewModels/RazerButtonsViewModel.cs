using System.Collections.ObjectModel;
using AiO.App.Infrastructure;
using AiO.Core.Razer;

namespace AiO.App.ViewModels;

/// <summary>One entry in a button's "function" drop-down.</summary>
public sealed record ButtonChoice(string Label, RazerButtonAction? Action, bool IsKeyboard = false)
{
    public override string ToString() => Label;
}

public sealed record KeyChoice(string Name, byte Usage)
{
    public override string ToString() => Name;
}

/// <summary>One physical button in the button editor.</summary>
public sealed class RazerButtonRowViewModel : ObservableObject
{
    private static readonly IReadOnlyList<ButtonChoice> CommonChoices = BuildCommonChoices();

    public static IReadOnlyList<KeyChoice> AllKeys { get; } = RazerKeys.Keys.Select(k => new KeyChoice(k.Name, k.Usage)).ToList();

    private ButtonChoice _selected;
    private KeyChoice _key;
    private bool _ctrl, _shift, _alt, _win;

    public RazerButtonRowViewModel(RazerButton button, RazerButtonAction current)
    {
        Button = button;
        Current = current;

        var choices = new List<ButtonChoice>();
        // Functions AiO can't edit (DPI cycling, macros, profile switching…) stay selectable so they can be kept.
        var match = CommonChoices.FirstOrDefault(c => c.Action is { } a && a.SameAs(current));
        if (match is null && current.KnownKind != RazerActionKind.Keyboard)
            choices.Add(new ButtonChoice($"Keep: {current.Describe()}", current));
        choices.AddRange(CommonChoices);
        Choices = choices;

        _key = AllKeys[0];
        if (current.KnownKind == RazerActionKind.Keyboard)
        {
            _selected = choices.First(c => c.IsKeyboard);
            _key = AllKeys.FirstOrDefault(k => k.Usage == current.Value(1)) ?? new KeyChoice(RazerKeys.KeyName(current.Value(1)), current.Value(1));
            var mods = (RazerModifiers)current.Value(0);
            _ctrl = mods.HasFlag(RazerModifiers.Ctrl);
            _shift = mods.HasFlag(RazerModifiers.Shift);
            _alt = mods.HasFlag(RazerModifiers.Alt);
            _win = mods.HasFlag(RazerModifiers.Win);
        }
        else
        {
            _selected = match ?? choices[0];
        }
    }

    private static List<ButtonChoice> BuildCommonChoices()
    {
        var list = new List<ButtonChoice>();
        foreach (RazerMouseFunction f in Enum.GetValues<RazerMouseFunction>())
            list.Add(new ButtonChoice(RazerKeys.MouseName(f), RazerButtonAction.Mouse(f)));
        list.Add(new ButtonChoice("Keyboard key…", null, IsKeyboard: true));
        foreach (var (name, usage) in RazerKeys.MediaKeys)
            list.Add(new ButtonChoice($"Media: {name}", RazerButtonAction.Media(usage)));
        list.Add(new ButtonChoice("Hypershift (hold for second layer)", RazerButtonAction.Hypershift()));
        list.Add(new ButtonChoice("Disabled", RazerButtonAction.Disabled()));
        return list;
    }

    public RazerButton Button { get; }
    public string Name => Button.Name;
    public bool Locked => Button.Locked;
    public bool Editable => !Locked;
    public RazerButtonAction Current { get; private set; }
    public IReadOnlyList<ButtonChoice> Choices { get; }
    public IReadOnlyList<KeyChoice> Keys => AllKeys;

    public ButtonChoice Selected
    {
        get => _selected;
        set
        {
            if (value is null || !Set(ref _selected, value)) return;
            OnPropertyChanged(nameof(IsKeyboard));
            OnPropertyChanged(nameof(IsChanged));
        }
    }

    public bool IsKeyboard => Selected.IsKeyboard;

    public KeyChoice Key { get => _key; set { if (value is not null && Set(ref _key, value)) OnPropertyChanged(nameof(IsChanged)); } }
    public bool Ctrl { get => _ctrl; set { if (Set(ref _ctrl, value)) OnPropertyChanged(nameof(IsChanged)); } }
    public bool Shift { get => _shift; set { if (Set(ref _shift, value)) OnPropertyChanged(nameof(IsChanged)); } }
    public bool Alt { get => _alt; set { if (Set(ref _alt, value)) OnPropertyChanged(nameof(IsChanged)); } }
    public bool Win { get => _win; set { if (Set(ref _win, value)) OnPropertyChanged(nameof(IsChanged)); } }

    /// <summary>The function picked in the editor.</summary>
    public RazerButtonAction Chosen
    {
        get
        {
            if (!IsKeyboard) return Selected.Action ?? Current;
            var mods = RazerModifiers.None;
            if (Ctrl) mods |= RazerModifiers.Ctrl;
            if (Shift) mods |= RazerModifiers.Shift;
            if (Alt) mods |= RazerModifiers.Alt;
            if (Win) mods |= RazerModifiers.Win;
            return RazerButtonAction.Key(Key.Usage, mods);
        }
    }

    public bool IsChanged => !Locked && !Chosen.SameAs(Current);

    public string CurrentText => $"On the mouse now: {Current.Describe()}";

    public void MarkSaved(RazerButtonAction saved)
    {
        Current = saved;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(CurrentText));
        OnPropertyChanged(nameof(IsChanged));
    }
}
