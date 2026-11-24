using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using GymApp.Core;

namespace GymApp.App;

/// <summary>Applies the Theme setting to the whole app (12e). Signed out (the login screens) it's dark, the app's default
/// since 0.27.1; System follows the phone.
/// Every color is a DynamicResource from GainStreakResources' theme dictionaries, so a change shows at once.</summary>
public static class AppTheme
{
    public static void Apply(ThemePreference? preference)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Apply(preference));
            return;
        }
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.System => ThemeVariant.Default,
            _ => ThemeVariant.Dark,
        };
    }
}
