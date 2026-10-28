using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Data.Models;

namespace GymApp.App.ViewModels;

public partial class SplitItemViewModel : ObservableObject
{
    private readonly SplitsViewModel _owner;
    public WorkoutSplit Split { get; }

    public SplitItemViewModel(WorkoutSplit split, bool isSelected, SplitsViewModel owner)
    {
        Split = split; _owner = owner; IsSelected = isSelected;
        Summary = split.Days.Any(d => d.Weekday is not null)
            ? string.Join("  ·  ", split.Days.Select(d => $"{d.Weekday.ToString()![..3]} {d.Name}"))
            : string.Join(" → ", split.Days.Select(d => d.Name));
        VariantsText = string.Join("  ·  ", split.Variants.OrderBy(v => v.Id).Select(v => v.Name));
        Sequence = split.Days.OrderBy(d => d.Order).Select(d => d.Name).ToList();

        // "6 / wk", "2–3 / wk" when the variants differ; custom splits count their scheduled days
        var perWeek = split.Variants.Count > 0
            ? split.Variants.Select(v => v.Pattern.Count(c => c == 'W')).Distinct().Order().ToList()
            : [split.Days.Any(d => d.Weekday is not null) ? split.Days.Count(d => d.Weekday is not null) : split.Days.Count];
        DaysText = perWeek.Count > 1 ? $"{perWeek[0]}–{perWeek[^1]} / wk" : $"{perWeek[0]} / wk";
        RecommendedDaysTag = SplitTags.RecommendedDays(split);
    }

    public string Name => Split.Name;
    public bool IsRecommended => Split.IsRecommended;
    public bool IsCustom => Split.OwnerUserId is not null;
    public string Summary { get; }
    public string VariantsText { get; }          // "2 days / week  ·  3 days / week", empty for custom splits
    public IReadOnlyList<string> Sequence { get; }   // the day names in rotation order, for the chips
    public string DaysText { get; }
    public string? RecommendedDaysTag { get; }   // presets only

    /// <summary>"18% of lifters", once the server's counts are in; presets only.</summary>
    [ObservableProperty] public partial string? UsageText { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteLabel))]
    public partial bool IsConfirmingDelete { get; set; }

    public string DeleteLabel => IsConfirmingDelete ? "Confirm delete" : "Delete";

    [RelayCommand] private void Open() => _owner.Open(this);
    [RelayCommand] private void Edit() => _owner.Edit(this);

    [RelayCommand]
    private System.Threading.Tasks.Task Delete()
    {
        if (!IsConfirmingDelete) { IsConfirmingDelete = true; return System.Threading.Tasks.Task.CompletedTask; }
        return _owner.DeleteAsync(this);
    }
}
