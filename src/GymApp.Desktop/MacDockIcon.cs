using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace GymApp.Desktop;

/// <summary>
/// macOS takes the Dock icon from an app bundle's Info.plist. "dotnet run" and the published build start a bare
/// executable with no bundle, so the Dock would show a blank document icon; this sets the GainStreak icon on
/// NSApplication at startup instead. Window.Icon has no effect on macOS. Does nothing on other systems.
/// </summary>
internal static class MacDockIcon
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);

    /// <summary>Call on the UI thread once Avalonia is running. Never throws: a missing icon isn't worth a crash.</summary>
    public static void Apply()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var uri = new Uri($"avares://{typeof(MacDockIcon).Assembly.GetName().Name}/Assets/gainstreak.icns");
            using var stream = AssetLoader.Open(uri);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();

            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                // NSData copies the bytes, so the pin can go right after
                var data = Send(objc_getClass("NSData"), sel_registerName("dataWithBytes:length:"),
                    handle.AddrOfPinnedObject(), (nuint)bytes.Length);
                var image = Send(Send(objc_getClass("NSImage"), sel_registerName("alloc")), sel_registerName("initWithData:"), data);
                if (image == IntPtr.Zero) return;
                var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                Send(app, sel_registerName("setApplicationIconImage:"), image);
            }
            finally
            {
                handle.Free();
            }
        }
        catch (Exception)
        {
            // Keep the default icon
        }
    }
}
