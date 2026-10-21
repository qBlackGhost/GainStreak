using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Desktop.ViewModels;

/// <summary>One day of the "Your week" preview.</summary>
public record ScheduleCell(string Label, string Title, bool IsTraining, bool IsStart)
{
    public bool IsTrainingStart => IsTraining && IsStart;
}

/// <summary>One step of the rotation: "Push →".</summary>
public record RotationStep(string Name, bool HasArrow);

/// <summary>One row of "About this split": the day and the muscle groups of the user's exercises on it.</summary>
public record SplitDayInfo(string Name, string Muscles);

/// <summary>One slice of the "Who uses it" donut and its legend row: a preset, "Custom splits" or "Other" (whose
/// <see cref="Detail"/> names what it holds). <see cref="Tone"/> 0 is this split's accent, 1–5 the greys in order.</summary>
public record SplitUsageSlice(string Name, string? Detail, double Percent, string PercentText, bool IsThis, int Tone)
{
    public bool HasDetail => Detail is not null;
}

/// <summary>Turns user counts into the donut's slices.</summary>
public static class SplitUsageChart
{
    /// <summary>Shares under this go into "Other", unless it's the split being viewed or the only small one.</summary>
    public const double MinPercent = 10;

    /// <param name="splits">Every preset and "Custom splits" with its user count; exactly one is the split being viewed.</param>
    /// <param name="total">Everyone with a split.</param>
    public static IReadOnlyList<SplitUsageSlice> Build(IEnumerable<(string Name, int Users, bool IsThis)> splits, int total)
    {
        var shares = splits.Select(s => (s.Name, Percent: 100.0 * s.Users / total, s.IsThis))
            .OrderByDescending(s => s.Percent).ToList();
        var small = shares.Where(s => s.Percent < MinPercent && !s.IsThis).ToList();
        if (small.Count > 1)
        {
            shares = shares.Except(small).ToList();
            shares.Add(("Other", small.Sum(s => s.Percent), false));
        }

        var tone = 0;
        return shares.Select(s => new SplitUsageSlice(s.Name,
                s.Name == "Other" && small.Count > 1 ? string.Join(" · ", small.Select(x => x.Name)) : null,
                s.Percent, SplitTags.Percent(s.Percent), s.IsThis, s.IsThis ? 0 : ++tone))
            .ToList();
    }
}

/// <summary>Shared by the split list and detail pages.</summary>
public static class SplitTags
{
    /// <summary>"Recommended for 4 days / week" from a preset's default variant; null for custom splits.</summary>
    public static string? RecommendedDays(WorkoutSplit split)
    {
        var variant = split.Variants.FirstOrDefault(v => v.IsDefault) ?? split.Variants.FirstOrDefault();
        if (split.OwnerUserId is not null || variant is null) return null;
        var days = variant.Pattern.Count(c => c == 'W');
        return days == 1 ? "Recommended for 1 day / week" : $"Recommended for {days} days / week";
    }

    /// <summary>"23%", or "<1%" for a share too small to round up.</summary>
    public static string Percent(double percent) =>
        percent is > 0 and < 0.5 ? "<1%" : $"{Math.Round(percent).ToString(CultureInfo.InvariantCulture)}%";
}

/// <summary>A "days per week" card for a preset's variant.</summary>
public partial class VariantOptionViewModel(SplitVariant variant, SplitDetailViewModel owner) : ObservableObject
{
    public SplitVariant Variant { get; } = variant;
    public string Days { get; } = variant.Pattern.Count(c => c == 'W').ToString(CultureInfo.InvariantCulture);

    [ObservableProperty] public partial bool IsSelected { get; set; }

    [RelayCommand] private void Pick() => owner.SelectedVariant = Variant;
}

/// <summary>One button of the "first workout day" selector.</summary>
public partial class StartDayOptionViewModel(DayOfWeek day, SplitDetailViewModel owner) : ObservableObject
{
    public DayOfWeek Day { get; } = day;
    public string Label { get; } = day.ToString()[..3];

    [ObservableProperty] public partial bool IsSelected { get; set; }

    [RelayCommand] private void Pick() => owner.StartDay = Day;
}

/// <summary>
/// A split's detail page, where it is chosen: for presets the days per week (variant) and the first workout day,
/// then a preview of the week. Custom splits already define their weekdays, so they only show the preview.
/// </summary>
public partial class SplitDetailViewModel : ViewModelBase
{
    private readonly SplitService _splits;
    private readonly DayExerciseService _dayExercises;
    private readonly SessionService _session;
    private readonly ISplitUsageService _usage;
    private readonly Func<MainViewModel> _shell;
    private WorkoutSplit _split = null!;

    public IReadOnlyList<DayOfWeek> StartDays { get; } = new[]
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };

    public IReadOnlyList<StartDayOptionViewModel> StartDayOptions { get; }

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string? Description { get; set; }
    [ObservableProperty] public partial bool IsRecommended { get; set; }
    [ObservableProperty] public partial bool IsPreset { get; set; }
    [ObservableProperty] public partial string Rotation { get; set; } = "";
    [ObservableProperty] public partial IReadOnlyList<RotationStep> RotationSteps { get; set; } = [];
    [ObservableProperty] public partial string WeekSummary { get; set; } = "";
    [ObservableProperty] public partial string TrainCount { get; set; } = "0";
    [ObservableProperty] public partial string RestCount { get; set; } = "7";
    [ObservableProperty] public partial string DaysTag { get; set; } = "";
    [ObservableProperty] public partial string? RecommendedDaysTag { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial string? Toast { get; set; }

    // Numbered steps: "1 · Days per week" only for presets with more than one variant
    [ObservableProperty] public partial string RotationStepLabel { get; set; } = "1 · ROTATION";
    [ObservableProperty] public partial string StartStepLabel { get; set; } = "2 · FIRST WORKOUT DAY";

    // About this split
    [ObservableProperty] public partial string? About { get; set; }
    [ObservableProperty] public partial IReadOnlyList<string> GoodFor { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SplitDayInfo> DayInfo { get; set; } = [];
    public bool HasGoodFor => GoodFor.Count > 0;
    partial void OnGoodForChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasGoodFor));

    // Who uses it: every preset and "Custom splits", from the server's counts; hidden when there are none
    [ObservableProperty] public partial IReadOnlyList<SplitUsageSlice> UsageSlices { get; set; } = [];
    [ObservableProperty] public partial string MinePercent { get; set; } = "";
    [ObservableProperty] public partial string UsageHeadline { get; set; } = "";
    [ObservableProperty] public partial string UsageNote { get; set; } = "";
    public bool HasUsage => UsageSlices.Count > 0;
    partial void OnUsageSlicesChanged(IReadOnlyList<SplitUsageSlice> value) => OnPropertyChanged(nameof(HasUsage));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseLabel), nameof(FootNote))]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVariants))]
    public partial IReadOnlyList<VariantOptionViewModel> VariantOptions { get; set; } = [];

    [ObservableProperty] public partial IReadOnlyList<SplitVariant> Variants { get; set; } = [];
    [ObservableProperty] public partial SplitVariant? SelectedVariant { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FootNote), nameof(StartDayIndex))]
    public partial DayOfWeek StartDay { get; set; } = DayOfWeek.Monday;

    [ObservableProperty] public partial IReadOnlyList<ScheduleCell> Cells { get; set; } = [];

    public bool HasVariants => VariantOptions.Count > 1;

    /// <summary>The selected start day's position in <see cref="StartDays"/>, for the sliding pill.</summary>
    public int StartDayIndex => StartDays.ToList().IndexOf(StartDay);
    public string UseLabel => IsActive ? "Save changes" : "Use this split";
    public string FootNote => IsActive
        ? IsPreset ? "This is your current split. Changing the start day updates your schedule from today on."
                   : "This is your current split. Edit it to change its days."
        : IsPreset ? $"Switching split keeps your past streak days. Your new week starts on {StartDay}."
                   : "Switching split keeps your past streak days.";

    public SplitDetailViewModel(SplitService splits, DayExerciseService dayExercises, SessionService session,
        ISplitUsageService usage, Func<MainViewModel> shell)
    {
        _splits = splits; _dayExercises = dayExercises; _session = session; _usage = usage; _shell = shell;
        StartDayOptions = StartDays.Select(d => new StartDayOptionViewModel(d, this)).ToList();
    }

    /// <summary>Call right after resolving from DI.</summary>
    public void Load(WorkoutSplit split)
    {
        _split = split;
        var user = _session.CurrentUser!;
        var days = split.Days.OrderBy(d => d.Order).ToList();

        Name = split.Name;
        Description = split.Description;
        IsRecommended = split.IsRecommended;
        IsPreset = split.OwnerUserId is null;
        IsActive = user.SelectedSplitId == split.Id;
        Rotation = string.Join(" → ", days.Select(d => d.Name));
        RotationSteps = days.Select((d, i) => new RotationStep(d.Name, i < days.Count - 1)).ToList();
        RecommendedDaysTag = SplitTags.RecommendedDays(split);
        Variants = split.Variants.OrderBy(v => v.Id).ToList();
        VariantOptions = Variants.Select(v => new VariantOptionViewModel(v, this)).ToList();
        RotationStepLabel = HasVariants ? "2 · ROTATION" : "1 · ROTATION";
        StartStepLabel = HasVariants ? "3 · FIRST WORKOUT DAY" : "2 · FIRST WORKOUT DAY";

        var guide = SplitGuides.For(split.Id, IsPreset);
        About = Description ?? guide?.About;
        GoodFor = guide?.GoodFor ?? [];

        // Pre-fill with the user's saved choice if this is their active split, otherwise the defaults
        StartDay = IsPreset && IsActive ? user.SplitStartDay ?? DayOfWeek.Monday : DayOfWeek.Monday;
        SelectedVariant = Variants.FirstOrDefault(v => IsActive && v.Id == user.SelectedVariantId)
                          ?? Variants.FirstOrDefault(v => v.IsDefault)
                          ?? Variants.FirstOrDefault();
        foreach (var d in StartDayOptions) d.IsSelected = d.Day == StartDay;   // the change hooks skip an unchanged value
        foreach (var v in VariantOptions) v.IsSelected = v.Variant == SelectedVariant;
        Rebuild();
        _ = LoadDayInfoAsync(days);
        _ = LoadUsageAsync();
    }

    /// <summary>The share of users on each preset and on custom splits, this split's bar marked.</summary>
    private async Task LoadUsageAsync()
    {
        try
        {
            var usage = await _usage.GetAsync(_session.CurrentUser!.Id);
            if (usage is null || usage.Users == 0) return;

            var presets = await _splits.GetPresetsAsync();
            var slices = SplitUsageChart.Build(presets
                .Select(p => (p.Name, Users: usage.Presets.FirstOrDefault(r => r.PresetId == p.Id)?.Users ?? 0, IsThis: IsPreset && p.Id == _split.Id))
                .Append((Name: "Custom splits", Users: usage.Custom, IsThis: !IsPreset)), usage.Users);

            var mine = slices.First(s => s.IsThis);
            MinePercent = mine.PercentText;
            UsageHeadline = IsPreset ? $"{mine.PercentText} of lifters use {Name}." : $"{mine.PercentText} of lifters use a custom split.";
            UsageNote = usage.Users == 1 ? "Based on 1 lifter with a split." : $"Based on {usage.Users} lifters with a split.";
            UsageSlices = slices;
        }
        catch (Exception)
        {
            UsageSlices = [];   // the card just stays hidden
        }
    }

    /// <summary>Each day's muscle groups, from the exercises the user put on it.</summary>
    private async Task LoadDayInfoAsync(IReadOnlyList<SplitDay> days)
    {
        try
        {
            var userId = _session.CurrentUser!.Id;
            var info = new List<SplitDayInfo>();
            foreach (var d in days)
            {
                var groups = (await _dayExercises.GetListAsync(userId, d.Id))
                    .Select(x => x.Exercise.MuscleGroup).Distinct().Select(g => g.ToString()).ToList();
                info.Add(new SplitDayInfo(d.Name, groups.Count > 0 ? string.Join(" · ", groups) : "No exercises yet"));
            }
            DayInfo = info;
        }
        catch (Exception)
        {
            DayInfo = days.Select(d => new SplitDayInfo(d.Name, "")).ToList();
        }
    }

    partial void OnSelectedVariantChanged(SplitVariant? value)
    {
        foreach (var v in VariantOptions) v.IsSelected = v.Variant == value;
        Toast = null;
        Rebuild();
    }

    partial void OnStartDayChanged(DayOfWeek value)
    {
        foreach (var d in StartDayOptions) d.IsSelected = d.Day == value;
        Toast = null;
        Rebuild();
    }

    private void Rebuild()
    {
        if (_split is null) return;

        var names = _split.Days.OrderBy(d => d.Order).Select(d => d.Name).ToList();
        if (names.Count == 0) { Clear(); return; }

        IReadOnlyList<ScheduleSlot> slots;
        if (IsPreset)
        {
            if (SelectedVariant is null) { Clear(); return; }
            slots = WeekSchedule.Build(SelectedVariant.Pattern, StartDay, names.Count);
        }
        else
        {
            var weekdays = _split.Days.Where(d => d.Weekday is not null).Select(d => d.Weekday!.Value).ToList();
            if (weekdays.Count == 0) { Clear(); return; }
            slots = WeekSchedule.FromWeekdays(weekdays);
        }

        DayOfWeek? start = IsPreset ? StartDay : null;
        Cells = slots.Select(s => new ScheduleCell(
            s.Weekday.ToString()[..3],
            s.RotationIndex is { } i ? names[i % names.Count] : "Rest",
            s.IsTraining,
            s.Weekday == start)).ToList();

        var training = slots.Count(s => s.IsTraining);
        TrainCount = training.ToString(CultureInfo.InvariantCulture);
        RestCount = (7 - training).ToString(CultureInfo.InvariantCulture);
        DaysTag = training == 1 ? "1 day a week" : $"{training} days a week";
        WeekSummary = $"{training} training day(s) · {7 - training} rest day(s) per week";
    }

    private void Clear()
    {
        Cells = [];
        WeekSummary = DaysTag = "";
        TrainCount = "0";
        RestCount = "7";
    }

    [RelayCommand]
    private async Task UseAsync()
    {
        try
        {
            var user = _session.CurrentUser!;
            var variantId = IsPreset ? SelectedVariant?.Id : null;
            DayOfWeek? startDay = IsPreset ? StartDay : null;

            if (!await _splits.SelectAsync(user.Id, _split.Id, variantId, startDay))
            {
                Error = "Could not select that split.";
                return;
            }

            // keep the in-memory session user in sync
            user.SelectedSplitId = _split.Id;
            user.SelectedVariantId = variantId;
            user.SplitStartDay = startDay;
            Error = null;
            var wasActive = IsActive;
            IsActive = true;
            Toast = wasActive
                ? "Saved. Your schedule changes from today on."
                : $"{Name} is now your split." + (IsPreset ? $" Your week starts on {StartDay}." : "") + " Past streak days are kept.";
        }
        catch (Exception)
        {
            Error = "Could not select that split.";
        }
    }

    [RelayCommand] private void Edit() => _shell().ShowSplitEditor(_split);
    [RelayCommand] private void Back() => _shell().ShowSplits();
}
