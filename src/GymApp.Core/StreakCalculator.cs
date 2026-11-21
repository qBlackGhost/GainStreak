namespace GymApp.Core;

/// <summary>From <see cref="EffectiveFrom"/> on, the user's week looks like <see cref="WeekPattern"/>:
/// 7 chars of W (workout) / R (rest), Monday first.</summary>
public record ScheduleChange(DateOnly EffectiveFrom, string WeekPattern);

public enum StreakStatus
{
    NotStarted,    // no workout logged yet
    Active,        // streak alive, nothing owed, today is covered (rest day or workout logged)
    PendingToday,  // nothing owed, but today is a training day and nothing is logged yet
    NeedsChoice,   // yesterday's training day was missed with a streak at stake, no choice is stored, today isn't logged
    AtRisk,        // a day is owed (a choice was made, or a shift chain is open) and today isn't logged yet
    Shifted,       // today is logged but the schedule slipped a day, so a workout is owed tomorrow
    Broken         // count is 0 after a missed training day that was never made up
}

/// <param name="OwedDay">The training day the open debt is for, or null when nothing is owed.</param>
/// <param name="Stale">Today is a rest day that didn't add to the streak (0.18.1): the streak is kept but not growing,
/// and the app shows it cold (ice) instead of on fire. Only with Status Active.</param>
/// <param name="VacationUntil">Today is a vacation day (0.19.0): the return date, the first normal day again.</param>
public record StreakResult(int Current, int Longest, StreakStatus Status, DateOnly? OwedDay = null, bool Stale = false,
    DateOnly? VacationUntil = null);

/// <summary>
/// The rest-day rule from 0.18.1: from <see cref="From"/> on, a rest day keeps the streak alive without adding to it,
/// unless a verified workout (done at one of the user's gyms, <see cref="Verified"/>) is logged on it. Rest days
/// before <see cref="From"/> still count +1, so nobody's streak dropped with the update.
/// </summary>
/// <param name="From">The first day under the rule: the day this user first ran 0.18.1 (User.StreakRestRuleFrom).</param>
/// <param name="Verified">The dates of the user's verified workout logs.</param>
/// <param name="Frozen">The user's vacation days (0.19.0, <see cref="VacationRules"/>). On one the streak neither grows nor
/// breaks, unless a verified workout is logged that day (+1); a debt still open is closed (the owed day is Graced).</param>
public record RestDayRule(DateOnly From, IReadOnlySet<DateOnly> Verified, IReadOnlySet<DateOnly>? Frozen = null)
{
    /// <summary>Whether <paramref name="d"/> is a vacation day.</summary>
    public bool IsFrozen(DateOnly d) => Frozen?.Contains(d) == true;

    /// <summary>What rest day <paramref name="d"/> adds with no debt open, or when it pays one.</summary>
    internal bool Counts(DateOnly d, bool worked) => d < From || (worked && Verified.Contains(d));

    /// <summary>Whether rest day <paramref name="d"/> falls under the rule (it no longer counts by itself).</summary>
    internal bool Applies(DateOnly d) => d >= From;
}

/// <summary>How one scheduled training day turned out under the grace rule.</summary>
public enum DayOutcome
{
    Done,     // logged on its date
    Graced,   // missed, but paid by a later workout (shift rule)
    Missed,   // not paid, the streak broke on it (or it came before the first workout)
    Pending,  // not settled yet: today or later, or still owed
    Frozen    // a vacation day (0.19.0) without a verified workout: neither done nor missed
}

public record TrainingDayOutcome(DateOnly Date, DayOutcome Outcome);

public static class StreakCalculator
{
    /// <summary>
    /// Recomputes the streak from scratch by walking forward day by day (grace rule with missed-day choices).
    /// Rules:
    ///  - The walk starts on the first logged workout; nothing counts before it.
    ///  - Rest days count (+1), logged or not, while there is a streak. A logged training day counts (+1).
    ///  - No streak, nothing to save: while the count is 0, a missed training day and an unlogged rest day change nothing.
    ///  - Otherwise a missed training day (a fresh miss) is counted provisionally and leaves a debt. What pays it
    ///    depends on the choice stored for it in <paramref name="choices"/>:
    ///     - ShiftToRest, or no choice: a workout the next day pays it; if that next day is a training day, its own
    ///       training becomes the new debt (the schedule shifts) until a rest day absorbs the slip.
    ///     - ShiftWeek: a workout the next day pays it and nothing more is owed (the schedule itself moved).
    ///     - Continue: the streak breaks before the next day, which is then counted normally.
    ///    A day with an open debt and no workout breaks the streak (count 0); that day is consumed.
    ///  - Today is never a miss: an open debt for a fresh miss with no choice makes it NeedsChoice, any other
    ///    open debt AtRisk, an unlogged training day PendingToday.
    ///  - Each day uses the schedule that was in force on that day. The first schedule entry also
    ///    covers days before its date. With no schedule at all, every day counts as a training day.
    ///  - Longest only takes settled counts (no debt open), plus the current count.
    ///  - A vacation day (<see cref="RestDayRule.Frozen"/>, 0.19.0) closes an open debt (Graced) and adds only for a
    ///    verified workout; otherwise nothing changes. A frozen today is Active and Stale, with VacationUntil set.
    /// </summary>
    /// <param name="choices">The user's missed-day choices, keyed by the missed date.</param>
    public static StreakResult Calculate(
        IEnumerable<DateOnly> workoutDates,
        IReadOnlyList<ScheduleChange> schedule,
        IReadOnlyDictionary<DateOnly, MissChoice> choices,
        DateOnly today,
        RestDayRule? restRule = null) =>
        Replay(workoutDates, schedule, choices, today, restRule, null, null, null);

    /// <summary>
    /// The outcome of every training day from <paramref name="from"/> to <paramref name="until"/>, from the
    /// same replay as <see cref="Calculate"/>. Training days before the first workout are Missed (nothing counts
    /// before it); days from today on, and a day still owed, are Pending.
    /// A workout that pays an owed day makes that day Graced. If it falls on a training day, that day's own
    /// training is owed next, so it ends up Graced (or Missed) rather than Done. A miss the user chose to skip
    /// (Continue) is Missed.
    /// </summary>
    public static IReadOnlyList<TrainingDayOutcome> Timeline(
        IEnumerable<DateOnly> workoutDates,
        IReadOnlyList<ScheduleChange> schedule,
        IReadOnlyDictionary<DateOnly, MissChoice> choices,
        DateOnly today,
        DateOnly from,
        DateOnly until,
        RestDayRule? restRule = null)
    {
        var settled = new Dictionary<DateOnly, DayOutcome>();
        Replay(workoutDates, schedule, choices, today, restRule, settled, null, null);

        var ordered = schedule.OrderBy(s => s.EffectiveFrom).ToList();
        var result = new List<TrainingDayOutcome>();
        for (var d = from; d <= until; d = d.AddDays(1))
        {
            if (!IsTraining(ordered, d)) continue;
            var outcome = settled.TryGetValue(d, out var o) ? o
                        : restRule?.IsFrozen(d) == true ? DayOutcome.Frozen
                        : d < today ? DayOutcome.Missed : DayOutcome.Pending;
            result.Add(new TrainingDayOutcome(d, outcome));
        }
        return result;
    }

    /// <summary>The missed training day the most recent break was for (the owed day that wasn't made up, or was
    /// skipped with Continue), or null if the streak never broke. Home uses it for "Your streak ended on ...".</summary>
    public static DateOnly? LastBreak(
        IEnumerable<DateOnly> workoutDates,
        IReadOnlyList<ScheduleChange> schedule,
        IReadOnlyDictionary<DateOnly, MissChoice> choices,
        DateOnly today,
        RestDayRule? restRule = null)
    {
        DateOnly? last = null;
        Replay(workoutDates, schedule, choices, today, restRule, null, d => last = d, null);
        return last;
    }

    /// <summary>
    /// The days the streak was kept on ice (0.18.1), in date order: a rest day under the rest-day rule, or a vacation
    /// day, that kept a running streak without adding to it. Today is included when it is one (the same day
    /// <see cref="StreakResult.Stale"/> reports). Badges (Phase 25) use it.
    /// </summary>
    public static IReadOnlyList<DateOnly> ColdDays(
        IEnumerable<DateOnly> workoutDates,
        IReadOnlyList<ScheduleChange> schedule,
        IReadOnlyDictionary<DateOnly, MissChoice> choices,
        DateOnly today,
        RestDayRule? restRule)
    {
        var cold = new List<DateOnly>();
        if (restRule is null) return cold;
        Replay(workoutDates, schedule, choices, today, restRule, null, null, cold.Add);
        return cold;
    }

    /// <summary>The walk behind the public methods. When <paramref name="outcomes"/> is given, each training day the walk
    /// settles (Done, Graced, Missed) or leaves owed (Pending) is written to it. <paramref name="onCold"/> gets each day
    /// the streak was kept on ice.</summary>
    private static StreakResult Replay(
        IEnumerable<DateOnly> workoutDates,
        IReadOnlyList<ScheduleChange> schedule,
        IReadOnlyDictionary<DateOnly, MissChoice> choices,
        DateOnly today,
        RestDayRule? restRule,
        Dictionary<DateOnly, DayOutcome>? outcomes,
        Action<DateOnly>? onBreak,
        Action<DateOnly>? onCold)
    {
        if (schedule.Any(s => !WeekSchedule.IsValidPattern(s.WeekPattern)))
            throw new ArgumentException("Schedule patterns must be 7 characters of W/R with at least one W.", nameof(schedule));

        var logged = workoutDates.Where(d => d <= today).ToHashSet();
        if (logged.Count == 0) return new StreakResult(0, 0, StreakStatus.NotStarted);

        var ordered = schedule.OrderBy(s => s.EffectiveFrom).ToList();

        var idx = 0;
        var count = 0;
        var debt = false;
        var owed = default(DateOnly);   // the training day the open debt is for
        var fresh = false;              // the owed day was missed with no debt open (not a day owed inside a chain)
        var longest = 0;
        var countBeforeToday = 0;
        var paidToday = false;          // today's workout made up an owed day: not frozen, even if the count didn't grow
        var everCounted = false;        // under the rest-day rule an unverified rest-day workout doesn't start a streak
        StreakStatus? stopped = null;

        for (var d = logged.Min(); d <= today; d = d.AddDays(1))
        {
            while (idx + 1 < ordered.Count && ordered[idx + 1].EffectiveFrom <= d) idx++;
            var isTraining = ordered.Count == 0 || WeekPattern.IsTrainingDay(ordered[idx].WeekPattern, d.DayOfWeek);
            var worked = logged.Contains(d);
            var restCounts = restRule?.Counts(d, worked) ?? true;   // a rest day adds +1 (always, before the rule)
            if (d == today) countBeforeToday = count;
            MissChoice? choice = debt && fresh && choices.TryGetValue(owed, out var c) ? c : null;

            if (choice == MissChoice.Continue)                                      // skipped: break before d
            {
                count = 0;
                debt = false;
                Set(outcomes, owed, DayOutcome.Missed);
                onBreak?.Invoke(owed);
            }

            if (restRule?.IsFrozen(d) == true)                                      // vacation (0.19.0): kept, not growing
            {
                if (debt)                                                           // the first frozen day closes a chain
                {
                    Set(outcomes, owed, DayOutcome.Graced);
                    debt = false;
                }
                if (worked && restRule.Verified.Contains(d))                        // a verified workout still adds (or restarts)
                {
                    count++;
                    if (isTraining) Set(outcomes, d, DayOutcome.Done);
                }
                else
                {
                    if (isTraining) Set(outcomes, d, DayOutcome.Frozen);
                    if (count > 0) onCold?.Invoke(d);
                }
                if (count > 0) everCounted = true;
                longest = Math.Max(longest, count);
                continue;
            }

            if (debt)
            {
                if (worked)                                                         // paid
                {
                    if (isTraining || restCounts) count++;                          // a rest day that pays adds only if it counts
                    if (d == today) paidToday = true;
                    Set(outcomes, owed, DayOutcome.Graced);
                    if (choice == MissChoice.ShiftWeek)                             // the schedule moved; d is the missed workout's new home
                    {
                        debt = false;
                        if (isTraining) Set(outcomes, d, DayOutcome.Done);
                    }
                    else                                                            // a training day shifts
                    {
                        debt = isTraining;
                        owed = d;
                        fresh = false;
                    }
                }
                else if (d == today)
                {
                    stopped = fresh && choice is null ? StreakStatus.NeedsChoice : StreakStatus.AtRisk;
                    break;
                }
                else                                                                // break; day is consumed
                {
                    count = 0;
                    debt = false;
                    Set(outcomes, owed, DayOutcome.Missed);
                    if (isTraining) Set(outcomes, d, DayOutcome.Missed);
                    onBreak?.Invoke(owed);
                }
            }
            else if (isTraining)
            {
                if (worked) { count++; Set(outcomes, d, DayOutcome.Done); }
                else if (d == today) { stopped = StreakStatus.PendingToday; break; }
                else if (count == 0) Set(outcomes, d, DayOutcome.Missed);           // no streak, nothing to save
                else { count++; debt = true; owed = d; fresh = true; }              // provisional
            }
            else if (restRule is not null && restRule.Applies(d))                   // rest day under the 0.18.1 rule
            {
                if (restCounts) count++;                                            // only a verified workout adds (or restarts)
                else if (count > 0) onCold?.Invoke(d);                              // kept on ice
            }
            else if (count > 0 || worked) count++;                                  // rest day; after a break only a workout restarts

            if (count > 0) everCounted = true;

            if (!debt) longest = Math.Max(longest, count);
        }

        if (debt) Set(outcomes, owed, DayOutcome.Pending);

        longest = Math.Max(longest, count);
        if (!everCounted) return new StreakResult(0, 0, StreakStatus.NotStarted);   // only unverified rest-day workouts
        var status = count == 0 ? StreakStatus.Broken
                   : stopped ?? (debt ? StreakStatus.Shifted : StreakStatus.Active);
        var frozenToday = restRule?.IsFrozen(today) == true;
        var stale = status == StreakStatus.Active && restRule is not null && count == countBeforeToday && !paidToday
                    && (frozenToday || (restRule.Applies(today) && !IsTraining(ordered, today)));
        return new StreakResult(count, longest, status, debt ? owed : null, stale,
            frozenToday ? ReturnDate(restRule!, today) : null);
    }

    /// <summary>The first day after <paramref name="today"/> that isn't frozen.</summary>
    private static DateOnly ReturnDate(RestDayRule rule, DateOnly today)
    {
        var d = today.AddDays(1);
        while (rule.IsFrozen(d)) d = d.AddDays(1);
        return d;
    }

    private static void Set(Dictionary<DateOnly, DayOutcome>? outcomes, DateOnly day, DayOutcome outcome)
    {
        if (outcomes is not null) outcomes[day] = outcome;
    }

    /// <summary>Whether <paramref name="day"/> is a training day under the schedule in force on it (every day, with no
    /// schedule). The reminders (16g) plan with it.</summary>
    public static bool IsTrainingDay(IReadOnlyList<ScheduleChange> schedule, DateOnly day) =>
        IsTraining(schedule.OrderBy(s => s.EffectiveFrom).ToList(), day);

    private static bool IsTraining(List<ScheduleChange> ordered, DateOnly d)
    {
        if (ordered.Count == 0) return true;
        var entry = ordered.LastOrDefault(s => s.EffectiveFrom <= d) ?? ordered[0];   // first entry covers earlier days
        return WeekPattern.IsTrainingDay(entry.WeekPattern, d.DayOfWeek);
    }
}
