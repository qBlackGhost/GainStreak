using GymApp.Core;

namespace GymApp.Tests;

/// <summary>Shared helpers for the badge rule tests (Phase 25a).</summary>
internal static class Badges
{
    // 2027-03-01 is a Monday.
    public static readonly DateOnly Mon = new(2027, 3, 1);
    public static DateOnly D(int offset) => Mon.AddDays(offset);
    public static readonly DateOnly Far = new(2030, 1, 1);

    public static Dictionary<string, BadgeProgress> Eval(BadgeInputs inputs, DateOnly? today = null) =>
        BadgeEvaluator.Evaluate(inputs, today ?? Far).ToDictionary(p => p.Id);

    public static BadgeProgress One(string id, BadgeInputs inputs, DateOnly? today = null) => Eval(inputs, today)[id];

    public static BadgeWorkout[] Workouts(params DateOnly[] dates) => dates.Select(d => new BadgeWorkout(d)).ToArray();

    public static BadgeWorkout[] Days(int from, int count) => Enumerable.Range(from, count).Select(i => new BadgeWorkout(D(i))).ToArray();

    public static BadgeSet Working(DateOnly d, double kg, int reps = 5, int exercise = 1, int set = 1, bool legs = false) =>
        new(d, exercise, set, SetKind.Working, reps, kg, legs);
}

public class BadgeCatalogTests
{
    [Fact] public void Counts_21Visible_18Hidden()
    {
        Assert.Equal(38, BadgeCatalog.All.Count);
        Assert.Equal(20, BadgeCatalog.All.Count(b => !b.Hidden));     // + Monthly Challenge = 21 visible
        Assert.Equal(18, BadgeCatalog.All.Count(b => b.Hidden));
    }

    [Fact] public void Ids_AreUniqueKebabCase()
    {
        Assert.Equal(BadgeCatalog.All.Count, BadgeCatalog.All.Select(b => b.Id).Distinct().Count());
        Assert.All(BadgeCatalog.All, b => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", b.Id));
        Assert.All(BadgeCatalog.All, b => Assert.True(b.Id.Length <= 40));
    }

    [Fact] public void Tiered_HaveFourRisingThresholds() =>
        Assert.All(BadgeCatalog.All.Where(b => b.IsTiered), b =>
        {
            Assert.Equal(4, b.Tiers.Count);
            Assert.Equal(b.Tiers.Order(), b.Tiers);
        });

    [Fact] public void Hidden_HaveHints_VisibleDont() =>
        Assert.All(BadgeCatalog.All, b => Assert.Equal(b.Hidden, b.Hint is not null));

    [Fact] public void EveryBadge_HasAGlyphOrText() =>
        Assert.All(BadgeCatalog.All, b => Assert.True(b.Glyph is not null || b.Text is not null, b.Id));

    [Fact] public void HeavyLifter_NamesAndGlyphsFollowTheLevel()
    {
        var heavy = BadgeCatalog.Find("heavy-lifter")!;
        Assert.Equal("Elephant", BadgeCatalog.TierName(heavy, 2));
        Assert.Equal("whale", BadgeCatalog.GlyphFor(heavy, 3));
        Assert.Equal("car", BadgeCatalog.GlyphFor(heavy, 0));
        Assert.Equal("Steel", BadgeCatalog.TierName(BadgeCatalog.Find("streak")!, 1));
        Assert.Null(BadgeCatalog.TierName(heavy, 0));
    }

    [Fact] public void Monthly_FoundFromTheFirstMonthOn()
    {
        var dec = BadgeCatalog.Find("monthly-2026-12")!;
        Assert.Equal(BadgeMaterial.Limited, dec.Material);
        Assert.Equal("DEC", dec.Text);
        Assert.False(dec.IsTiered);
        Assert.Null(BadgeCatalog.Find("monthly-2026-11"));
        Assert.Null(BadgeCatalog.Find("monthly-nope"));
        Assert.Null(BadgeCatalog.Find("from-a-newer-app"));
    }

    [Fact] public void Evaluate_ReturnsEveryCatalogBadgeOnce()
    {
        var ids = BadgeEvaluator.Evaluate(new BadgeInputs(), Badges.Far).Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(BadgeCatalog.All, b => Assert.Contains(b.Id, ids));
    }

    [Fact] public void Evaluate_EmptyInputs_EarnNothing() =>
        Assert.DoesNotContain(BadgeEvaluator.Evaluate(new BadgeInputs(), Badges.Far), p => p.Earned);
}

public class BadgeConsistencyTests
{
    [Theory]
    [InlineData(6, 0, 7.0)]
    [InlineData(7, 1, 30.0)]
    [InlineData(100, 3, 365.0)]
    [InlineData(365, 4, null)]
    public void Streak_TiersFromTheLongest(int longest, int tier, double? next)
    {
        var p = Badges.One("streak", new BadgeInputs { LongestStreak = longest });
        Assert.Equal(tier, p.Tier);
        Assert.Equal(next, p.Target);
        Assert.Null(p.EarnedOn);
    }

    private static TrainingDayOutcome[] Week(int start, params DayOutcome[] outcomes) =>
        outcomes.Select((o, i) => new TrainingDayOutcome(Badges.D(start + i * 2), o)).ToArray();

    [Fact] public void PerfectWeek_AllDone_FinishedWeek()
    {
        var p = Badges.One("perfect-week", new BadgeInputs { TrainingDays = Week(0, DayOutcome.Done, DayOutcome.Done, DayOutcome.Done) });
        Assert.Equal(1, p.Tier);
        Assert.Equal(Badges.D(6), p.EarnedOn);
    }

    [Theory]
    [InlineData(DayOutcome.Graced)]
    [InlineData(DayOutcome.Missed)]
    [InlineData(DayOutcome.Frozen)]
    public void PerfectWeek_AnyOtherOutcome_DoesNotCount(DayOutcome other) =>
        Assert.Equal(0, Badges.One("perfect-week",
            new BadgeInputs { TrainingDays = Week(0, DayOutcome.Done, other, DayOutcome.Done) }).Tier);

    [Fact] public void PerfectWeek_TheCurrentWeek_IsNotFinished() =>
        Assert.Equal(0, Badges.One("perfect-week",
            new BadgeInputs { TrainingDays = Week(0, DayOutcome.Done, DayOutcome.Done) }, Badges.D(6)).Tier);

    [Fact] public void OnIce_OnceOnTheFirstColdDay()
    {
        var p = Badges.One("on-ice", new BadgeInputs { ColdDays = [Badges.D(5), Badges.D(2)] });
        Assert.True(p.Earned);
        Assert.Equal(0, p.Tier);
        Assert.Equal(Badges.D(2), p.EarnedOn);
    }

    [Fact] public void OnIce_ColdDayAfterToday_DoesNotCount() =>
        Assert.False(Badges.One("on-ice", new BadgeInputs { ColdDays = [Badges.D(5)] }, Badges.D(4)).Earned);

    [Fact] public void WeekendWarrior_SaturdayAndSunday()
    {
        var p = Badges.One("weekend-warrior", new BadgeInputs { Workouts = Badges.Workouts(Badges.D(5), Badges.D(6)) });
        Assert.Equal(1, p.Tier);
        Assert.Equal(Badges.D(6), p.EarnedOn);
    }

    [Fact] public void WeekendWarrior_SundayAndMonday_DoNotCount() =>
        Assert.Equal(0, Badges.One("weekend-warrior",
            new BadgeInputs { Workouts = Badges.Workouts(Badges.D(6), Badges.D(7)) }).Tier);

    private static readonly Dictionary<int, IReadOnlySet<int>> Splits = new()
    {
        [1] = new HashSet<int> { 10, 11, 12 },
        [2] = new HashSet<int> { 20, 21 },
    };

    [Fact] public void SplitFinisher_EveryDayOnce_InAnyOrder()
    {
        var p = Badges.One("split-finisher", new BadgeInputs
        {
            SplitDays = Splits,
            Workouts = [new(Badges.D(0), SplitId: 1, SplitDayId: 12), new(Badges.D(1), SplitId: 1, SplitDayId: 12),
                        new(Badges.D(2), SplitId: 1, SplitDayId: 10), new(Badges.D(3), SplitId: 1, SplitDayId: 11)],
        });
        Assert.Equal(1, p.Tier);
        Assert.Equal(Badges.D(3), p.EarnedOn);
    }

    [Fact] public void SplitFinisher_SwitchingSplits_StartsANewCycle() =>
        Assert.Equal(0, Badges.One("split-finisher", new BadgeInputs
        {
            SplitDays = Splits,
            Workouts = [new(Badges.D(0), SplitId: 1, SplitDayId: 10), new(Badges.D(1), SplitId: 1, SplitDayId: 11),
                        new(Badges.D(2), SplitId: 2, SplitDayId: 20), new(Badges.D(3), SplitId: 1, SplitDayId: 12)],
        }).Tier);
}

public class BadgeStrengthTests
{
    [Fact] public void PrBreaker_CountsPrsNotTheBaselineOrTies()
    {
        var p = Badges.One("pr-breaker", new BadgeInputs
        {
            Sets = [Badges.Working(Badges.D(0), 60), Badges.Working(Badges.D(2), 62.5), Badges.Working(Badges.D(4), 62.5),
                    new BadgeSet(Badges.D(5), 1, 1, SetKind.Warmup, 5, 100)],
        });
        Assert.Equal(1, p.Tier);
        Assert.Equal(1, p.Value);
        Assert.Equal(Badges.D(2), p.EarnedOn);
    }

    [Theory]
    [InlineData(1499.0, 0)]
    [InlineData(1500.0, 1)]
    [InlineData(6000.0, 2)]
    public void HeavyLifter_LifetimeVolume(double kgTotal, int tier) =>
        Assert.Equal(tier, Badges.One("heavy-lifter", new BadgeInputs
        {
            Sets = [Badges.Working(Badges.D(0), kgTotal / 10, reps: 10), new BadgeSet(Badges.D(0), 1, 2, SetKind.Warmup, 10, 500)],
        }).Tier);

    [Fact] public void HeavyLifter_TierReachedOnTheDayItAddsUp()
    {
        var p = Badges.One("heavy-lifter", new BadgeInputs
        {
            Sets = [Badges.Working(Badges.D(0), 100, reps: 10), Badges.Working(Badges.D(3), 50, reps: 10)],
        });
        Assert.Equal(1, p.Tier);
        Assert.Equal(Badges.D(3), p.EarnedOn);
        Assert.Equal(6000, p.Target);
    }

    [Theory]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    public void Century_Workouts(int count, int tier) =>
        Assert.Equal(tier, Badges.One("century", new BadgeInputs { Workouts = Badges.Days(0, count) }).Tier);

    [Theory]
    [InlineData(79.0, 0)]
    [InlineData(80.0, 1)]
    [InlineData(160.0, 3)]
    public void BodyweightClub_AgainstTheLatestWeighIn(double kg, int tier) =>
        Assert.Equal(tier, Badges.One("bodyweight-club", new BadgeInputs
        {
            WeighIns = [new(Badges.D(0), 90), new(Badges.D(1), 80), new(Badges.D(9), 50)],
            Sets = [Badges.Working(Badges.D(2), kg)],
        }).Tier);

    [Fact] public void BodyweightClub_NoWeighInYet_NoBadge() =>
        Assert.Equal(0, Badges.One("bodyweight-club", new BadgeInputs
        {
            WeighIns = [new(Badges.D(5), 60)],
            Sets = [Badges.Working(Badges.D(2), 200)],
        }).Tier);

    [Theory]
    [InlineData(999, 0)]
    [InlineData(1000, 1)]
    public void RepMachine_LifetimeReps(int reps, int tier) =>
        Assert.Equal(tier, Badges.One("rep-machine", new BadgeInputs { Sets = [Badges.Working(Badges.D(0), 20, reps: reps)] }).Tier);

    [Fact] public void ExerciseExplorer_DifferentExercises()
    {
        var sets = Enumerable.Range(1, 10).Select(e => Badges.Working(Badges.D(e), 20, exercise: e)).ToArray();
        Assert.Equal(1, Badges.One("exercise-explorer", new BadgeInputs { Sets = sets }).Tier);
        Assert.Equal(0, Badges.One("exercise-explorer", new BadgeInputs { Sets = sets[..9] }).Tier);
    }

    private static BadgeSet[] Prs(params int[] days) =>
        [Badges.Working(Badges.D(-10), 10), .. days.Select((d, i) => Badges.Working(Badges.D(d), 20 + i))];

    [Fact] public void PrWeek_ThreePrsInOneWeek()
    {
        var p = Badges.One("pr-week", new BadgeInputs { Sets = Prs(0, 2, 4) });
        Assert.Equal(1, p.Tier);
        Assert.Equal(Badges.D(4), p.EarnedOn);
    }

    [Fact] public void PrWeek_ThreePrsAcrossTwoWeeks_DoNotCount() =>
        Assert.Equal(0, Badges.One("pr-week", new BadgeInputs { Sets = Prs(4, 6, 7) }).Tier);
}

public class BadgeBodyTests
{
    private static BadgeCalorieDay Hit(int day) => new(Badges.D(day), 2000, 2000);

    [Fact] public void FuelStreak_SevenDaysInARow()
    {
        var p = Badges.One("fuel-streak", new BadgeInputs { Calories = Enumerable.Range(0, 7).Select(Hit).ToArray() });
        Assert.Equal(1, p.Tier);
        Assert.Equal(Badges.D(6), p.EarnedOn);
    }

    [Fact] public void FuelStreak_AMissOrNoGoal_BreaksTheRun()
    {
        var days = Enumerable.Range(0, 6).Select(Hit)
            .Append(new BadgeCalorieDay(Badges.D(6), 2000, 3500))
            .Append(new BadgeCalorieDay(Badges.D(7), null, 2000))
            .Concat(Enumerable.Range(8, 6).Select(Hit)).ToArray();
        var p = Badges.One("fuel-streak", new BadgeInputs { Calories = days });
        Assert.Equal(0, p.Tier);
        Assert.Equal(6, p.Value);
    }

    [Fact] public void WeighInHabit_FourWeeksInARow()
    {
        var p = Badges.One("weigh-in-habit", new BadgeInputs
        {
            WeighIns = [new(Badges.D(3), 80), new(Badges.D(7), 80), new(Badges.D(22), 80), new(Badges.D(15), 80)],
        });
        Assert.Equal(1, p.Tier);
        Assert.Equal(Badges.D(22), p.EarnedOn);
    }

    [Fact] public void WeighInHabit_AWeekWithout_StartsOver() =>
        Assert.Equal(0, Badges.One("weigh-in-habit", new BadgeInputs
        {
            WeighIns = [new(Badges.D(0), 80), new(Badges.D(7), 80), new(Badges.D(21), 80), new(Badges.D(28), 80)],
        }).Tier);

    [Theory]
    [InlineData(29, 0)]
    [InlineData(30, 1)]
    public void CardioMinutes_LogsAndSessionsTogether(int sessionMinutes, int tier) =>
        Assert.Equal(tier, Badges.One("cardio-minutes", new BadgeInputs
        {
            Workouts = [new(Badges.D(0), CardioMinutes: 30)],
            Cardio = [new(Badges.D(1), sessionMinutes)],
        }).Tier);
}

public class BadgeSocialTests
{
    private const string Gym = "osm:node:42";

    [Theory]
    [InlineData(24, false)]
    [InlineData(25, true)]
    public void Regular_TwentyFiveAtTheSameGym(int count, bool earned) =>
        Assert.Equal(earned, Badges.One("regular", new BadgeInputs
        {
            Workouts = Enumerable.Range(0, count).Select(i => new BadgeWorkout(Badges.D(i), Gym))
                .Append(new BadgeWorkout(Badges.D(100), "custom:1")).ToArray(),
        }).Earned);

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void Explorer_FiveGyms(int gyms, bool earned) =>
        Assert.Equal(earned, Badges.One("explorer", new BadgeInputs
        {
            Workouts = Enumerable.Range(0, gyms).Select(i => new BadgeWorkout(Badges.D(i), $"osm:way:{i}"))
                .Append(new BadgeWorkout(Badges.D(50), "osm:way:0")).ToArray(),
        }).Earned);

    [Fact] public void WeGoJim_SameOsmGymSameDay()
    {
        var p = Badges.One("we-go-jim", new BadgeInputs
        {
            Workouts = [new(Badges.D(0), Gym), new(Badges.D(1), Gym), new(Badges.D(2), "custom:3")],
            FriendCheckIns = [new(Badges.D(0), Gym), new(Badges.D(0), Gym), new(Badges.D(1), "osm:node:7"), new(Badges.D(2), "custom:3")],
        });
        Assert.Equal(1, p.Tier);
        Assert.Equal(1, p.Value);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Recruiter_FromTheServerCount(int invites, bool earned) =>
        Assert.Equal(earned, Badges.One("recruiter", new BadgeInputs { AcceptedInvites = invites }).Earned);

    [Fact] public void TopOfTheBoard_FinishedWeeksFirst()
    {
        var inputs = new BadgeInputs { WeekResults = [new(Badges.D(0), true), new(Badges.D(7), false), new(Badges.D(14), true)] };
        Assert.Equal(2, Badges.One("top-of-the-board", inputs).Value);
        Assert.Equal(1, Badges.One("top-of-the-board", inputs, Badges.D(19)).Value);   // the third week isn't over
    }

    [Fact] public void Monthly_TheMonthsWorkouts()
    {
        var dec = new DateOnly(2026, 12, 1);
        var twelve = Enumerable.Range(0, 12).Select(i => new BadgeWorkout(dec.AddDays(i * 2))).ToArray();
        var all = Badges.Eval(new BadgeInputs { Workouts = twelve }, new DateOnly(2027, 1, 10));
        Assert.True(all["monthly-2026-12"].Earned);
        Assert.Equal(dec.AddDays(22), all["monthly-2026-12"].EarnedOn);
        Assert.False(all["monthly-2027-01"].Earned);
        Assert.Equal(16, all["monthly-2027-01"].Target);
        Assert.DoesNotContain("monthly-2026-11", all.Keys);
        Assert.DoesNotContain("monthly-2027-02", all.Keys);
        Assert.False(Badges.One("monthly-2026-12", new BadgeInputs { Workouts = twelve[..11] }).Earned);
    }
}

public class BadgeHiddenTests
{
    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    public void IllBeBack_FourteenDaysOff(int gap, bool earned) =>
        Assert.Equal(earned, Badges.One("ill-be-back", new BadgeInputs { Workouts = Badges.Workouts(Badges.D(0), Badges.D(gap)) }).Earned);

    private static BadgeInputs Live(int hour, int minute, double hours = 1, bool autoStopped = false)
    {
        var start = Badges.D(0).ToDateTime(new TimeOnly(hour, minute));
        return new BadgeInputs { LiveWorkouts = [new(Badges.D(0), start, start.AddHours(hours), autoStopped)] };
    }

    [Theory]
    [InlineData(22, 59, false, false)]
    [InlineData(23, 0, true, false)]
    [InlineData(3, 59, true, false)]
    [InlineData(4, 0, false, true)]
    [InlineData(5, 59, false, true)]
    [InlineData(6, 0, false, false)]
    public void NightOwl_EyeOfTheTiger_StartTimes(int hour, int minute, bool owl, bool tiger)
    {
        var all = Badges.Eval(Live(hour, minute));
        Assert.Equal(owl, all["night-owl"].Earned);
        Assert.Equal(tiger, all["eye-of-the-tiger"].Earned);
    }

    [Theory]
    [InlineData(1.99, false, false)]
    [InlineData(2.0, false, true)]
    [InlineData(3.0, true, false)]
    public void MarathonSession_TwoHoursEndedByTheUser(double hours, bool autoStopped, bool earned) =>
        Assert.Equal(earned, Badges.One("marathon-session", Live(10, 0, hours, autoStopped)).Earned);

    [Fact] public void DoubleUp_StrengthAndCardioTheSameDay()
    {
        Assert.True(Badges.One("double-up", new BadgeInputs
        {
            Sets = [Badges.Working(Badges.D(0), 40)], Cardio = [new(Badges.D(0), 0)],
        }).Earned);
        Assert.False(Badges.One("double-up", new BadgeInputs
        {
            Sets = [Badges.Working(Badges.D(0), 40)], Cardio = [new(Badges.D(1), 20)],
        }).Earned);
    }

    [Theory]
    [InlineData(99.5, false, false)]
    [InlineData(100.0, true, false)]
    [InlineData(149.9, true, false)]
    [InlineData(150.0, true, true)]
    public void TripleDigits_LightWeightBaby(double kg, bool triple, bool light)
    {
        var all = Badges.Eval(new BadgeInputs { Sets = [Badges.Working(Badges.D(0), kg)] });
        Assert.Equal(triple, all["triple-digits"].Earned);
        Assert.Equal(light, all["light-weight-baby"].Earned);
    }

    [Fact] public void FreshStart_OnlyJanuaryFirst_FirstOfTheMonth_TheOthers()
    {
        var all = Badges.Eval(new BadgeInputs
        {
            Workouts = Badges.Workouts(new DateOnly(2027, 1, 1), new DateOnly(2027, 2, 1), new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 2)),
        });
        Assert.True(all["fresh-start"].Earned);
        Assert.Equal(2, all["first-of-month"].Value);
        Assert.Equal(1, all["first-of-month"].Tier);
        Assert.Equal(3, all["first-of-month"].Target);
        Assert.False(Badges.One("fresh-start", new BadgeInputs { Workouts = Badges.Workouts(new DateOnly(2027, 2, 1)) }).Earned);
    }

    [Fact] public void BirthdayGains_LeapDayBirthday_FallsOnThe28th()
    {
        var born = new DateOnly(2004, 2, 29);
        Assert.True(Badges.One("birthday-gains", new BadgeInputs { BirthDate = born, Workouts = Badges.Workouts(new DateOnly(2027, 2, 28)) }).Earned);
        Assert.False(Badges.One("birthday-gains", new BadgeInputs { BirthDate = born, Workouts = Badges.Workouts(new DateOnly(2028, 2, 28)) }).Earned);
        Assert.True(Badges.One("birthday-gains", new BadgeInputs { BirthDate = born, Workouts = Badges.Workouts(new DateOnly(2028, 2, 29)) }).Earned);
        Assert.False(Badges.One("birthday-gains", new BadgeInputs { Workouts = Badges.Workouts(new DateOnly(2028, 2, 29)) }).Earned);
    }

    [Fact] public void LeapDay_ChristmasGains()
    {
        var all = Badges.Eval(new BadgeInputs { Workouts = Badges.Workouts(new DateOnly(2028, 2, 29), new DateOnly(2027, 12, 25)) });
        Assert.True(all["leap-day"].Earned);
        Assert.True(all["christmas-gains"].Earned);
        Assert.False(Badges.One("christmas-gains", new BadgeInputs { Workouts = Badges.Workouts(new DateOnly(2027, 12, 24)) }).Earned);
    }

    [Fact] public void FullHouse_TenFullPlans()
    {
        var full = Enumerable.Range(0, 9).Select(i => new BadgeWorkout(Badges.D(i), FilledExercises: 5, TotalExercises: 5)).ToList();
        var nine = new BadgeInputs { Workouts = [.. full, new(Badges.D(20), FilledExercises: 4, TotalExercises: 5)] };
        Assert.False(Badges.One("full-house", nine).Earned);
        Assert.Equal(9, Badges.One("full-house", nine).Value);
        Assert.True(Badges.One("full-house", new BadgeInputs { Workouts = [.. full, new(Badges.D(20), FilledExercises: 5, TotalExercises: 5)] }).Earned);
    }

    [Theory]
    [InlineData(13, false)]
    [InlineData(14, true)]
    public void RestDayNeverHeardOfHer_DaysInARow(int days, bool earned) =>
        Assert.Equal(earned, Badges.One("rest-day-never", new BadgeInputs { Workouts = Badges.Days(0, days) }).Earned);

    [Fact] public void ThisIsFine_AGracedDay() =>
        Assert.Equal(Badges.D(1), Badges.One("this-is-fine", new BadgeInputs
        {
            TrainingDays = [new(Badges.D(0), DayOutcome.Done), new(Badges.D(1), DayOutcome.Graced)],
        }).EarnedOn);

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void TouchGrass_TrainWithinThreeDaysOfComingBack(int after, bool earned) =>
        Assert.Equal(earned, Badges.One("touch-grass", new BadgeInputs
        {
            Vacations = [new(Badges.D(0), Badges.D(7))],
            Workouts = Badges.Workouts(Badges.D(7 + after)),
        }).Earned);

    [Fact] public void TouchGrass_VacationNotOverYet() =>
        Assert.False(Badges.One("touch-grass", new BadgeInputs
        {
            Vacations = [new(Badges.D(0), Badges.D(7))], Workouts = Badges.Workouts(Badges.D(3)),
        }, Badges.D(5)).Earned);

    [Fact] public void Completion_EveryVisibleBadge()
    {
        var visible = BadgeCatalog.CompletionSet.Select(b => b.Id).ToList();
        Assert.True(Badges.One("completion", new BadgeInputs { PreviouslyEarned = visible.ToHashSet() }).Earned);
        var p = Badges.One("completion", new BadgeInputs { PreviouslyEarned = visible.Skip(1).ToHashSet() });
        Assert.False(p.Earned);
        Assert.Equal(visible.Count - 1, p.Value);
    }

    [Fact] public void NeverSkipLegDay_WeeksInARowWithLegs()
    {
        var legs = new[] { 0, 8, 16, 24 }.Select(d => Badges.Working(Badges.D(d), 60, legs: true)).ToArray();
        Assert.Equal(1, Badges.One("never-skip-leg-day", new BadgeInputs { Sets = legs }).Tier);
        var gap = legs.Take(3).Append(Badges.Working(Badges.D(24), 60)).ToArray();          // week 4 without legs
        Assert.Equal(0, Badges.One("never-skip-leg-day", new BadgeInputs { Sets = gap }).Tier);
    }
}

public class StreakColdDaysTests
{
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static DateOnly D(int i) => Mon.AddDays(i);
    private static readonly ScheduleChange[] Schedule = [new(Mon, "WRWRWRR")];
    private static readonly Dictionary<DateOnly, MissChoice> NoChoices = new();

    [Fact] public void RestDaysUnderTheRule_WithAStreak_AreCold()
    {
        var rule = new RestDayRule(Mon, new HashSet<DateOnly>());
        var cold = StreakCalculator.ColdDays([D(0), D(2)], Schedule, NoChoices, D(3), rule);
        Assert.Equal([D(1), D(3)], cold);
    }

    [Fact] public void AVerifiedRestDayWorkout_IsNotCold()
    {
        var rule = new RestDayRule(Mon, new HashSet<DateOnly> { D(1) });
        Assert.Empty(StreakCalculator.ColdDays([D(0), D(1)], Schedule, NoChoices, D(1), rule));
    }

    [Fact] public void VacationDays_WithAStreak_AreCold()
    {
        var rule = new RestDayRule(Mon.AddDays(100), new HashSet<DateOnly>(), new HashSet<DateOnly> { D(2), D(3) });
        Assert.Equal([D(2), D(3)], StreakCalculator.ColdDays([D(0)], Schedule, NoChoices, D(3), rule));
    }

    [Fact] public void BeforeTheRule_OrWithoutOne_NothingIsCold()
    {
        Assert.Empty(StreakCalculator.ColdDays([D(0), D(2)], Schedule, NoChoices, D(3), null));
        Assert.Empty(StreakCalculator.ColdDays([D(0), D(2)], Schedule, NoChoices, D(3), new RestDayRule(D(10), new HashSet<DateOnly>())));
    }
}
