using GymApp.Core;

namespace GymApp.Tests;

public class BadgeDisplayTests
{
    private static BadgeDefinition B(string id) => BadgeCatalog.Find(id)!;

    [Theory]
    [InlineData(61_000.0, false, "61 t")]
    [InlineData(61_000.0, true, "67 tons")]
    [InlineData(1_500.0, false, "1.5 t")]
    [InlineData(420_000.0, false, "420 t")]
    public void HeavyLifter_TonnesOrTons(double kg, bool imperial, string text) =>
        Assert.Equal(text, BadgeDisplay.Amount("heavy-lifter", kg, imperial));

    [Fact] public void Bodyweight_Multiples() =>
        Assert.Equal(["1×", "1.5×", "1.25×"], new[] { 1.0, 1.5, 1.25 }.Select(v => BadgeDisplay.Amount("bodyweight-club", v, false)));

    [Fact] public void Counts_WithUnitsAndThousands()
    {
        Assert.Equal("58,210 reps", BadgeDisplay.Amount("rep-machine", 58_210, false));
        Assert.Equal("940 min", BadgeDisplay.Amount("cardio-minutes", 940, true));
        Assert.Equal("12 workouts", BadgeDisplay.Amount("monthly-2026-12", 12, false));
    }

    [Fact] public void Progress_UnitOnceForCounts_BothSidesForWeights()
    {
        Assert.Equal("9 / 14 days", BadgeDisplay.Progress(B("fuel-streak"), new("fuel-streak", true, 1, null, 9, 14), false));
        Assert.Equal("61 t / 150 t", BadgeDisplay.Progress(B("heavy-lifter"), new("heavy-lifter", true, 2, null, 61_000, 150_000), false));
        Assert.Equal("1.3× / 1.5×", BadgeDisplay.Progress(B("bodyweight-club"), new("bodyweight-club", true, 1, null, 1.3, 1.5), false));
        Assert.Null(BadgeDisplay.Progress(B("streak"), new("streak", true, 4, null, 400, null), false));
    }

    [Fact] public void One_IsSingular()
    {
        Assert.Equal("0 / 1 week", BadgeDisplay.Progress(B("perfect-week"), new("perfect-week", false, 0, null, 0, 1), false));
        Assert.Equal("1 / 3 weeks", BadgeDisplay.Progress(B("pr-week"), new("pr-week", true, 1, null, 1, 3), false));
        Assert.Equal("1 PR", BadgeDisplay.Amount("pr-breaker", 1, false));
        Assert.Equal("1 min", BadgeDisplay.Amount("cardio-minutes", 1, false));
    }

    [Fact] public void Progress_NeverPastTheTarget() =>
        Assert.Equal(1, BadgeDisplay.Ratio(new("x", true, 0, null, 30, 14)));

    [Fact] public void Status_TierNamesAndLocked()
    {
        Assert.Equal("Fire · 100 days", BadgeDisplay.Status(B("streak"), true, 3, false));
        Assert.Equal("Elephant", BadgeDisplay.Status(B("heavy-lifter"), true, 2, false));
        Assert.Equal("Earned", BadgeDisplay.Status(B("on-ice"), true, 0, false));
        Assert.Equal("Locked", BadgeDisplay.Status(B("streak"), false, 0, false));
    }
}
