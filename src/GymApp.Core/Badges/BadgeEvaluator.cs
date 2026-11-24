namespace GymApp.Core;

/// <summary>
/// Where one badge stands. <see cref="Tier"/> is 1 to 4 for a tiered badge (0 before Steel) and always 0 for a
/// one-time badge, which only has <see cref="Earned"/>.
/// </summary>
/// <param name="EarnedOn">The day the current tier (or the badge) was reached, when the data shows it; null when it
/// doesn't (Streak, Recruiter, 100% Completion), and the caller stamps today.</param>
/// <param name="Value">The current amount (days, kg, workouts...), for the progress bar.</param>
/// <param name="Target">The amount the next tier (or the badge) needs; null once the top tier is reached.</param>
public sealed record BadgeProgress(string Id, bool Earned, int Tier, DateOnly? EarnedOn, double Value, double? Target);

/// <summary>
/// The badge rules (Phase 25): what the user's data earns, badge by badge. Pure: everything comes in through
/// <see cref="BadgeInputs"/>, nothing after <c>today</c> counts. Earned badges are kept by the caller (it only raises
/// stored tiers); this returns what the data shows now.
/// </summary>
public static class BadgeEvaluator
{
    public static IReadOnlyList<BadgeProgress> Evaluate(BadgeInputs inputs, DateOnly today)
    {
        var c = new Context(inputs, today);
        var result = new List<BadgeProgress>
        {
            // Consistency
            Tiered("streak", inputs.LongestStreak, null),
            Counted("perfect-week", PerfectWeeks(c)),
            Once("on-ice", c.ColdDays.FirstOrNull()),
            Counted("weekend-warrior", c.WorkoutDates.Where(d => d.DayOfWeek == DayOfWeek.Sunday && c.Worked(d.AddDays(-1)))),
            Counted("split-finisher", SplitCycles(c)),

            // Strength
            Counted("pr-breaker", c.PrDates),
            Running("heavy-lifter", Cumulative(c.Working.Select(s => (s.Date, s.Reps * s.WeightKg)))),
            Counted("century", c.WorkoutDates),
            Running("bodyweight-club", BodyweightMultiples(c)),
            Running("rep-machine", Cumulative(c.Working.Select(s => (s.Date, (double)s.Reps)))),
            Counted("exercise-explorer", c.Working.GroupBy(s => s.ExerciseId).Select(g => g.Min(s => s.Date))),
            Counted("pr-week", c.PrDates.GroupBy(PointsCalculator.MondayOf).Where(g => g.Count() >= 3)
                .Select(g => g.OrderBy(d => d).ElementAt(2))),

            // Body & health
            Running("fuel-streak", LongestRun(c.Inputs.Calories
                .Where(d => d.Date <= today && d.Goal is { } goal && CalorieGoals.HitTarget(goal, d.Consumed))
                .Select(d => d.Date), 1)),
            Running("weigh-in-habit", WeeklyRun(c.Inputs.WeighIns.Where(w => w.Date <= today && !w.FromHealth).Select(w => w.Date))),
            Running("cardio-minutes", Cumulative(c.Workouts.Select(w => (w.Date, (double)(w.CardioMinutes ?? 0)))
                .Concat(c.Cardio.Select(s => (s.Date, (double)s.Minutes))))),

            // Social & gyms
            Regular(c),
            Explorer(c),
            Counted("we-go-jim", WithFriends(c)),
            OnceValue("recruiter", inputs.AcceptedInvites, 1, null),
            Counted("top-of-the-board", inputs.WeekResults.Where(w => w.First && w.WeekStart.AddDays(6) <= today)
                .Select(w => w.WeekStart.AddDays(6))),

            // Hidden
            Once("ill-be-back", Comeback(c)),
            Once("night-owl", c.Live.Where(l => l.StartedLocal.Hour >= 23 || l.StartedLocal.Hour < 4).Select(l => l.Date).FirstOrNull()),
            Once("eye-of-the-tiger", c.Live.Where(l => l.StartedLocal.Hour is >= 4 and < 6).Select(l => l.Date).FirstOrNull()),
            Once("double-up", c.Cardio.Select(s => s.Date).Where(d => c.StrengthDays.Contains(d)).FirstOrNull()),
            Once("marathon-session", c.Live.Where(l => l.EndedLocal is { } end && !l.AutoStopped
                && end - l.StartedLocal >= TimeSpan.FromHours(2)).Select(l => l.Date).FirstOrNull()),
            Once("triple-digits", c.Working.Where(s => s.WeightKg >= 100).Select(s => s.Date).FirstOrNull()),
            Once("fresh-start", c.WorkoutDates.Where(d => d is { Month: 1, Day: 1 }).FirstOrNull()),
            Counted("first-of-month", c.WorkoutDates.Where(d => d.Day == 1 && d.Month != 1)),
            Once("birthday-gains", Birthday(c)),
            Once("leap-day", c.WorkoutDates.Where(d => d is { Month: 2, Day: 29 }).FirstOrNull()),
            Once("christmas-gains", c.WorkoutDates.Where(d => d is { Month: 12, Day: 25 }).FirstOrNull()),
            NthOnce("full-house", c.Workouts.Where(w => w.TotalExercises > 0 && w.FilledExercises == w.TotalExercises)
                .Select(w => w.Date), 10),
            Running("rest-day-never", LongestRun(c.WorkoutDates, 1), onceAt: 14),
            Once("this-is-fine", inputs.TrainingDays.Where(t => t.Outcome == DayOutcome.Graced && t.Date <= today)
                .Select(t => t.Date).Order().FirstOrNull()),
            Once("touch-grass", TouchGrass(c)),
            Once("light-weight-baby", c.Working.Where(s => s.WeightKg >= 150).Select(s => s.Date).FirstOrNull()),
            Running("never-skip-leg-day", WeeklyRun(c.Working.Where(s => s.IsLegs).Select(s => s.Date))),
        };

        result.Insert(result.FindIndex(r => r.Id == "ill-be-back"), Completion(inputs, result));
        result.AddRange(Monthly(c));
        return result;
    }

    // ---- shapes ----

    /// <summary>A tiered badge from a value whose dates aren't known.</summary>
    private static BadgeProgress Tiered(string id, double value, DateOnly? on)
    {
        var tiers = Def(id).Tiers;
        var tier = tiers.Count(t => value >= t);
        return new BadgeProgress(id, tier > 0, tier, tier > 0 ? on : null, value, Next(tiers, tier));
    }

    /// <summary>A tiered badge from a running value (cumulative or running best), in date order.</summary>
    private static BadgeProgress Running(string id, IEnumerable<(DateOnly Date, double Value)> series, double? onceAt = null)
    {
        var def = Def(id);
        IReadOnlyList<double> thresholds = onceAt is { } once ? [once] : def.Tiers;
        var tier = 0;
        DateOnly? on = null;
        double value = 0;
        foreach (var (date, v) in series.OrderBy(p => p.Date))
        {
            value = Math.Max(value, v);
            while (tier < thresholds.Count && value >= thresholds[tier])
            {
                tier++;
                on = date;
            }
        }
        return def.IsTiered
            ? new BadgeProgress(id, tier > 0, tier, on, value, Next(thresholds, tier))
            : new BadgeProgress(id, tier > 0, 0, on, value, tier > 0 ? null : thresholds[0]);
    }

    /// <summary>A tiered badge counting events, one per date given.</summary>
    private static BadgeProgress Counted(string id, IEnumerable<DateOnly> events) =>
        Running(id, events.Order().Select((d, i) => (d, (double)(i + 1))));

    private static BadgeProgress Once(string id, DateOnly? on) =>
        new(id, on is not null, 0, on, on is null ? 0 : 1, on is null ? 1 : null);

    private static BadgeProgress OnceValue(string id, double value, double target, DateOnly? on) =>
        new(id, value >= target, 0, value >= target ? on : null, value, value >= target ? null : target);

    /// <summary>A one-time badge earned on the <paramref name="n"/>th event.</summary>
    private static BadgeProgress NthOnce(string id, IEnumerable<DateOnly> events, int n)
    {
        var sorted = events.Order().ToList();
        return sorted.Count >= n
            ? new BadgeProgress(id, true, 0, sorted[n - 1], sorted.Count, null)
            : new BadgeProgress(id, false, 0, null, sorted.Count, n);
    }

    private static double? Next(IReadOnlyList<double> tiers, int tier) => tier < tiers.Count ? tiers[tier] : null;

    private static BadgeDefinition Def(string id) =>
        BadgeCatalog.Find(id) ?? throw new InvalidOperationException($"No badge {id} in the catalog.");

    // ---- series ----

    private static IEnumerable<(DateOnly, double)> Cumulative(IEnumerable<(DateOnly Date, double Amount)> amounts)
    {
        double total = 0;
        foreach (var g in amounts.GroupBy(a => a.Date).OrderBy(g => g.Key))
        {
            total += g.Sum(a => a.Amount);
            yield return (g.Key, total);
        }
    }

    /// <summary>The best run of dates <paramref name="step"/> days apart, as it grows.</summary>
    private static IEnumerable<(DateOnly, double)> LongestRun(IEnumerable<DateOnly> dates, int step)
    {
        DateOnly? prev = null;
        int run = 0, best = 0;
        foreach (var d in dates.Distinct().Order())
        {
            run = prev is { } p && d.DayNumber - p.DayNumber == step ? run + 1 : 1;
            best = Math.Max(best, run);
            prev = d;
            yield return (d, best);
        }
    }

    /// <summary>The best run of Monday–Sunday weeks in a row with at least one of <paramref name="dates"/>, dated on
    /// each week's first such date.</summary>
    private static IEnumerable<(DateOnly, double)> WeeklyRun(IEnumerable<DateOnly> dates)
    {
        var firstPerWeek = dates.GroupBy(PointsCalculator.MondayOf).ToDictionary(g => g.Key, g => g.Min());
        DateOnly? prev = null;
        int run = 0, best = 0;
        foreach (var monday in firstPerWeek.Keys.Order())
        {
            run = prev is { } p && monday.DayNumber - p.DayNumber == 7 ? run + 1 : 1;
            best = Math.Max(best, run);
            prev = monday;
            yield return (firstPerWeek[monday], best);
        }
    }

    // ---- badges with their own rules ----

    private static IEnumerable<DateOnly> PerfectWeeks(Context c) =>
        c.Inputs.TrainingDays
            .GroupBy(t => PointsCalculator.MondayOf(t.Date))
            .Where(g => g.Key.AddDays(6) < c.Today && g.All(t => t.Outcome == DayOutcome.Done))
            .Select(g => g.Key.AddDays(6));

    private static IEnumerable<DateOnly> SplitCycles(Context c)
    {
        int? split = null;
        var done = new HashSet<int>();
        foreach (var w in c.Workouts)
        {
            if (w is not { SplitId: { } id, SplitDayId: { } day } || !c.Inputs.SplitDays.TryGetValue(id, out var days)) continue;
            if (split != id)
            {
                split = id;
                done.Clear();
            }
            done.Add(day);
            if (days.Count > 0 && done.IsSupersetOf(days))
            {
                yield return w.Date;
                done.Clear();
            }
        }
    }

    private static IEnumerable<(DateOnly, double)> BodyweightMultiples(Context c)
    {
        var weighIns = c.Inputs.WeighIns.Where(w => w.WeightKg > 0).OrderBy(w => w.Date).ToList();
        var i = -1;
        foreach (var s in c.Working)                                            // in date order
        {
            while (i + 1 < weighIns.Count && weighIns[i + 1].Date <= s.Date) i++;
            if (i >= 0) yield return (s.Date, s.WeightKg / weighIns[i].WeightKg);
        }
    }

    private static BadgeProgress Regular(Context c)
    {
        const int needed = 25;
        var perGym = new Dictionary<string, int>();
        foreach (var w in c.Workouts)
        {
            if (w.GymKey is null) continue;
            var n = perGym[w.GymKey] = perGym.GetValueOrDefault(w.GymKey) + 1;
            if (n == needed) return new BadgeProgress("regular", true, 0, w.Date, n, null);
        }
        var best = perGym.Count == 0 ? 0 : perGym.Values.Max();
        return new BadgeProgress("regular", false, 0, null, best, needed);
    }

    private static BadgeProgress Explorer(Context c) =>
        NthOnce("explorer", c.Workouts.Where(w => w.GymKey is not null).GroupBy(w => w.GymKey).Select(g => g.Min(w => w.Date)), 5);

    private static IEnumerable<DateOnly> WithFriends(Context c)
    {
        var friends = c.Inputs.FriendCheckIns
            .Where(f => f.GymKey.StartsWith("osm:", StringComparison.Ordinal))
            .Select(f => (f.Date, f.GymKey))
            .ToHashSet();
        return c.Workouts.Where(w => w.GymKey is { } key && friends.Contains((w.Date, key))).Select(w => w.Date);
    }

    private static DateOnly? Comeback(Context c)
    {
        for (var i = 1; i < c.WorkoutDates.Count; i++)
            if (c.WorkoutDates[i].DayNumber - c.WorkoutDates[i - 1].DayNumber >= 15) return c.WorkoutDates[i];
        return null;
    }

    private static DateOnly? Birthday(Context c)
    {
        if (c.Inputs.BirthDate is not { } born) return null;
        return c.WorkoutDates.Where(d => d >= born && d == BirthdayIn(born, d.Year)).FirstOrNull();
    }

    /// <summary>The birthday in <paramref name="year"/>; a February 29 birthday is February 28 in other years.</summary>
    internal static DateOnly BirthdayIn(DateOnly born, int year) =>
        born is { Month: 2, Day: 29 } && !DateTime.IsLeapYear(year) ? new DateOnly(year, 2, 28) : new DateOnly(year, born.Month, born.Day);

    private static DateOnly? TouchGrass(Context c) =>
        c.Inputs.Vacations
            .Where(v => v.ReturnDate <= c.Today)
            .Select(v => c.WorkoutDates.Where(d => d >= v.ReturnDate && d.DayNumber - v.ReturnDate.DayNumber <= 3).FirstOrNull())
            .Where(d => d is not null)
            .Min();

    private static BadgeProgress Completion(BadgeInputs inputs, IReadOnlyList<BadgeProgress> evaluated)
    {
        var earned = evaluated.Where(p => p.Earned).Select(p => p.Id).Concat(inputs.PreviouslyEarned).ToHashSet();
        var set = BadgeCatalog.CompletionSet.ToList();
        var have = set.Count(b => earned.Contains(b.Id));
        return OnceValue("completion", have, set.Count, null);
    }

    private static IEnumerable<BadgeProgress> Monthly(Context c)
    {
        foreach (var month in MonthlyChallenges.MonthsUntil(c.Today))
        {
            var id = MonthlyChallenges.IdFor(month);
            var target = MonthlyChallenges.Target(month);
            var dates = c.WorkoutDates.Where(d => d.Year == month.Year && d.Month == month.Month).ToList();
            yield return dates.Count >= target
                ? new BadgeProgress(id, true, 0, dates[target - 1], dates.Count, null)
                : new BadgeProgress(id, false, 0, null, dates.Count, target);
        }
    }

    /// <summary>The inputs cut to <c>today</c> and sorted once.</summary>
    private sealed class Context
    {
        public Context(BadgeInputs inputs, DateOnly today)
        {
            Inputs = inputs;
            Today = today;
            Workouts = inputs.Workouts.Where(w => w.Date <= today).OrderBy(w => w.Date).ToList();
            WorkoutDates = Workouts.Select(w => w.Date).Distinct().ToList();
            workedOn = WorkoutDates.ToHashSet();
            Working = inputs.Sets.Where(s => s.Kind == SetKind.Working && s.Date <= today)
                .OrderBy(s => s.Date).ThenBy(s => s.SetNumber).ToList();
            StrengthDays = Working.Select(s => s.Date).ToHashSet();
            Live = inputs.LiveWorkouts.Where(l => l.Date <= today).OrderBy(l => l.StartedLocal).ToList();
            Cardio = inputs.Cardio.Where(s => s.Date <= today).OrderBy(s => s.Date).ToList();
            ColdDays = inputs.ColdDays.Where(d => d <= today).Order().ToList();
            PrDates = Working.GroupBy(s => s.ExerciseId)
                .SelectMany(g => PrCalculator.Compute(
                        g.Select(s => new SetEntry(s.Date, s.SetNumber, s.Kind, s.Reps, s.WeightKg)),
                        inputs.PrAdjustments.GetValueOrDefault(g.Key))
                    .Entries.Where(e => e.Kind == PrEntryKind.Pr).Select(e => e.Date))
                .Order().ToList();
        }

        private readonly HashSet<DateOnly> workedOn;

        public BadgeInputs Inputs { get; }
        public DateOnly Today { get; }
        public List<BadgeWorkout> Workouts { get; }
        public List<DateOnly> WorkoutDates { get; }
        public List<BadgeSet> Working { get; }
        public HashSet<DateOnly> StrengthDays { get; }
        public List<BadgeLiveWorkout> Live { get; }
        public List<BadgeCardio> Cardio { get; }
        public List<DateOnly> ColdDays { get; }
        public List<DateOnly> PrDates { get; }

        public bool Worked(DateOnly d) => workedOn.Contains(d);
    }

    private static DateOnly? FirstOrNull(this IEnumerable<DateOnly> dates)
    {
        foreach (var d in dates.Order()) return d;
        return null;
    }
}
