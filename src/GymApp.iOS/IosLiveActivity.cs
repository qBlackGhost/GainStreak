using System.Runtime.InteropServices;
using System.Text;
using Foundation;
using GymApp.App;

namespace GymApp.iOS;

/// <summary>
/// The live workout's Live Activity (0.20.0): the Dynamic Island and the Lock Screen show the percent done, the next
/// exercise and a running timer once the user leaves the app. ActivityKit is Swift only, so the work is done by
/// GainStreakLiveActivityBridge.framework (src/GymApp.iOS.LiveActivity), whose two C functions this calls through
/// function pointers. iOS before 16.2, or a missing framework, shows nothing.
/// </summary>
public sealed unsafe class IosLiveActivity : ILiveActivity
{
    private const string Bridge = "GainStreakLiveActivityBridge";

    private readonly delegate* unmanaged[Cdecl]<byte*, int, int, int, byte*, double, double, void> _show;
    private readonly delegate* unmanaged[Cdecl]<void> _end;

    public IosLiveActivity()
    {
        if (!OperatingSystem.IsIOSVersionAtLeast(16, 2)) return;
        var path = Path.Combine(NSBundle.MainBundle.PrivateFrameworksPath ?? "", $"{Bridge}.framework", Bridge);
        if (!NativeLibrary.TryLoad(path, out var library)) return;
        if (NativeLibrary.TryGetExport(library, "gs_live_activity_show", out var show) &&
            NativeLibrary.TryGetExport(library, "gs_live_activity_end", out var end))
        {
            _show = (delegate* unmanaged[Cdecl]<byte*, int, int, int, byte*, double, double, void>)show;
            _end = (delegate* unmanaged[Cdecl]<void>)end;
        }
    }

    public void Show(LiveActivityState state)
    {
        if (_show == null) return;
        var day = Utf8(state.DayName);
        var next = state.NextExercise is null ? null : Utf8(state.NextExercise);
        fixed (byte* d = day)
        fixed (byte* n = next)
            _show(d, state.Percent, state.DoneSets, state.PlannedSets, n, UnixSeconds(state.StartedAt), UnixSeconds(state.EndsAt));
    }

    public void End()
    {
        if (_end != null) _end();
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + '\0');

    /// <summary>The app's times are local (AppClock).</summary>
    private static double UnixSeconds(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds() / 1000.0;
}
