using GymApp.Core;

namespace GymApp.Tests;

/// <summary>Phase 20c: what's read from Health, and what's left alone.</summary>
public class HealthImportTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);
    private static DateTime At(int daysAgo, int hour) => Today.AddDays(-daysAgo).ToDateTime(new TimeOnly(hour, 0));

    [Fact] public void WeighIns_LastOfTheDay_SkippingTypedDaysOwnSamplesAndOldOnes()
    {
        var samples = new[]
        {
            new HealthWeight(At(0, 7), 80.0), new HealthWeight(At(0, 21), 80.6),   // the day's last
            new HealthWeight(At(1, 7), 81.0),                                      // a typed weigh-in that day
            new HealthWeight(At(2, 8), 82.0, HealthPlan.WeightId(3, Today.AddDays(-2))),   // GainStreak's own
            new HealthWeight(At(3, 8), 83.0, "gainstreak-u9-weight-2026-11-07"),   // another account's
            new HealthWeight(At(4, 8), 84.04, "withings-1"),                        // a scale: imported
            new HealthWeight(At(91, 8), 85.0),                                     // too old
            new HealthWeight(At(5, 8), 12.0),                                      // not a weight the app takes
        };
        var import = HealthImport.WeighInsToImport(samples, new HashSet<DateOnly> { Today.AddDays(-1) }, Today.AddDays(-90), Today);
        Assert.Equal([(Today.AddDays(-4), 84.04), (Today, 80.6)], import);
    }

    [Fact] public void AverageSteps_NeedsTwoWeeksOfSteps()
    {
        Assert.Null(HealthImport.AverageSteps(Enumerable.Repeat(9000.0, 13).Concat(Enumerable.Repeat(0.0, 17))));
        Assert.Equal(9000, HealthImport.AverageSteps(Enumerable.Repeat(9000.0, 14).Concat(Enumerable.Repeat(0.0, 16))));
    }

    [Theory]
    [InlineData(7499, ActivityLevel.LessActive)]
    [InlineData(7500, ActivityLevel.Active)]
    [InlineData(12500, ActivityLevel.Active)]
    [InlineData(12501, ActivityLevel.VeryActive)]
    public void LevelForSteps_MatchesThePickersBands(double steps, ActivityLevel level) =>
        Assert.Equal(level, HealthImport.LevelForSteps(steps));

    [Fact] public void StepsSuggestion_OnlyANewLevel()
    {
        Assert.Equal(ActivityLevel.Active, HealthImport.StepsSuggestion(9000, ActivityLevel.LessActive, null));
        Assert.Null(HealthImport.StepsSuggestion(9000, ActivityLevel.Active, null));        // already that
        Assert.Null(HealthImport.StepsSuggestion(9000, null, ActivityLevel.Active));       // answered before
        Assert.Equal(ActivityLevel.VeryActive, HealthImport.StepsSuggestion(14000, ActivityLevel.Active, ActivityLevel.Active));
        Assert.Null(HealthImport.StepsSuggestion(null, null, null));
    }

    [Fact] public void Compare_FillsEmptyFields_AndOffersDifferences()
    {
        var health = new HealthProfile(182.04, new DateOnly(1994, 2, 3), Sex.Female);
        var (fill, offer) = HealthImport.Compare(health, null, null, null, Today);
        Assert.Equal(new HealthProfile(182.0, new DateOnly(1994, 2, 3), Sex.Female), fill);
        Assert.True(offer.IsEmpty);

        (fill, offer) = HealthImport.Compare(health, 175, new DateOnly(1994, 2, 3), Sex.Male, Today);
        Assert.Equal(new HealthProfile(null, null, null), fill);
        Assert.Equal(new HealthProfileOffer(182.0, null, Sex.Female), offer);
    }

    [Fact] public void Compare_IgnoresSmallHeightDifferences_AndValuesOutOfRange()
    {
        var (_, offer) = HealthImport.Compare(new HealthProfile(182.6, null, null), 182, null, null, Today);
        Assert.True(offer.IsEmpty);
        var (fill, _) = HealthImport.Compare(new HealthProfile(30, Today.AddYears(-5), null), null, null, null, Today);
        Assert.Equal(new HealthProfile(null, null, null), fill);
    }

    [Fact] public void OfferKey_NamesTheValues() =>
        Assert.Equal("182.0|1994-02-03|Female", HealthImport.OfferKey(new HealthProfileOffer(182, new DateOnly(1994, 2, 3), Sex.Female)));
}
