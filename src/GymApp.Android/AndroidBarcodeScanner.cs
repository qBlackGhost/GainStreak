using Android.App;
using Android.Content;
using Android.Gms.Tasks;
using GymApp.App;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.CodeScanner;

namespace GymApp.Android;

/// <summary>
/// The food log's barcode scanner on Android (Phase 26): Google's code scanner (Play services), which draws its own camera
/// screen and needs no camera permission. The scanner's module downloads the first time (the manifest asks Play services
/// to fetch it at install); until it has, the scan fails with a message that says to try again or enter the number.
/// </summary>
public sealed class AndroidBarcodeScanner(Context context) : IBarcodeScanner
{
    public bool IsSupported => true;
    public string? Problem { get; private set; }

    public async Task<string?> ScanAsync()
    {
        Problem = null;
        var result = new TaskCompletionSource<string?>();
        try
        {
            var options = new GmsBarcodeScannerOptions.Builder()
                .SetBarcodeFormats(Barcode.FormatEan13, Barcode.FormatEan8, Barcode.FormatUpcA, Barcode.FormatUpcE)!
                .Build();
            var client = GmsBarcodeScanning.GetClient(MainActivity.Current ?? context, options);
            var listener = new Listener(result, this);
            client.StartScan().AddOnSuccessListener(listener).AddOnFailureListener(listener).AddOnCanceledListener(listener);
        }
        catch (Exception)
        {
            Problem = "The scanner couldn't start. Enter the number instead.";
            return null;
        }
        return await result.Task;
    }

    private sealed class Listener(TaskCompletionSource<string?> result, AndroidBarcodeScanner owner)
        : Java.Lang.Object, IOnSuccessListener, IOnFailureListener, IOnCanceledListener
    {
        public void OnSuccess(Java.Lang.Object? barcode) => result.TrySetResult((barcode as Barcode)?.RawValue);

        public void OnFailure(Java.Lang.Exception e)
        {
            // "Waiting for the Barcode UI module to be downloaded": Play services is still fetching the scanner
            owner.Problem = e.Message is { } m && m.Contains("module", StringComparison.OrdinalIgnoreCase)
                ? "The scanner is getting ready. Try again in a minute, or enter the number."
                : "The scanner couldn't start. Enter the number instead.";
            result.TrySetResult(null);
        }

        public void OnCanceled() => result.TrySetResult(null);
    }
}
