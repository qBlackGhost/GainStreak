using System.Threading.Tasks;

namespace GymApp.App;

/// <summary>
/// Reads a food's barcode with the phone's camera (Phase 26). Each head has its own: iOS shows an AVFoundation camera
/// screen over the app, Android hands over to Google's code scanner. The desktop has none, so the Add food sheet hides
/// its barcode button there (typing the number still works).
/// </summary>
public interface IBarcodeScanner
{
    /// <summary>Whether this head can scan at all. False on the desktop.</summary>
    bool IsSupported { get; }

    /// <summary>Opens the camera until a barcode is read (EAN-13, EAN-8, UPC-A, UPC-E) and returns its digits; null when
    /// the user cancelled or the camera could not be used (then <see cref="Problem"/> says why, if it's worth telling).</summary>
    Task<string?> ScanAsync();

    /// <summary>Set by the last <see cref="ScanAsync"/> when it returned null for a reason the user can act on:
    /// "Camera access is off. Turn it on in Settings, or enter the number." Null after a cancel or a good scan.</summary>
    string? Problem { get; }
}

/// <summary>The desktop's: nothing to scan with.</summary>
public sealed class NoBarcodeScanner : IBarcodeScanner
{
    public bool IsSupported => false;
    public string? Problem => null;
    public Task<string?> ScanAsync() => Task.FromResult<string?>(null);
}
