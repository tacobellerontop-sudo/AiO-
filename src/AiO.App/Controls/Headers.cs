using System.Windows;
using System.Windows.Controls;

namespace AiO.App.Controls;

/// <summary>Icon + title + subtitle at the top of a page. Content goes on the right (actions).</summary>
public class PageHeader : ContentControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(PageHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(PageHeader), new PropertyMetadata(""));

    /// <summary>Segoe Fluent Icons glyph.</summary>
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
}

/// <summary>Same idea at card level (styled separately in Theme.xaml).</summary>
public sealed class CardHeader : PageHeader;
