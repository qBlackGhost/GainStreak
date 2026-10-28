using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Styling;

namespace GymApp.App.Views;

/// <summary>A page transition where the new screen fades in over the old one, which stays solid until it's gone.
/// Unlike a cross-fade, what both screens share (the brand, a switch in the same place) doesn't dim halfway. With no
/// duration it's a plain cut that still leaves both screens fully opaque, which TransitioningContentControl needs
/// (after a cross-fade, the screen switched to next could stay faded out).</summary>
public sealed class FadeIn(TimeSpan duration) : IPageTransition
{
    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (from is not null) from.ZIndex = 0;
        if (to is null) return;
        to.ZIndex = 1;
        to.IsVisible = true;

        if (duration > TimeSpan.Zero)
        {
            var fade = new Animation
            {
                Duration = duration,
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
                },
            };
            await fade.RunAsync(to, cancellationToken);
        }
        to.Opacity = 1;
        if (from is not null)
        {
            from.Opacity = 1;
            from.IsVisible = false;
        }
    }
}
