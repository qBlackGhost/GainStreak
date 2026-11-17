using Android.App;
using Android.Content;
using Android.OS;
using Uri = Android.Net.Uri;

namespace GymApp.Android;

/// <summary>
/// Health Connect (Phase 23) shows GainStreak's permission sheet only when the app has a page explaining how it uses
/// health data; its "privacy policy" link opens this. It opens the privacy policy (its "Health Connect" section) in the
/// browser and closes. Registered for the rationale action here (Android 13 and earlier) and through the
/// <c>ViewPermissionUsageActivity</c> alias in the manifest (Android 14 and later).
/// </summary>
[Activity(Name = "com.gainstreak.app.HealthPermissionsRationaleActivity", Exported = true, NoHistory = true,
    Theme = "@android:style/Theme.Translucent.NoTitleBar")]
[IntentFilter(["androidx.health.ACTION_SHOW_PERMISSIONS_RATIONALE"])]
public class HealthPermissionsRationaleActivity : Activity
{
    public const string PrivacyUrl = "https://qblackghost.github.io/GainStreak-releases/privacy.html#health-connect";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        try { StartActivity(new Intent(Intent.ActionView, Uri.Parse(PrivacyUrl))); }
        catch (ActivityNotFoundException) { }   // no browser: nothing to show
        Finish();
    }
}
