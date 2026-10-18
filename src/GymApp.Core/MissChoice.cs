namespace GymApp.Core;

/// <summary>What the user chose to do about a missed training day, the day after it. Stored as an int, so don't
/// reorder the members.</summary>
public enum MissChoice
{
    ShiftWeek,     // the whole schedule moves one day later from the day after the miss
    ShiftToRest,   // make it up before the next rest day (the grace rule's shift chain)
    Continue       // skip it: the streak resets and today's workout stays as planned
}
