using System.Runtime.InteropServices;
using Foundation;
using GymApp.App;

namespace GymApp.iOS;

/// <summary>
/// The Home Screen widgets and Control Center controls (Phase 24). The widget extension (src/GymApp.iOS.LiveActivity)
/// reads widget.json from the App Group group.&lt;bundle id&gt;; this saves it there and, when it changed, asks WidgetKit
/// to redraw through the bridge framework's gs_widgets_reload (WidgetKit is Swift only). Without the App Group (a build
/// with -p:WidgetAppGroup=false, or a profile without it) the widgets ask to open the app and nothing is saved.
/// </summary>
public sealed unsafe class IosHomeWidgets : IHomeWidgets
{
    private const string Bridge = "GainStreakLiveActivityBridge";
    private const string FileName = "widget.json";

    private readonly string? _path;
    private readonly delegate* unmanaged[Cdecl]<void> _reload;
    private string? _last;

    public IosHomeWidgets()
    {
        var group = "group." + NSBundle.MainBundle.BundleIdentifier;
        if (NSFileManager.DefaultManager.GetContainerUrl(group)?.Path is { } folder) _path = Path.Combine(folder, FileName);

        var lib = Path.Combine(NSBundle.MainBundle.PrivateFrameworksPath ?? "", $"{Bridge}.framework", Bridge);
        if (NativeLibrary.TryLoad(lib, out var library) && NativeLibrary.TryGetExport(library, "gs_widgets_reload", out var reload))
            _reload = (delegate* unmanaged[Cdecl]<void>)reload;
    }

    public bool IsSupported => _path is not null && OperatingSystem.IsIOSVersionAtLeast(17);

    public void Save(string json)
    {
        if (_path is null || json == _last) return;
        try
        {
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);   // the extension never reads half a file
            _last = json;
            if (_reload != null) _reload();
        }
        catch (IOException) { }   // the next change tries again
    }
}
