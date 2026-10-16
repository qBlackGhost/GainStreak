using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.Core;

namespace GymApp.Desktop.ViewModels;

/// <summary>One city in a city search result list (profile home city, gym picker).</summary>
public partial class CityCandidateViewModel(CityCandidate candidate, Func<CityCandidate, Task> choose) : ObservableObject
{
    public CityCandidate Candidate { get; } = candidate;
    public string Name => Candidate.Name;
    public string DisplayName => Candidate.DisplayName;

    [RelayCommand] private Task Choose() => choose(Candidate);
}
