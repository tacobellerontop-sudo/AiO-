using System.Windows.Controls;
using System.Windows.Input;
using AiO.App.ViewModels;
using AiO.Core.Keyboard;

namespace AiO.App.Views;

public partial class AsusPage : UserControl
{
    public AsusPage() => InitializeComponent();

    /// <summary>While an M-key row is recording, the next non-modifier key press (plus held modifiers) becomes its shortcut.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not AsusViewModel { Hotkeys: { Recording: not null } hotkeys }) return;

        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        e.Handled = true;
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            hotkeys.CancelRecording();
            return;
        }
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (VirtualKeys.IsModifier(vk)) return;

        var m = Keyboard.Modifiers;
        var modifiers = (m.HasFlag(ModifierKeys.Control) ? KeyModifiers.Ctrl : 0)
                        | (m.HasFlag(ModifierKeys.Alt) ? KeyModifiers.Alt : 0)
                        | (m.HasFlag(ModifierKeys.Shift) ? KeyModifiers.Shift : 0)
                        | (m.HasFlag(ModifierKeys.Windows) ? KeyModifiers.Win : 0);
        hotkeys.Record(vk, modifiers);
    }
}
