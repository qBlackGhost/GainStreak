using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;

namespace GymApp.App.Views;

/// <summary>A time of day picked on two wheels right in the card (0.23.1). <see cref="Time"/> follows the wheels once
/// they settle, so a long scroll is one change (each change of a reminder time saves and reschedules).</summary>
public partial class TimeWheel : UserControl
{
    public const int MinuteStep = 5;

    public static readonly StyledProperty<TimeSpan?> TimeProperty =
        AvaloniaProperty.Register<TimeWheel, TimeSpan?>(nameof(Time), defaultBindingMode: BindingMode.TwoWay);

    public TimeSpan? Time
    {
        get => GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool _showing;

    public TimeWheel()
    {
        InitializeComponent();
        Hours.MinimumValue = 0;
        Hours.MaximumValue = 23;
        Hours.Increment = 1;
        Minutes.MinimumValue = 0;
        Minutes.MaximumValue = 59;
        Minutes.Increment = MinuteStep;
        Show(Time);

        Hours.SelectionChanged += (_, _) => Restart();
        Minutes.SelectionChanged += (_, _) => Restart();
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            var picked = new TimeSpan(Hours.SelectedValue, Minutes.SelectedValue, 0);
            if (picked != Time) SetCurrentValue(TimeProperty, picked);
        };
    }

    private void Restart()
    {
        if (_showing) return;
        _settle.Stop();
        _settle.Start();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TimeProperty) Show(Time);
    }

    /// <summary>Turns the wheels to <paramref name="time"/>, its minutes rounded down to the step.</summary>
    private void Show(TimeSpan? time)
    {
        if (Hours is null || Minutes is null) return;
        var t = time ?? new TimeSpan(18, 0, 0);
        _showing = true;
        try
        {
            if (Hours.SelectedValue != t.Hours) Hours.SelectedValue = t.Hours;
            var minute = t.Minutes / MinuteStep * MinuteStep;
            if (Minutes.SelectedValue != minute) Minutes.SelectedValue = minute;
        }
        finally { _showing = false; }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // A change still settling is kept, not lost with the page
        if (_settle.IsEnabled)
        {
            _settle.Stop();
            var picked = new TimeSpan(Hours.SelectedValue, Minutes.SelectedValue, 0);
            if (picked != Time) SetCurrentValue(TimeProperty, picked);
        }
        base.OnDetachedFromVisualTree(e);
    }
}
