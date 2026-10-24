using System.Windows.Controls;
using System.Windows.Input;
using AiO.App.ViewModels;
using AiO.Core.Keyboard;

namespace AiO.App.Views;

public partial class KeyboardPage : UserControl
{
    public KeyboardPage() => InitializeComponent();

    /// <summary>While recording, the next non-modifier key press (plus held modifiers) fills the editor.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not KeyboardViewModel { Editor: { IsRecording: true } editor }) return;

        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        e.Handled = true;
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            editor.Recording = "";
            return;
        }
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (VirtualKeys.IsModifier(vk)) return; // wait for the main key

        var m = Keyboard.Modifiers;
        var modifiers = (m.HasFlag(ModifierKeys.Control) ? KeyModifiers.Ctrl : 0)
                        | (m.HasFlag(ModifierKeys.Alt) ? KeyModifiers.Alt : 0)
                        | (m.HasFlag(ModifierKeys.Shift) ? KeyModifiers.Shift : 0)
                        | (m.HasFlag(ModifierKeys.Windows) ? KeyModifiers.Win : 0);
        editor.Record(vk, modifiers);
    }
}
