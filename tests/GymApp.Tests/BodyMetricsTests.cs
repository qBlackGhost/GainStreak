using GymApp.Core;

namespace GymApp.Tests;

public class BodyMetricsTests
{
    [Fact] public void Bmr_Male_MifflinStJeor() =>
        Assert.Equal(1780, BodyMetrics.Bmr(Sex.Male, 80, 180, 30), 6);      // 800 + 1125 - 150 + 5

    [Fact] public void Bmr_Female_MifflinStJeor() =>
        Assert.Equal(1345.25, BodyMetrics.Bmr(Sex.Female, 60, 165, 25), 6); // 600 + 1031.25 - 125 - 161

    [Theory]
    [InlineData(0, 1.2)]
    [InlineData(4_999, 1.2)]
    [InlineData(5_000, 1.375)]
    [InlineData(7_499, 1.375)]
    [InlineData(7_500, 1.55)]
    [InlineData(9_999, 1.55)]
    [InlineData(10_000, 1.725)]
    [InlineData(12_499, 1.725)]
    [InlineData(12_500, 1.9)]
    [InlineData(30_000, 1.9)]
    public void ActivityFactor_FollowsTheStepBands(int steps, double expected) =>
        Assert.Equal(expected, BodyMetrics.ActivityFactor(steps));

    [Fact] public void Maintenance_IsBmrTimesFactor_RoundedTo10()
    {
        Assert.Equal(2760, BodyMetrics.Maintenance(Sex.Male, 80, 180, 30, 8_000));    // 1780 x 1.55 = 2759
        Assert.Equal(1610, BodyMetrics.Maintenance(Sex.Female, 60, 165, 25, 3_000));  // 1345.25 x 1.2 = 1614.3
    }

    [Theory]
    [InlineData(1614.3, 1610)]
    [InlineData(1615, 1620)]
    [InlineData(1619.9, 1620)]
    public void RoundTo10_RoundsHalfAwayFromZero(double kcal, int expected) =>
        Assert.Equal(expected, BodyMetrics.RoundTo10(kcal));

    [Fact] public void Age_CountsFullYears()
    {
        var birth = new DateOnly(2000, 10, 15);
        Assert.Equal(25, BodyMetrics.AgeOn(birth, new DateOnly(2026, 10, 14)));   // day before the birthday
        Assert.Equal(26, BodyMetrics.AgeOn(birth, new DateOnly(2026, 10, 15)));   // on the birthday
    }

    [Fact] public void Age_LeapDayBirthday()
    {
        var birth = new DateOnly(2004, 2, 29);
        Assert.Equal(22, BodyMetrics.AgeOn(birth, new DateOnly(2027, 2, 28)));
        Assert.Equal(23, BodyMetrics.AgeOn(birth, new DateOnly(2027, 3, 1)));
    }

    [Theory]
    [InlineData(99.9, false)]
    [InlineData(100, true)]
    [InlineData(250, true)]
    [InlineData(250.1, false)]
    public void Height_Range(double cm, bool ok) => Assert.Equal(ok, BodyMetrics.CheckHeight(cm) is null);

    [Theory]
    [InlineData(29.9, false)]
    [InlineData(30, true)]
    [InlineData(350, true)]
    [InlineData(350.1, false)]
    public void Weight_Range(double kg, bool ok) => Assert.Equal(ok, BodyMetrics.CheckWeight(kg) is null);

    [Fact] public void BirthDate_Range()
    {
        var today = new DateOnly(2026, 10, 14);
        Assert.NotNull(BodyMetrics.CheckBirthDate(today.AddDays(1), today));         // future
        Assert.NotNull(BodyMetrics.CheckBirthDate(new DateOnly(2014, 1, 1), today)); // 12
        Assert.Null(BodyMetrics.CheckBirthDate(new DateOnly(2013, 10, 14), today));  // 13 today
        Assert.Null(BodyMetrics.CheckBirthDate(new DateOnly(1926, 10, 14), today));  // 100
        Assert.NotNull(BodyMetrics.CheckBirthDate(new DateOnly(1925, 10, 13), today)); // 101
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(60_000, true)]
    [InlineData(60_001, false)]
    public void Steps_Range(int steps, bool ok) => Assert.Equal(ok, BodyMetrics.CheckSteps(steps) is null);
}
