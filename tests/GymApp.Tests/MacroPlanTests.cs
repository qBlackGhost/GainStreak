using GymApp.Core;

namespace GymApp.Tests;

public class MacroPlanTests
{
    [Theory]
    // man, 80 kg, 180 cm (BMI 24.7, no cap), Active
    [InlineData(GoalType.Lose, Sex.Male, 176, 200, 56, 2008)]
    [InlineData(GoalType.Maintain, Sex.Male, 144, 320, 72, 2504)]
    [InlineData(GoalType.Gain, Sex.Male, 144, 400, 80, 2896)]
    public void AMan_GetsTheTablesGramsPerKg_AndTheCaloriesTheyAddUpTo(GoalType goal, Sex sex, int p, int c, int f, int kcal)
    {
        var r = MacroPlan.Recommend(goal, sex, 80, 180, ActivityLevel.Active);
        Assert.Equal((p, c, f, kcal), (r.Protein, r.Carbs, r.Fat, r.Kcal));
        Assert.Equal(FoodRules.KcalFromMacros(r.Protein, r.Carbs, r.Fat), r.Kcal);
        Assert.False(r.RaisedToMinimum);
    }

    [Fact] public void AWoman_GetsTheSameProteinPerKg_MoreFat_AndFewerCarbs()
    {
        var man = MacroPlan.Recommend(GoalType.Maintain, Sex.Male, 62, 168, ActivityLevel.Active);
        var woman = MacroPlan.Recommend(GoalType.Maintain, Sex.Female, 62, 168, ActivityLevel.Active);
        Assert.Equal(man.Protein, woman.Protein);
        Assert.True(woman.Fat > man.Fat);
        Assert.True(woman.Carbs < man.Carbs);
        Assert.Equal((112, 217, 62), (woman.Protein, woman.Carbs, woman.Fat));
    }

    [Fact] public void Protein_UsesTheWeightAtBmi27_WhenTheBodyIsHeavier()
    {
        // 120 kg at 175 cm: BMI 39; the weight at BMI 27 is 27 x 1.75² = 82.7 kg
        var r = MacroPlan.Recommend(GoalType.Lose, Sex.Male, 120, 175, ActivityLevel.Active);
        Assert.Equal(82.7, r.ProteinWeightKg, 1);
        Assert.Equal(182, r.Protein);          // 2.2 x 82.7
        Assert.Equal(84, r.Fat);               // fat and carbs still use the whole weight
        Assert.Equal(300, r.Carbs);

        Assert.Equal(70, MacroPlan.ProteinWeight(70, 175));   // under the cap: the weight itself
        Assert.Equal(70, MacroPlan.ProteinWeight(70, null));  // no height: no cap
    }

    [Fact] public void Carbs_MoveHalfAGramPerKgPerActivityStep()
    {
        var less = MacroPlan.Recommend(GoalType.Maintain, Sex.Male, 80, 180, ActivityLevel.LessActive);
        var active = MacroPlan.Recommend(GoalType.Maintain, Sex.Male, 80, 180, ActivityLevel.Active);
        var very = MacroPlan.Recommend(GoalType.Maintain, Sex.Male, 80, 180, ActivityLevel.VeryActive);
        Assert.Equal((280, 320, 360), (less.Carbs, active.Carbs, very.Carbs));
        Assert.Equal(3.5, less.CarbsPerKg);
        Assert.Equal(active.Protein, very.Protein);
        Assert.Equal(active.Fat, less.Fat);
    }

    [Fact] public void UnderTheSafeMinimum_CarbsGoUpUntilItIsReached()
    {
        // a small woman losing: 45 kg, 155 cm, Less active
        var plain = MacroPlan.Recommend(GoalType.Lose, Sex.Female, 45, 155, ActivityLevel.LessActive);
        Assert.True(plain.Kcal < CalorieGoals.FemaleFloor);

        var safe = MacroPlan.Recommend(GoalType.Lose, Sex.Female, 45, 155, ActivityLevel.LessActive, CalorieGoals.FemaleFloor);
        Assert.True(safe.RaisedToMinimum);
        Assert.InRange(safe.Kcal, CalorieGoals.FemaleFloor, CalorieGoals.FemaleFloor + 3);
        Assert.Equal((plain.Protein, plain.Fat), (safe.Protein, safe.Fat));
        Assert.True(safe.Carbs > plain.Carbs);
    }

    [Fact] public void TheTrendWeight_AveragesTheLast14Days_OrTakesTheLatestWeighIn()
    {
        var today = new DateOnly(2026, 11, 22);
        Assert.Null(MacroPlan.TrendWeight([], today));

        (DateOnly, double)[] logs = [(today, 80.6), (today.AddDays(-2), 79.2), (today.AddDays(-13), 80.0), (today.AddDays(-14), 90)];
        Assert.Equal(79.9, MacroPlan.TrendWeight(logs, today));   // 90 is 15 days old: left out

        Assert.Equal(83, MacroPlan.TrendWeight([(today.AddDays(-40), 83.0), (today.AddDays(-60), 85.0)], today));
        Assert.Equal(80.0, MacroPlan.TrendWeight([(today.AddDays(1), 70.0), (today, 80.0)], today));   // nothing from the future
    }

    [Theory]
    [InlineData(null, 80.0, true)]
    [InlineData(80.0, 80.9, false)]
    [InlineData(80.0, 79.1, false)]
    [InlineData(80.0, 81.0, true)]
    [InlineData(80.0, 78.8, true)]
    public void TheTargetsMove_OnlyWhenTheTrendIsAKiloAway(double? basis, double trend, bool moved) =>
        Assert.Equal(moved, MacroPlan.TrendMoved(basis, trend));
}
