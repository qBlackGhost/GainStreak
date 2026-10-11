using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;
using GymApp.Desktop.Models;
using GymApp.Desktop.Services;

namespace GymApp.Desktop.ViewModels;

public record ScheduleCell(string Label, string Title, bool IsTraining, bool IsStart);

public partial class SplitDetailViewModel : ViewModelBase
{
    private readonly SplitService _splits;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private WorkoutSplit _split = null!;

    public IReadOnlyList<DayOfWeek> StartDays { get; } = new[]
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string? Description { get; set; }
    [ObservableProperty] public partial bool IsRecommended { get; set; }
    [ObservableProperty] public partial bool IsPreset { get; set; }
    [ObservableProperty] public partial string Rotation { get; set; } = "";
    [ObservableProperty] public partial string WeekSummary { get; set; } = "";
    [ObservableProperty] public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseLabel))]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVariants))]
    public partial IReadOnlyList<SplitVariant> Variants { get; set; } = [];

    [ObservableProperty] public partial SplitVariant? SelectedVariant { get; set; }
    [ObservableProperty] public partial DayOfWeek StartDay { get; set; } = DayOfWeek.Monday;
    [ObservableProperty] public partial IReadOnlyList<ScheduleCell> Cells { get; set; } = [];

    public bool HasVariants => Variants.Count > 1;
    public string UseLabel => IsActive ? "Save changes" : "Use this split";

    public SplitDetailViewModel(SplitService splits, SessionService session, Func<MainViewModel> shell)
    {
        _splits = splits; _session = session; _shell = shell;
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
        Variants = split.Variants.OrderBy(v => v.Id).ToList();

        // Pre-fill with the user's saved choice if this is their active split, otherwise the defaults
        StartDay = IsPreset && IsActive ? user.SplitStartDay ?? DayOfWeek.Monday : DayOfWeek.Monday;
        SelectedVariant = Variants.FirstOrDefault(v => IsActive && v.Id == user.SelectedVariantId)
                          ?? Variants.FirstOrDefault(v => v.IsDefault)
                          ?? Variants.FirstOrDefault();
        Rebuild();
    }

    partial void OnSelectedVariantChanged(SplitVariant? value) => Rebuild();
    partial void OnStartDayChanged(DayOfWeek value) => Rebuild();

    private void Rebuild()
    {
        if (_split is null) return;

        var names = _split.Days.OrderBy(d => d.Order).Select(d => d.Name).ToList();
        if (names.Count == 0) { Cells = []; WeekSummary = ""; return; }

        IReadOnlyList<ScheduleSlot> slots;
        if (IsPreset)
        {
            if (SelectedVariant is null) { Cells = []; WeekSummary = ""; return; }
            slots = WeekSchedule.Build(SelectedVariant.Pattern, StartDay, names.Count);
        }
        else
        {
            var weekdays = _split.Days.Where(d => d.Weekday is not null).Select(d => d.Weekday!.Value).ToList();
            if (weekdays.Count == 0) { Cells = []; WeekSummary = ""; return; }
            slots = WeekSchedule.FromWeekdays(weekdays);
        }

        DayOfWeek? start = IsPreset ? StartDay : null;
        Cells = slots.Select(s => new ScheduleCell(
            s.Weekday.ToString()[..3],
            s.RotationIndex is { } i ? names[i % names.Count] : "Rest",
            s.IsTraining,
            s.Weekday == start)).ToList();

        var training = slots.Count(s => s.IsTraining);
        WeekSummary = $"{training} training day(s) · {7 - training} rest day(s) per week";
    }

    [RelayCommand]
    private async Task UseAsync()
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
        IsActive = true;
    }

    [RelayCommand] private void Edit() => _shell().ShowSplitEditor(_split);
    [RelayCommand] private void Back() => _shell().ShowSplits();
}
