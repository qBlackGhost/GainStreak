using AVFoundation;
using CoreFoundation;
using CoreGraphics;
using Foundation;
using GymApp.App;
using UIKit;

namespace GymApp.iOS;

/// <summary>
/// The food log's barcode scanner on iOS (Phase 26): a full-screen camera view (AVFoundation, <c>AVCaptureMetadataOutput</c> for
/// EAN-13, EAN-8 and UPC-E; UPC-A comes back as EAN-13) presented over the app, with Cancel and a torch button. Asks for the
/// camera the first time. The Simulator has no camera: the screen still opens, says so, and Cancel closes it.
/// </summary>
public sealed class IosBarcodeScanner : IBarcodeScanner
{
    public bool IsSupported => true;
    public string? Problem { get; private set; }

    private const string Denied = "Camera access is off. Turn it on in Settings, or enter the number.";

    public async Task<string?> ScanAsync()
    {
        Problem = null;
        var camera = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);

        if (camera is not null)
        {
            var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
            if (status == AVAuthorizationStatus.NotDetermined)
            {
                if (!await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video)) { Problem = Denied; return null; }
            }
            else if (status is AVAuthorizationStatus.Denied or AVAuthorizationStatus.Restricted) { Problem = Denied; return null; }
        }

        var result = new TaskCompletionSource<string?>();
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            var top = TopController();
            if (top is null) { result.TrySetResult(null); return; }
            var screen = new ScannerController(camera, result) { ModalPresentationStyle = UIModalPresentationStyle.FullScreen };
            top.PresentViewController(screen, true, null);
        });
        return await result.Task;
    }

    // The controller on top of the key window, so the camera screen opens over whatever is showing
    private static UIViewController? TopController()
    {
        var top = UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>()
            .SelectMany(s => s.Windows).FirstOrDefault(w => w.IsKeyWindow)?.RootViewController;
        while (top?.PresentedViewController is { } next) top = next;
        return top;
    }

    private sealed class ScannerController(AVCaptureDevice? camera, TaskCompletionSource<string?> result) : UIViewController
    {
        private AVCaptureSession? _session;
        private AVCaptureVideoPreviewLayer? _preview;
        private UIButton? _torch;
        private bool _finished;

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            View!.BackgroundColor = UIColor.Black;

            if (camera is not null && StartSession(camera))
            {
                _preview = new AVCaptureVideoPreviewLayer(_session!) { VideoGravity = AVLayerVideoGravity.ResizeAspectFill };
                View.Layer.AddSublayer(_preview);
                AddGuide();
            }
            else
            {
                var note = new UILabel
                {
                    Text = camera is null ? "No camera on this device.\nGo back and enter the number instead." : "The camera couldn't start.\nGo back and enter the number instead.",
                    TextColor = UIColor.White, TextAlignment = UITextAlignment.Center, Lines = 0,
                    Font = UIFont.SystemFontOfSize(16), TranslatesAutoresizingMaskIntoConstraints = false
                };
                View.AddSubview(note);
                NSLayoutConstraint.ActivateConstraints([
                    note.CenterXAnchor.ConstraintEqualTo(View.CenterXAnchor),
                    note.CenterYAnchor.ConstraintEqualTo(View.CenterYAnchor),
                    note.LeadingAnchor.ConstraintGreaterThanOrEqualTo(View.LeadingAnchor, 24)
                ]);
            }

            var cancel = Button("Cancel");
            cancel.AccessibilityLabel = "Cancel scanning";
            cancel.TouchUpInside += (_, _) => Finish(null);
            View.AddSubview(cancel);
            NSLayoutConstraint.ActivateConstraints([
                cancel.LeadingAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.LeadingAnchor, 18),
                cancel.TopAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TopAnchor, 14)
            ]);

            if (camera is { HasTorch: true })
            {
                _torch = Button("Torch");
                _torch.AccessibilityLabel = "Turn the torch on or off";
                _torch.TouchUpInside += (_, _) => ToggleTorch();
                View.AddSubview(_torch);
                NSLayoutConstraint.ActivateConstraints([
                    _torch.TrailingAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TrailingAnchor, -18),
                    _torch.TopAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TopAnchor, 14)
                ]);
            }

            if (_preview is not null)
            {
                var hint = new UILabel
                {
                    Text = "Point the camera at the barcode", TextColor = UIColor.White, TextAlignment = UITextAlignment.Center,
                    Font = UIFont.BoldSystemFontOfSize(15), TranslatesAutoresizingMaskIntoConstraints = false
                };
                View.AddSubview(hint);
                NSLayoutConstraint.ActivateConstraints([
                    hint.CenterXAnchor.ConstraintEqualTo(View.CenterXAnchor),
                    hint.BottomAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.BottomAnchor, -28)
                ]);
            }
        }

        public override void ViewDidLayoutSubviews()
        {
            base.ViewDidLayoutSubviews();
            if (_preview is not null) _preview.Frame = View!.Bounds;
        }

        public override void ViewWillAppear(bool animated)
        {
            base.ViewWillAppear(animated);
            if (_session is { Running: false } s) DispatchQueue.GetGlobalQueue(DispatchQueuePriority.Default).DispatchAsync(s.StartRunning);
        }

        public override void ViewDidDisappear(bool animated)
        {
            base.ViewDidDisappear(animated);
            Stop();
            result.TrySetResult(null);   // closed some other way: a cancel
        }

        private bool StartSession(AVCaptureDevice device)
        {
            var input = AVCaptureDeviceInput.FromDevice(device, out var error);
            if (input is null || error is not null) return false;

            var session = new AVCaptureSession();
            if (!session.CanAddInput(input)) return false;
            session.AddInput(input);

            var output = new AVCaptureMetadataOutput();
            if (!session.CanAddOutput(output)) return false;
            session.AddOutput(output);
            output.SetDelegate(new CodeDelegate(this), DispatchQueue.MainQueue);
            output.MetadataObjectTypes = AVMetadataObjectType.EAN13Code | AVMetadataObjectType.EAN8Code | AVMetadataObjectType.UPCECode;

            _session = session;
            return true;
        }

        private void AddGuide()
        {
            // A thin frame in the middle to aim with
            var guide = new UIView { UserInteractionEnabled = false, TranslatesAutoresizingMaskIntoConstraints = false };
            guide.Layer.BorderColor = UIColor.FromRGB(198, 244, 50).CGColor;   // the app's lime
            guide.Layer.BorderWidth = 2;
            guide.Layer.CornerRadius = 14;
            View!.AddSubview(guide);
            NSLayoutConstraint.ActivateConstraints([
                guide.CenterXAnchor.ConstraintEqualTo(View.CenterXAnchor),
                guide.CenterYAnchor.ConstraintEqualTo(View.CenterYAnchor),
                guide.WidthAnchor.ConstraintEqualTo(View.WidthAnchor, 0.78f),
                guide.HeightAnchor.ConstraintEqualTo(guide.WidthAnchor, 0.5f)
            ]);
        }

        private static UIButton Button(string title)
        {
            var b = new UIButton(UIButtonType.System) { TranslatesAutoresizingMaskIntoConstraints = false };
            b.SetTitle(title, UIControlState.Normal);
            b.SetTitleColor(UIColor.White, UIControlState.Normal);
            b.TitleLabel!.Font = UIFont.BoldSystemFontOfSize(16);
            b.BackgroundColor = UIColor.FromWhiteAlpha(0f, 0.55f);
            b.Layer.CornerRadius = 18;
            b.ContentEdgeInsets = new UIEdgeInsets(9, 16, 9, 16);
            return b;
        }

        private void ToggleTorch()
        {
            if (camera is null) return;
            if (!camera.LockForConfiguration(out _)) return;
            try { camera.TorchMode = camera.TorchMode == AVCaptureTorchMode.On ? AVCaptureTorchMode.Off : AVCaptureTorchMode.On; }
            finally { camera.UnlockForConfiguration(); }
        }

        private void Stop()
        {
            if (camera is { HasTorch: true, TorchMode: AVCaptureTorchMode.On } && camera.LockForConfiguration(out _))
            {
                camera.TorchMode = AVCaptureTorchMode.Off;
                camera.UnlockForConfiguration();
            }
            if (_session is { Running: true } s) DispatchQueue.GetGlobalQueue(DispatchQueuePriority.Default).DispatchAsync(s.StopRunning);
        }

        private void Finish(string? code)
        {
            if (_finished) return;
            _finished = true;
            Stop();
            if (code is not null) new UINotificationFeedbackGenerator().NotificationOccurred(UINotificationFeedbackType.Success);
            result.TrySetResult(code);
            DismissViewController(true, null);
        }

        private sealed class CodeDelegate(ScannerController owner) : AVCaptureMetadataOutputObjectsDelegate
        {
            public override void DidOutputMetadataObjects(AVCaptureMetadataOutput captureOutput, AVMetadataObject[] metadataObjects,
                AVCaptureConnection connection)
            {
                foreach (var o in metadataObjects)
                    if (o is AVMetadataMachineReadableCodeObject { StringValue: { Length: >= 8 } code } && code.All(char.IsDigit))
                    {
                        owner.Finish(code);
                        return;
                    }
            }
        }
    }
}
