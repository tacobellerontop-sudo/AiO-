using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AiO.Core.Asus;

namespace AiO.App.Infrastructure;

/// <summary>Notification-area icon with quick actions.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _image;

    public event Action? OpenRequested;
    public event Action? ExitRequested;
    public event Action<AsusPerformanceMode>? PerformanceModeRequested;
    public event Action<string>? ColorPresetRequested;

    public TrayIcon(bool showAsusModes, Func<IEnumerable<string>> colorPresetNames)
    {
        _image = CreateIcon();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open AiO", null, (_, _) => OpenRequested?.Invoke());

        // Rebuilt each time the menu opens so new presets show up.
        var presets = new ToolStripMenuItem("Colour preset");
        presets.DropDownItems.Add("(loading)");
        menu.Opening += (_, _) =>
        {
            presets.DropDownItems.Clear();
            foreach (var name in colorPresetNames())
                presets.DropDownItems.Add(name, null, (_, _) => ColorPresetRequested?.Invoke(name));
        };
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(presets);
        if (showAsusModes)
        {
            menu.Items.Add(new ToolStripSeparator());
            foreach (var mode in new[] { AsusPerformanceMode.Silent, AsusPerformanceMode.Balanced, AsusPerformanceMode.Turbo })
                menu.Items.Add(mode.ToString(), null, (_, _) => PerformanceModeRequested?.Invoke(mode));
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon
        {
            Icon = _image,
            Text = "AiO",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenRequested?.Invoke();
        };
    }

    public void ShowMessage(string title, string text) => _icon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);

    /// <summary>Draws the app icon at runtime so the repo needs no binary assets.</summary>
    public static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var background = new SolidBrush(System.Drawing.Color.FromArgb(59, 130, 246));
            g.FillEllipse(background, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 11, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("AiO", font, Brushes.White, new RectangleF(0, 0, 32, 32), format);
        }
        IntPtr handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}
