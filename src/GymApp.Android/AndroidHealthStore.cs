using Android.Content;
using Android.Runtime;
using AndroidX.Health.Connect.Client;
using AndroidX.Health.Connect.Client.Records;
using AndroidX.Health.Connect.Client.Request;
using AndroidX.Health.Connect.Client.Response;
using AndroidX.Health.Connect.Client.Time;
using GymApp.App;
using GymApp.Core;
using Java.Time;
using DataOrigin = AndroidX.Health.Connect.Client.Records.Metadata.DataOrigin;
using HcMetadata = AndroidX.Health.Connect.Client.Records.Metadata.Metadata;
using Mass = AndroidX.Health.Connect.Client.Units.Mass;
using Uri = Android.Net.Uri;

namespace GymApp.Android;

/// <summary>
/// Health Connect (Phase 23), behind the same <see cref="IHealthStore"/> as Apple Health. Every record GainStreak writes
/// carries its sync id as <c>clientRecordId</c> and its version as <c>clientRecordVersion</c>, so inserting it again
/// replaces it (an upsert); deletes go by client record id. Workouts are exercise sessions (strength training, or "other
/// workout" for cardio) with the day name as title; weigh-ins are weight records. Binding details: docs/PHASE_23_HEALTH_CONNECT.md,
/// "23a results".
/// <para>Unlike HealthKit, Health Connect says what was granted, reads included; and the system asks again for anything
/// refused, so the permissions already asked for are remembered here and asked only once, as iOS does.</para>
/// </summary>
public sealed class AndroidHealthStore(Context context) : IHealthStore
{
    /// <summary>Health Connect's app on Android 8 to 13 (built in from Android 14).</summary>
    private const string ProviderPackage = "com.google.android.apps.healthdata";
    private const string Prefs = "health_connect";
    private const string AskedKey = "asked";

    private const string WriteWeight = "android.permission.health.WRITE_WEIGHT";
    private const string WriteExercise = "android.permission.health.WRITE_EXERCISE";

    /// <summary>Everything the manifest declares.</summary>
    internal static readonly string[] Permissions =
    [
        "android.permission.health.READ_WEIGHT", WriteWeight,
        "android.permission.health.READ_EXERCISE", WriteExercise,
        "android.permission.health.READ_HEIGHT", "android.permission.health.READ_STEPS",
    ];

    private IHealthConnectClient? _client;
    private HashSet<string>? _granted;

    private int Status => HealthConnectClient.GetSdkStatus(context, ProviderPackage);

    public bool IsSupported => Status != HealthConnectClient.SdkUnavailable;

    public bool NeedsInstall => Status == HealthConnectClient.SdkUnavailableProviderUpdateRequired;

    public HealthTexts Texts => HealthTexts.HealthConnect;

    private IHealthConnectClient Client => _client ??= HealthConnectClient.GetOrCreate(context, ProviderPackage);

    public async Task<bool> RequestAuthorizationAsync()
    {
        if (Status != HealthConnectClient.SdkAvailable) return false;
        await RefreshGrantedAsync();
        var prefs = context.GetSharedPreferences(Prefs, FileCreationMode.Private)!;
        var asked = prefs.GetStringSet(AskedKey, null)?.ToHashSet() ?? [];
        var ask = Permissions.Where(p => !_granted!.Contains(p) && !asked.Contains(p)).ToList();
        if (ask.Count == 0) return true;   // like iOS: only what was never asked shows the sheet

        if (await MainActivity.RequestHealthPermissionsAsync(ask) is not { } granted) return false;
        asked.UnionWith(ask);
        prefs.Edit()!.PutStringSet(AskedKey, asked)!.Apply();
        _granted = [.. granted];
        return true;
    }

    /// <summary>From the last look at what's granted (refreshed by every sync and request). Not asked yet: nothing known.</summary>
    public HealthAccess WriteAccess(HealthKind kind)
    {
        if (_granted is not { } granted) return HealthAccess.NotAsked;
        return granted.Contains(kind == HealthKind.Weight ? WriteWeight : WriteExercise) ? HealthAccess.Allowed : HealthAccess.Denied;
    }

    public async Task<IReadOnlyList<HealthItem>> ReadOwnAsync(DateTime from, DateTime to)
    {
        await RefreshGrantedAsync();   // the sync checks WriteAccess next; the user may have changed it in Health Connect
        var range = TimeRangeFilter.Between(ToInstant(from), ToInstant(to))!;
        var items = new List<HealthItem>();
        foreach (var e in await ReadAllAsync<ExerciseSessionRecord>(range, own: true))
            if (OwnId(e.Metadata) is { } id)
                items.Add(new HealthItem(id, e.ExerciseType == ExerciseSessionRecord.ExerciseTypeStrengthTraining
                        ? HealthKind.StrengthWorkout : HealthKind.CardioWorkout,
                    FromInstant(e.StartTime), FromInstant(e.EndTime),
                    Title: string.IsNullOrEmpty(e.Title) ? null : e.Title,   // a null title reads back as "" (23a)
                    Version: e.Metadata.ClientRecordVersion));
        foreach (var w in await ReadAllAsync<WeightRecord>(range, own: true))
            if (OwnId(w.Metadata) is { } id)
            {
                var at = FromInstant(w.Time);
                items.Add(new HealthItem(id, HealthKind.Weight, at, at, w.Weight.Kilograms, Version: w.Metadata.ClientRecordVersion));
            }
        return items;
    }

    public async Task SaveAsync(HealthItem item)
    {
        var metadata = HcMetadata.ManualEntry(item.SyncId, item.Version)!;
        var start = ToInstant(item.Start);
        IRecord record = item.Kind == HealthKind.Weight
            ? new WeightRecord(start, Offset(start), Mass.InvokeKilograms(item.Value ?? 0)!, metadata)
            : new ExerciseSessionRecord(start, Offset(start), ToInstant(item.End), Offset(ToInstant(item.End)), metadata,
                item.Kind == HealthKind.StrengthWorkout
                    ? ExerciseSessionRecord.ExerciseTypeStrengthTraining : ExerciseSessionRecord.ExerciseTypeOtherWorkout,
                item.Title, null);
        await KotlinSuspend.Run<InsertRecordsResponse>(c => Client.InsertRecords(new List<IRecord> { record }, c));
    }

    public Task DeleteAsync(HealthItem item) =>
        KotlinSuspend.Run(c => Client.DeleteRecords(item.Kind == HealthKind.Weight ? K<WeightRecord>() : K<ExerciseSessionRecord>(),
            new List<string>(), new List<string> { item.SyncId }, c));

    /// <summary>Health Connect's home, whose "App permissions" lists GainStreak. The page for one app can't be opened by
    /// apps (it needs GRANT_RUNTIME_PERMISSIONS, 23b). Android 14+ has it in the system; earlier, the Health Connect app.</summary>
    public void OpenSettings() =>
        TryStart(new Intent(OperatingSystem.IsAndroidVersionAtLeast(34)
            ? "android.health.connect.action.HEALTH_HOME_SETTINGS" : "androidx.health.ACTION_HEALTH_CONNECT_SETTINGS"));

    private bool TryStart(Intent intent)
    {
        try
        {
            context.StartActivity(intent.AddFlags(ActivityFlags.NewTask));
            return true;
        }
        catch (Exception e) when (e is ActivityNotFoundException or Java.Lang.SecurityException) { return false; }
    }

    /// <summary>Health Connect on Google Play (Android 8 to 13); the web page where there's no store app.</summary>
    public void OpenInstallPage()
    {
        try { Open($"market://details?id={ProviderPackage}&url=healthconnect%3A%2F%2Fonboarding"); }
        catch (ActivityNotFoundException) { Open($"https://play.google.com/store/apps/details?id={ProviderPackage}"); }
    }

    // ===== Reads (23c). A refused or failed read looks like no data, as on iOS =====

    /// <summary>The latest height. Health Connect has no birth date or sex.</summary>
    public async Task<HealthProfile> ReadProfileAsync()
    {
        double? heightCm = null;
        try
        {
            var latest = await KotlinSuspend.Run<ReadRecordsResponse>(c => Client.ReadRecords(new ReadRecordsRequest(K<HeightRecord>(),
                TimeRangeFilter.Before(Instant.Now()!.PlusSeconds(60)!), new List<DataOrigin>(), false, 1, null), c));
            if (latest is not null) foreach (var r in latest.Records)
                heightCm = ((Java.Lang.Object)r).JavaCast<HeightRecord>()!.Height.Meters * 100;
        }
        catch (Exception e) when (IsRefusal(e)) { }
        return new HealthProfile(heightCm, null, null);
    }

    /// <summary>
    /// With an <paramref name="anchor"/> (a changes token as UTF-8): the weigh-ins added or changed since, from any app.
    /// Without one, or when the token expired (about 30 days unused): every weigh-in from <paramref name="from"/>. The
    /// token for next time is taken before reading, so nothing added meanwhile is missed. GainStreak's own records are
    /// returned with their sync id; <c>HealthImport</c> leaves them out.
    /// </summary>
    public async Task<HealthWeights> ReadWeightsAsync(DateTime from, byte[]? anchor)
    {
        try
        {
            if (anchor is { Length: > 0 } && await ChangesSinceAsync(System.Text.Encoding.UTF8.GetString(anchor), from) is { } changed)
                return changed;

            var token = await NewWeightTokenAsync();
            var all = await ReadAllAsync<WeightRecord>(TimeRangeFilter.After(ToInstant(from))!, own: false);
            return new HealthWeights(all.Select(Weight).ToList(), System.Text.Encoding.UTF8.GetBytes(token));
        }
        catch (Exception e) when (IsRefusal(e)) { return new HealthWeights([], anchor); }
    }

    /// <summary>Null when the token expired.</summary>
    private async Task<HealthWeights?> ChangesSinceAsync(string token, DateTime from)
    {
        var samples = new List<HealthWeight>();
        while (true)
        {
            var current = token;
            var response = await KotlinSuspend.Run<ChangesResponse>(c => Client.GetChanges(current, c));
            if (response is null || response.ChangesTokenExpired) return null;
            foreach (var change in response.Changes)
            {
                // List items can arrive as plain Java objects: check the Java class, then cast (deletions are skipped)
                var item = (Java.Lang.Object)change;
                if (item.Class.Name != "androidx.health.connect.client.changes.UpsertionChange") continue;
                var record = (Java.Lang.Object)item.JavaCast<AndroidX.Health.Connect.Client.Changes.UpsertionChange>()!.Record;
                if (record.Class.Name != "androidx.health.connect.client.records.WeightRecord") continue;
                var w = record.JavaCast<WeightRecord>()!;
                if (FromInstant(w.Time) >= from) samples.Add(Weight(w));
            }
            token = response.NextChangesToken;
            if (!response.HasMore) break;
        }
        return new HealthWeights(samples, System.Text.Encoding.UTF8.GetBytes(token));
    }

    private async Task<string> NewWeightTokenAsync() =>
        (await KotlinSuspend.Run<Java.Lang.String>(c => Client.GetChangesToken(
            new ChangesTokenRequest(new List<Kotlin.Reflect.IKClass> { K<WeightRecord>() }, new List<DataOrigin>()), c)))!.ToString();

    private static HealthWeight Weight(WeightRecord w) => new(FromInstant(w.Time), w.Weight.Kilograms, w.Metadata.ClientRecordId);

    /// <summary>Each day's steps from <paramref name="from"/> up to but not including <paramref name="to"/>, counted once
    /// across the phone and a watch (Health Connect's aggregate de-duplicates); a day without steps is 0.</summary>
    public async Task<IReadOnlyList<double>> ReadDailyStepsAsync(DateOnly from, DateOnly to)
    {
        try
        {
            var groups = await KotlinSuspend.Run<Java.Lang.Object>(c => Client.AggregateGroupByPeriod(new AggregateGroupByPeriodRequest(
                new List<AndroidX.Health.Connect.Client.Aggregate.AggregateMetric> { StepsRecord.CountTotal },
                TimeRangeFilter.Between(LocalDate(from), LocalDate(to))!, Period.OfDays(1)!, new List<DataOrigin>()), c));
            if (groups is null) return [];
            var list = groups.JavaCast<Java.Util.IList>()!;
            var days = new List<double>();
            for (var i = 0; i < list.Size(); i++)
            {
                var group = list.Get(i)!.JavaCast<AndroidX.Health.Connect.Client.Aggregate.AggregationResultGroupedByPeriod>()!;
                days.Add(group.Result.Get(StepsRecord.CountTotal)?.JavaCast<Java.Lang.Number>()?.DoubleValue() ?? 0);
            }
            return days;
        }
        catch (Exception e) when (IsRefusal(e)) { return []; }
    }

    private static LocalDateTime LocalDate(DateOnly d) => LocalDateTime.Of(d.Year, d.Month, d.Day, 0, 0)!;

    /// <summary>A refused permission (SecurityException) or Health Connect saying no (a Java exception) reads as no
    /// data; .NET bugs still surface in HealthSync's log.</summary>
    private static bool IsRefusal(Exception e) => e is Java.Lang.Throwable;

    // ===== Helpers =====

    private async Task RefreshGrantedAsync()
    {
        var granted = await KotlinSuspend.Run<Java.Lang.Object>(c => Client.PermissionController.GetGrantedPermissions(c));
        _granted = granted is null ? [] : [.. Strings(granted)];
    }

    /// <summary>All pages of one record type in <paramref name="range"/>, this app's only when <paramref name="own"/>.</summary>
    private async Task<List<T>> ReadAllAsync<T>(TimeRangeFilter range, bool own) where T : Java.Lang.Object, IRecord
    {
        var origins = own ? new List<DataOrigin> { new(context.PackageName!) } : new List<DataOrigin>();
        var all = new List<T>();
        string? page = null;
        do
        {
            var token = page;
            var response = await KotlinSuspend.Run<ReadRecordsResponse>(c => Client.ReadRecords(
                new ReadRecordsRequest(K<T>(), range, origins, true, 1000, token), c));
            if (response is null) break;
            foreach (var r in response.Records) all.Add(((Java.Lang.Object)r).JavaCast<T>()!);
            page = response.PageToken;
        } while (!string.IsNullOrEmpty(page));
        return all;
    }

    /// <summary>The sync id of a record this app wrote for GainStreak; this app's records without one are skipped (23a).</summary>
    private static string? OwnId(HcMetadata metadata) =>
        metadata.ClientRecordId is { } id && id.StartsWith(HealthPlan.Prefix, StringComparison.Ordinal) ? id : null;

    private static Kotlin.Reflect.IKClass K<T>() where T : Java.Lang.Object =>
        Kotlin.Jvm.JvmClassMappingKt.GetKotlinClass(Java.Lang.Class.FromType(typeof(T)));

    internal static List<string> Strings(Java.Lang.Object collection)
    {
        var list = new List<string>();
        var it = collection.JavaCast<Java.Util.ICollection>()!.Iterator()!;
        while (it.HasNext) list.Add(it.Next()!.ToString());
        return list;
    }

    /// <summary>The app's times are local (AppClock).</summary>
    private static Instant ToInstant(DateTime local) =>
        Instant.OfEpochMilli(new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local)).ToUnixTimeMilliseconds())!;

    private static DateTime FromInstant(Instant instant) =>
        DateTime.SpecifyKind(DateTimeOffset.FromUnixTimeMilliseconds(instant.ToEpochMilli()).LocalDateTime, DateTimeKind.Unspecified);

    private static ZoneOffset Offset(Instant at) => ZoneId.SystemDefault()!.Rules!.GetOffset(at)!;

    private void Open(string url) =>
        context.StartActivity(new Intent(Intent.ActionView, Uri.Parse(url)).AddFlags(ActivityFlags.NewTask));
}
