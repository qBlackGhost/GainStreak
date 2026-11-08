using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Services;

namespace GymApp.App.ViewModels;

/// <summary>One exercise in the rest day's picker. The same instance stays through searches and filters, so a pick
/// isn't lost when the list changes.</summary>
public partial class RestDayPickItem(PickableExercise exercise, RestDayPickerViewModel owner) : ObservableObject
{
    public PickableExercise Exercise { get; } = exercise;
    public int ExerciseId => Exercise.ExerciseId;
    public string Name => Exercise.Name;
    /// <summary>"60 kg × 8", the top set of the last time; null if never done.</summary>
    public string? LastText => Exercise.LastText;
    public bool HasLast => Exercise.LastText is not null;

    [ObservableProperty] public partial bool IsSelected { get; set; }

    [RelayCommand] private void Toggle() => owner.Toggle(this);
}

public record RestDayPickGroup(string Name, IReadOnlyList<RestDayPickItem> Items)
{
    public string Title => Name.ToUpperInvariant();
}

/// <summary>A chip above the list: Recent, All, or one muscle group ("Custom" for the user's own exercises).</summary>
public partial class RestDayPickFilter(string label, string? group, bool recent = false) : ObservableObject
{
    public string Label { get; } = label;
    /// <summary>The muscle group shown, or null for Recent and All.</summary>
    public string? Group { get; } = group;
    public bool IsRecent { get; } = recent;

    [ObservableProperty] public partial bool IsSelected { get; set; }
}

/// <summary>
/// "Start workout anyway" on a rest day, on phones (0.18.1, option B of the GainStreak Mobile canvas): a sheet over most
/// of the screen to pick any exercises, with a search, Recent and muscle group chips, and a tray of the picks in the
/// order picked. Start begins a live workout of those exercises.
/// </summary>
public partial class RestDayPickerViewModel : ObservableObject
{
    /// <summary>The Recent list, and the recent ones shown on top of All.</summary>
    public const int RecentCount = 12, RecentOnTop = 5;

    private readonly IReadOnlyList<RestDayPickItem> _all;
    private readonly List<RestDayPickItem> _picked = [];
    private readonly Func<IReadOnlyList<int>, Task> _start;
    private readonly Action _cancel;

    public RestDayPickerViewModel(IReadOnlyList<PickableExercise> choices, Func<IReadOnlyList<int>, Task> start, Action cancel)
    {
        _all = choices.Select(c => new RestDayPickItem(c, this)).ToList();
        _start = start;
        _cancel = cancel;

        var hasRecent = _all.Any(i => i.HasLast);
        Filters = (hasRecent ? new[] { new RestDayPickFilter("Recent", null, recent: true) } : [])
            .Append(new RestDayPickFilter("All", null))
            .Concat(_all.Select(i => i.Exercise.Group).Distinct()
                .OrderBy(g => g == "Custom" ? 1 : 0).ThenBy(g => Order(g))
                .Select(g => new RestDayPickFilter(g, g)))
            .ToList();
        SelectFilter(Filters[0]);
    }

    public IReadOnlyList<RestDayPickFilter> Filters { get; }
    public ObservableCollection<RestDayPickGroup> Groups { get; } = new();

    [ObservableProperty] public partial string Search { get; set; } = "";
    [ObservableProperty] public partial bool NoResults { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPicks), nameof(StartLabel))]
    public partial int PickedCount { get; set; }

    /// <summary>The tray: "Barbell row · Hammer curl · Lat pulldown".</summary>
    [ObservableProperty] public partial string PickedText { get; set; } = "";

    public bool HasPicks => PickedCount > 0;
    public string StartLabel => PickedCount switch
    {
        0 => "Pick exercises to start",
        1 => "Start · 1 exercise",
        _ => $"Start · {PickedCount} exercises"
    };

    private RestDayPickFilter? _filter;

    [RelayCommand]
    private void SelectFilter(RestDayPickFilter filter)
    {
        _filter = filter;
        foreach (var f in Filters) f.IsSelected = f == filter;
        Rebuild();
    }

    partial void OnSearchChanged(string value) => Rebuild();

    private void Rebuild()
    {
        var term = Search?.Trim() ?? "";
        var searching = term.Length > 0;
        var matches = _all.Where(i => !searching || i.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();

        List<RestDayPickGroup> groups;
        if (_filter is { IsRecent: true } && !searching)
            groups = [new RestDayPickGroup("Recent", matches.Where(i => i.HasLast).Take(RecentCount).ToList())];
        else if (_filter?.Group is { } group)
            groups = [new RestDayPickGroup(group, matches.Where(i => i.Exercise.Group == group).OrderBy(i => i.Name).ToList())];
        else
        {
            // All (or a search from Recent): the latest few on top when not searching, then every group
            groups = [];
            if (!searching && matches.Where(i => i.HasLast).Take(RecentOnTop).ToList() is { Count: > 0 } recent)
                groups.Add(new RestDayPickGroup("Recent", recent));
            groups.AddRange(matches.GroupBy(i => i.Exercise.Group)
                .OrderBy(g => g.Key == "Custom" ? 1 : 0).ThenBy(g => Order(g.Key))
                .Select(g => new RestDayPickGroup(g.Key, g.OrderBy(i => i.Name).ToList())));
        }
        groups = groups.Where(g => g.Items.Count > 0).ToList();

        Groups.Clear();
        foreach (var g in groups) Groups.Add(g);
        NoResults = groups.Count == 0;
    }

    /// <summary>Muscle groups in the app's usual order (the enum's), by name.</summary>
    private static int Order(string group) =>
        Enum.TryParse<Data.Models.MuscleGroup>(group, out var g) ? (int)g : int.MaxValue;

    internal void Toggle(RestDayPickItem item)
    {
        Error = null;
        if (item.IsSelected)
        {
            item.IsSelected = false;
            _picked.Remove(item);
        }
        else
        {
            if (_picked.Count >= WorkoutService.MaxPickedExercises)
            {
                Error = $"Pick up to {WorkoutService.MaxPickedExercises} exercises.";
                return;
            }
            item.IsSelected = true;
            _picked.Add(item);
        }
        PickedCount = _picked.Count;
        PickedText = string.Join(" · ", _picked.Select(i => i.Name));
    }

    [RelayCommand]
    private async Task Start()
    {
        if (!HasPicks || IsBusy) { Error = HasPicks ? null : "Pick at least one exercise."; return; }
        IsBusy = true;
        try { await _start(_picked.Select(i => i.ExerciseId).ToList()); }
        finally { IsBusy = false; }
    }

    [RelayCommand] private void Cancel() => _cancel();
}
