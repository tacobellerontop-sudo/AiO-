using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace AiO.App.Controls;

/// <summary>"Label ─────●──── value" row used throughout the settings pages.</summary>
public sealed class SliderRow : Grid
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(SliderRow), new PropertyMetadata(""));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(SliderRow),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((SliderRow)d).UpdateText()));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(SliderRow), new PropertyMetadata(0.0));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(SliderRow), new PropertyMetadata(100.0));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(double), typeof(SliderRow), new PropertyMetadata(1.0));

    public static readonly DependencyProperty FormatProperty =
        DependencyProperty.Register(nameof(Format), typeof(string), typeof(SliderRow), new PropertyMetadata("{0:0}", (d, _) => ((SliderRow)d).UpdateText()));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    /// <summary>Composite format for the value, e.g. "{0:0} K" or "{0:0.00}".</summary>
    public string Format { get => (string)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }

    private readonly TextBlock _value;

    public SliderRow()
    {
        Margin = new Thickness(0, 4, 0, 4);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        var label = new TextBlock { Style = (Style)Application.Current.FindResource("Label") };
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(Label)) { Source = this });
        Children.Add(label);

        var slider = new Slider();
        slider.SetBinding(RangeBase.ValueProperty, new Binding(nameof(Value)) { Source = this, Mode = BindingMode.TwoWay });
        slider.SetBinding(RangeBase.MinimumProperty, new Binding(nameof(Minimum)) { Source = this });
        slider.SetBinding(RangeBase.MaximumProperty, new Binding(nameof(Maximum)) { Source = this });
        slider.SetBinding(Slider.TickFrequencyProperty, new Binding(nameof(Step)) { Source = this });
        slider.IsSnapToTickEnabled = true;
        SetColumn(slider, 1);
        Children.Add(slider);

        _value = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        SetColumn(_value, 2);
        Children.Add(_value);
        UpdateText();
    }

    private void UpdateText()
    {
        if (_value is not null) _value.Text = string.Format(Format, Value);
    }
}
