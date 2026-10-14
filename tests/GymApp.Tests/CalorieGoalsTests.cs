using GymApp.Core;

namespace GymApp.Tests;

public class CalorieGoalsTests
{
    [Theory]
    [InlineData(GoalType.Maintain, 2760, 2760)]
    [InlineData(GoalType.Lose, 2760, 2260)]     // 500 kcal, inside 15-20 percent
    [InlineData(GoalType.Lose, 4000, 3400)]     // 500 is under 15 percent, so 600
    [InlineData(GoalType.Gain, 2760, 3040)]     // +276, rounded to 10
    [InlineData(GoalType.Gain, 2000, 2250)]     // 10 percent is 200, raised to 250
    [InlineData(GoalType.Gain, 4000, 4300)]     // 10 percent is 400, capped at 300
    public void Suggest_Male(GoalType goal, int maintenance, int expected)
    {
        var s = CalorieGoals.Suggest(goal, maintenance, Sex.Male);
        Assert.Equal(expected, s.Kcal);
        Assert.Equal(maintenance, s.Maintenance);
        Assert.Null(s.Note);
    }

    [Fact] public void Suggest_Lose_SmallMaintenance_UsesTwentyPercent() =>
        Assert.Equal(1440, CalorieGoals.Suggest(GoalType.Lose, 1800, Sex.Female).Kcal);

    [Fact] public void Suggest_Lose_IsRaisedToTheFloor()
    {
        var s = CalorieGoals.Suggest(GoalType.Lose, 1400, Sex.Female);   // 1400 - 280 = 1120
        Assert.Equal(1200, s.Kcal);
        Assert.Contains("safe minimum", s.Note);
    }

    [Fact] public void Suggest_Lose_MaleFloorIsHigher() =>
        Assert.Equal(1500, CalorieGoals.Suggest(GoalType.Lose, 1800, Sex.Male).Kcal);

    [Fact] public void Suggest_Lose_MaintenanceUnderTheFloor_GivesNoDeficit()
    {
        var s = CalorieGoals.Suggest(GoalType.Lose, 1150, Sex.Female);
        Assert.Equal(1150, s.Kcal);
        Assert.Contains("no room for a deficit", s.Note);
    }

    [Theory]
    [InlineData(1200, null, null, true)]
    [InlineData(1190, null, null, false)]
    [InlineData(1200, Sex.Female, null, true)]
    [InlineData(1490, Sex.Male, null, false)]
    [InlineData(1500, Sex.Male, null, true)]
    [InlineData(2060, Sex.Male, 2760, false)]   // more than 25 percent under maintenance
    [InlineData(2070, Sex.Male, 2760, true)]
    [InlineData(6000, Sex.Male, 2760, true)]
    [InlineData(6010, Sex.Male, 2760, false)]
    public void CheckGoal(int kcal, Sex? sex, int? maintenance, bool ok) =>
        Assert.Equal(ok, CalorieGoals.CheckGoal(kcal, sex, maintenance) is null);

    [Theory]
    [InlineData(GoalType.Lose, 2000, 2000, true)]
    [InlineData(GoalType.Lose, 2000, 2001, false)]
    [InlineData(GoalType.Gain, 3000, 3000, true)]
    [InlineData(GoalType.Gain, 3000, 2999, false)]
    [InlineData(GoalType.Maintain, 2000, 2150, true)]
    [InlineData(GoalType.Maintain, 2000, 2151, false)]
    [InlineData(GoalType.Maintain, 2000, 1850, true)]
    [InlineData(GoalType.Maintain, 2000, 1849, false)]
    public void HitTarget(GoalType goal, int target, int consumed, bool hit) =>
        Assert.Equal(hit, CalorieGoals.HitTarget(goal, target, consumed));

    [Fact] public void MaintainRange_MatchesHitTarget() =>
        Assert.Equal((1850, 2150), CalorieGoals.MaintainRange(2000));

    [Fact] public void TargetRule_Text()
    {
        Assert.Equal("at or under 2,260 kcal", CalorieGoals.TargetRule(GoalType.Lose, 2260));
        Assert.Equal("between 1,850 and 2,150 kcal", CalorieGoals.TargetRule(GoalType.Maintain, 2000));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(15000, true)]
    [InlineData(15001, false)]
    public void CheckConsumed(int kcal, bool ok) =>
        Assert.Equal(ok, CalorieGoals.CheckConsumed(kcal) is null);
}
