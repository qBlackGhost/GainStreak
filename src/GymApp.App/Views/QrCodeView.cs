using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using QRCoder;

namespace GymApp.App.Views;

/// <summary>A QR code for <see cref="Text"/> (Phase 21's invite link), black on white with its quiet zone, in both
/// themes: scanners need the contrast. Square, as big as the space it's given; modules snap to whole pixels.</summary>
public class QrCodeView : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<QrCodeView, string?>(nameof(Text));

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    private List<BitArray>? _modules;

    static QrCodeView() => AffectsRender<QrCodeView>(TextProperty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TextProperty) return;
        _modules = null;
        if (Text is { Length: > 0 } text)
        {
            using var data = QRCodeGenerator.GenerateQrCode(text, QRCodeGenerator.ECCLevel.M);
            _modules = data.ModuleMatrix.Select(row => new BitArray(row)).ToList();   // includes the 4-module quiet zone
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Math.Min(availableSize.Width, availableSize.Height);
        if (double.IsInfinity(side)) side = double.IsInfinity(availableSize.Width) ? availableSize.Height : availableSize.Width;
        if (double.IsInfinity(side)) side = 240;
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        context.FillRectangle(Brushes.White, new Rect(0, 0, side, side));
        if (_modules is not { Count: > 0 } modules) return;

        var n = modules.Count;
        var cell = Math.Max(1, Math.Floor(side / n));
        var offset = Math.Floor((side - cell * n) / 2);
        for (var y = 0; y < n; y++)
        {
            var row = modules[y];
            for (var x = 0; x < n; x++)
            {
                if (!row[x]) continue;
                var start = x;
                while (x + 1 < n && row[x + 1]) x++;   // one rectangle per run
                context.FillRectangle(Brushes.Black, new Rect(offset + start * cell, offset + y * cell, (x - start + 1) * cell, cell));
            }
        }
    }
}
