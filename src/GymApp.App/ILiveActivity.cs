using System;

namespace GymApp.App;

/// <summary>What the Live Activity shows of an open live workout (0.20.0).</summary>
/// <param name="DayName">"Push", or "Extra workout" for a rest day's picked exercises.</param>
/// <param name="Percent">Working sets done out of the planned ones (<c>LiveWorkoutRules.SetProgress</c>), 0 to 100.</param>
/// <param name="NextExercise">The exercise on the card; null once every exercise is done.</param>
/// <param name="StartedAt">Local time, for the running workout timer.</param>
/// <param name="EndsAt">Local time the session stops by itself (4 hours, or midnight): the activity goes stale then.</param>
public record LiveActivityState(string DayName, int Percent, int DoneSets, int PlannedSets, string? NextExercise,
    DateTime StartedAt, DateTime EndsAt);

/// <summary>
/// The iPhone's Live Activity for an open live workout (0.20.0): the Dynamic Island and the Lock Screen show the percent
/// done once the user leaves the app. The system hides it while the app is in front, so the app keeps it up to date
/// all the time and the island appears by itself on leaving. Other heads have none (<see cref="NoLiveActivity"/>).
/// Calls never throw; a phone with Live Activities turned off just shows nothing.
/// </summary>
public interface ILiveActivity
{
    /// <summary>Starts the activity, or updates the one already showing.</summary>
    void Show(LiveActivityState state);

    /// <summary>Ends the activity, if one is showing (the workout finished, ended, or stopped by itself).</summary>
    void End();
}

public sealed class NoLiveActivity : ILiveActivity
{
    public void Show(LiveActivityState state) { }
    public void End() { }
}
