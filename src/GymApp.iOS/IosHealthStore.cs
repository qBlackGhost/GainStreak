using Foundation;
using GymApp.App;
using GymApp.Core;
using HealthKit;

namespace GymApp.iOS;

/// <summary>
/// Apple Health through HealthKit (Phase 20). Needs the HealthKit entitlement (Entitlements.plist) and the usage texts
/// (Info.plist). Workouts are saved with <see cref="HKWorkoutBuilder"/>
/// (HKWorkout.Create is deprecated) and weigh-ins as body mass samples; every object carries its sync id and version, so
/// Health replaces it on a change. Binding names were found in 20a (docs/PHASE_20_APPLE_HEALTH.md, "20a results").
/// </summary>
public sealed class IosHealthStore : IHealthStore
{
    private readonly HKHealthStore _store = new();
    private static readonly HKQuantityType Mass = HKQuantityType.Create(HKQuantityTypeIdentifier.BodyMass)!;
    private static readonly HKWorkoutType Workout = HKObjectType.WorkoutType;
    private static readonly HKUnit Kg = HKUnit.FromGramUnit(HKMetricPrefix.Kilo);
    private static readonly HKQuantityType Height = HKQuantityType.Create(HKQuantityTypeIdentifier.Height)!;
    private static readonly HKQuantityType Steps = HKQuantityType.Create(HKQuantityTypeIdentifier.StepCount)!;
    private static readonly HKCharacteristicType BirthDate = HKCharacteristicType.Create(HKCharacteristicTypeIdentifier.DateOfBirth)!;
    private static readonly HKCharacteristicType BiologicalSex = HKCharacteristicType.Create(HKCharacteristicTypeIdentifier.BiologicalSex)!;

    public bool IsSupported => HKHealthStore.IsHealthDataAvailable;

    public async Task<bool> RequestAuthorizationAsync()
    {
        // Asks only for types not asked before: after the first time it returns at once
        var (shown, _) = await _store.RequestAuthorizationToShareAsync(new NSSet(Mass, Workout),
            new NSSet(Mass, Workout, Height, Steps, BirthDate, BiologicalSex));
        return shown;
    }

    public HealthAccess WriteAccess(HealthKind kind) =>
        _store.GetAuthorizationStatus(kind == HealthKind.Weight ? Mass : Workout) switch
        {
            HKAuthorizationStatus.SharingAuthorized => HealthAccess.Allowed,
            HKAuthorizationStatus.SharingDenied => HealthAccess.Denied,
            _ => HealthAccess.NotAsked,
        };

    public async Task<IReadOnlyList<HealthItem>> ReadOwnAsync(DateTime from, DateTime to)
    {
        var predicate = NSCompoundPredicate.CreateAndPredicate([
            HKQuery.GetPredicateForObjectsFromSource(HKSource.GetDefaultSource),
            HKQuery.GetPredicateForSamples(ToNs(from), ToNs(to), HKQueryOptions.StrictStartDate),
        ]);
        var items = new List<HealthItem>();
        foreach (var sample in await QueryAsync(Workout, predicate))
            if (sample is HKWorkout w && Sync(w) is var (id, version) && id is not null)
                items.Add(new HealthItem(id, w.WorkoutActivityType == HKWorkoutActivityType.TraditionalStrengthTraining
                        ? HealthKind.StrengthWorkout : HealthKind.CardioWorkout,
                    FromNs(w.StartDate), FromNs(w.EndDate), Title: Text(w.Metadata?.Dictionary, HKMetadataKey.WorkoutBrandName), Version: version));
        foreach (var sample in await QueryAsync(Mass, predicate))
            if (sample is HKQuantitySample q && Sync(q) is var (id, version) && id is not null)
                items.Add(new HealthItem(id, HealthKind.Weight, FromNs(q.StartDate), FromNs(q.EndDate), q.Quantity.GetDoubleValue(Kg), Version: version));
        return items;
    }

    public async Task SaveAsync(HealthItem item)
    {
        var metadata = Metadata(item);
        if (item.Kind == HealthKind.Weight)
        {
            var sample = HKQuantitySample.FromType(Mass, HKQuantity.FromQuantity(Kg, item.Value ?? 0), ToNs(item.Start), ToNs(item.End), metadata);
            var (saved, error) = await _store.SaveObjectAsync(sample);
            if (!saved) throw new InvalidOperationException(error?.LocalizedDescription ?? "Health refused the weigh-in");
            return;
        }
        var config = new HKWorkoutConfiguration
        {
            ActivityType = item.Kind == HealthKind.StrengthWorkout
                ? HKWorkoutActivityType.TraditionalStrengthTraining : HKWorkoutActivityType.MixedCardio,
        };
        var builder = new HKWorkoutBuilder(_store, config, HKDevice.LocalDevice);
        Check(await builder.BeginCollectionAsync(ToNs(item.Start)));
        Check(await builder.AddAsync(new HKMetadata(metadata)));
        Check(await builder.EndCollectionAsync(ToNs(item.End)));
        Check(await builder.FinishWorkoutAsync());
    }

    public Task DeleteAsync(HealthItem item)
    {
        var done = new TaskCompletionSource();
        _store.DeleteObjects(item.Kind == HealthKind.Weight ? Mass : Workout,
            HKQuery.GetPredicateForMetadataKey(HKMetadataKey.SyncIdentifier, [new NSString(item.SyncId)]),
            (ok, _, error) =>
            {
                if (ok) done.TrySetResult();
                else done.TrySetException(new InvalidOperationException(error?.LocalizedDescription ?? "Health refused the delete"));
            });
        return done.Task;
    }

    public void OpenSettings() =>
        UIKit.UIApplication.SharedApplication.OpenUrl(new NSUrl(UIKit.UIApplication.OpenSettingsUrlString), new UIKit.UIApplicationOpenUrlOptions(), null);

    // ===== Reads (20c) =====

    public async Task<HealthProfile> ReadProfileAsync()
    {
        double? heightCm = null;
        try
        {
            var latest = await QueryAsync(Height, null, limit: 1,
                sort: [new NSSortDescriptor(HKSample.SortIdentifierEndDate, false)]);
            if (latest.FirstOrDefault() is HKQuantitySample h) heightCm = h.Quantity.GetDoubleValue(HKUnit.CreateMeterUnit(HKMetricPrefix.Centi));
        }
        catch (InvalidOperationException) { }   // no access looks like no data

        var birth = _store.GetDateOfBirthComponents(out _);
        DateOnly? birthDate = birth is { Year: > 0 and < 10000, Month: >= 1 and <= 12, Day: >= 1 and <= 31 }
            && DateOnly.TryParseExact($"{birth.Year:D4}-{birth.Month:D2}-{birth.Day:D2}", "yyyy-MM-dd", out var d) ? d : null;
        var sex = _store.GetBiologicalSex(out _)?.BiologicalSex switch
        {
            HKBiologicalSex.Female => Core.Sex.Female,
            HKBiologicalSex.Male => Core.Sex.Male,
            _ => (Core.Sex?)null,
        };
        return new HealthProfile(heightCm, birthDate, sex);
    }

    public Task<HealthWeights> ReadWeightsAsync(DateTime from, byte[]? anchor)
    {
        var done = new TaskCompletionSource<HealthWeights>();
        HKQueryAnchor? start = null;
        if (anchor is { Length: > 0 })
            start = NSKeyedUnarchiver.GetUnarchivedObject(typeof(HKQueryAnchor), NSData.FromArray(anchor), out _) as HKQueryAnchor;
        var predicate = HKQuery.GetPredicateForSamples(ToNs(from), null, HKQueryOptions.StrictStartDate);
        _store.ExecuteQuery(new HKAnchoredObjectQuery(Mass, predicate, start, 0, (_, added, _, next, error) =>
        {
            if (error is not null) { done.TrySetResult(new HealthWeights([], anchor)); return; }
            var samples = (added ?? []).OfType<HKQuantitySample>()
                .Select(q => new HealthWeight(FromNs(q.EndDate), q.Quantity.GetDoubleValue(Kg),
                    Text(q.Metadata?.Dictionary, HKMetadataKey.SyncIdentifier)))
                .ToList();
            var bytes = next is null ? anchor : NSKeyedArchiver.GetArchivedData(next, true, out _)?.ToArray() ?? anchor;
            done.TrySetResult(new HealthWeights(samples, bytes));
        }));
        return done.Task;
    }

    public Task<IReadOnlyList<double>> ReadDailyStepsAsync(DateOnly from, DateOnly to)
    {
        var done = new TaskCompletionSource<IReadOnlyList<double>>();
        var start = ToNs(from.ToDateTime(TimeOnly.MinValue));
        var query = new HKStatisticsCollectionQuery(Steps,
            HKQuery.GetPredicateForSamples(start, ToNs(to.ToDateTime(TimeOnly.MinValue)), HKQueryOptions.StrictStartDate),
            HKStatisticsOptions.CumulativeSum, start, new NSDateComponents { Day = 1 });
        query.InitialResultsHandler = (_, result, error) =>
            done.TrySetResult(error is not null || result is null ? []
                : result.Statistics.Select(s => s.SumQuantity()?.GetDoubleValue(HKUnit.Count) ?? 0).ToList());
        _store.ExecuteQuery(query);
        return done.Task;
    }

    private static NSDictionary Metadata(HealthItem item)
    {
        var d = new NSMutableDictionary
        {
            [HKMetadataKey.SyncIdentifier] = new NSString(item.SyncId),
            [HKMetadataKey.SyncVersion] = NSNumber.FromInt64(item.Version),
        };
        if (item.Title is { } title) d[HKMetadataKey.WorkoutBrandName] = new NSString(title);
        return d;
    }

    private static (string? Id, long Version) Sync(HKObject o)
    {
        var d = o.Metadata?.Dictionary;
        var id = Text(d, HKMetadataKey.SyncIdentifier);
        var version = d?[HKMetadataKey.SyncVersion] is NSNumber n ? n.Int64Value : 0;
        return (id is not null && id.StartsWith(HealthPlan.Prefix, StringComparison.Ordinal) ? id : null, version);
    }

    private static string? Text(NSDictionary? d, NSString key) => d?[key] is NSString s ? s.ToString() : null;

    private static void Check(Tuple<bool, NSError?> result)
    {
        if (!result.Item1) throw new InvalidOperationException(result.Item2?.LocalizedDescription ?? "Health refused the workout");
    }

    private Task<HKSample[]> QueryAsync(HKSampleType type, NSPredicate? predicate, nuint limit = 0, NSSortDescriptor[]? sort = null)
    {
        var done = new TaskCompletionSource<HKSample[]>();
        _store.ExecuteQuery(new HKSampleQuery(type, predicate, limit, sort, (_, results, error) =>
        {
            if (error is not null) done.TrySetException(new InvalidOperationException(error.LocalizedDescription));
            else done.TrySetResult(results ?? []);
        }));
        return done.Task;
    }

    /// <summary>The app's times are local (AppClock).</summary>
    private static NSDate ToNs(DateTime local) => (NSDate)DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();

    private static DateTime FromNs(NSDate date) => DateTime.SpecifyKind(((DateTime)date).ToLocalTime(), DateTimeKind.Unspecified);
}
