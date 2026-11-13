using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using AiO.App.ViewModels;
using AiO.Core.Asus;
using AiO.Core.Audio;
using AiO.Core.Keyboard;

namespace AiO.App.Infrastructure;

/// <summary>
/// Carries out the action of a key mapping. Shared by keyboard remaps and the ASUS M-keys, and safe
/// to call from any thread (UI work is marshalled to the dispatcher).
/// </summary>
public sealed class ActionRunner(Func<AsusViewModel?> asus)
{
    private readonly AppServices _services = AppServices.Instance;

    /// <summary>Called when an action asks to show the AiO window.</summary>
    public Action? OpenAiORequested { get; set; }

    public void Run(KeyMapping m)
    {
        try
        {
            RunCore(m);
        }
        catch (Exception ex)
        {
            _services.Report($"{KeyMapping.DescribeAction(m.Action)} failed: {ex.Message}");
        }
    }

    private void RunCore(KeyMapping m)
    {
        switch (m.Action)
        {
            case KeyActionType.SendKeys when m.Target is { } target:
                KeyboardHook.SendChord(target);
                break;
            case KeyActionType.TypeText when !string.IsNullOrEmpty(m.Text):
                KeyboardHook.TypeText(m.Text);
                break;
            case KeyActionType.Run when !string.IsNullOrWhiteSpace(m.Command):
                try { Process.Start(new ProcessStartInfo(m.Command) { UseShellExecute = true }); }
                catch (Exception ex) { _services.Report($"Couldn't run \"{m.Command}\": {ex.Message}"); }
                break;
            case KeyActionType.CycleAsusMode:
                OnAsus(a => a.PerformanceMode = a.PerformanceMode switch
                {
                    AsusPerformanceMode.Silent => AsusPerformanceMode.Balanced,
                    AsusPerformanceMode.Balanced => AsusPerformanceMode.Turbo,
                    _ => AsusPerformanceMode.Silent,
                });
                break;
            case KeyActionType.NextKeyboardLighting:
                OnAsus(a => a.NextAuraMode());
                break;
            case KeyActionType.KeyboardBrightnessUp:
                OnAsus(a => a.StepKeyboardBrightness(+1));
                break;
            case KeyActionType.KeyboardBrightnessDown:
                OnAsus(a => a.StepKeyboardBrightness(-1));
                break;
            case KeyActionType.ApplyColorPreset:
                if (ColorPresets.Find(m.Text) is { } preset) _ = ColorPresets.ApplyToAllAsync(preset);
                else _services.Report($"No colour preset called \"{m.Text}\".");
                break;
            case KeyActionType.OpenAiO:
                Application.Current.Dispatcher.BeginInvoke(() => OpenAiORequested?.Invoke());
                break;
            case KeyActionType.MicrophoneMute:
                _services.Report(Microphone.ToggleMute() switch
                {
                    true => "Microphone muted.",
                    false => "Microphone on.",
                    null => "No microphone found.",
                });
                break;
            case KeyActionType.LockScreen:
                LockWorkStation();
                break;
            case KeyActionType.ScreenOff:
                // Posted, not sent: a hung window would otherwise block the broadcast.
                PostMessage(new IntPtr(0xFFFF), 0x0112 /* WM_SYSCOMMAND */, new IntPtr(0xF170) /* SC_MONITORPOWER */, new IntPtr(2));
                break;
        }
    }

    private void OnAsus(Action<AsusViewModel> action) =>
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (asus() is { } a) action(a);
            else _services.Report("ASUS controls aren't available.");
        });

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
