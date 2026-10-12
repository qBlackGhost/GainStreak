using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Data.Models;

namespace GymApp.Desktop.ViewModels;

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
    }

    public string Name => Split.Name;
    public bool IsRecommended => Split.IsRecommended;
    public bool IsCustom => Split.OwnerUserId is not null;
    public string Summary { get; }
    public string VariantsText { get; }          // "2 days / week  ·  3 days / week", empty for custom splits

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteLabel))]
    public partial bool IsConfirmingDelete { get; set; }

    public string DeleteLabel => IsConfirmingDelete ? "Confirm?" : "Delete";

    [RelayCommand] private void Open() => _owner.Open(this);
    [RelayCommand] private void Edit() => _owner.Edit(this);

    [RelayCommand]
    private System.Threading.Tasks.Task Delete()
    {
        if (!IsConfirmingDelete) { IsConfirmingDelete = true; return System.Threading.Tasks.Task.CompletedTask; }
        return _owner.DeleteAsync(this);
    }
}
