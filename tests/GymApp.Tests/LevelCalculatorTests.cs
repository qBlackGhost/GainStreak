using GymApp.Core;

namespace GymApp.Tests;

public class LevelCalculatorTests
{
    [Theory]
    [InlineData(0, 1, "Beginner")]
    [InlineData(99, 1, "Beginner")]
    [InlineData(100, 2, "Novice")]
    [InlineData(299, 2, "Novice")]
    [InlineData(300, 3, "Regular")]
    [InlineData(699, 3, "Regular")]
    [InlineData(700, 4, "Dedicated")]
    [InlineData(1500, 5, "Athlete")]
    [InlineData(3000, 6, "Elite")]
    [InlineData(5999, 6, "Elite")]
    [InlineData(6000, 7, "Legend")]
    [InlineData(100000, 7, "Legend")]
    public void Boundaries(int points, int number, string name)
    {
        var l = LevelCalculator.For(points);
        Assert.Equal(number, l.Number);
        Assert.Equal(name, l.Name);
    }

    [Fact] public void Progress_WithinTheLevel()
    {
        var l = LevelCalculator.For(200);   // Novice: 100 to 300
        Assert.Equal(100, l.From);
        Assert.Equal(300, l.NextFrom);
        Assert.Equal("Regular", l.NextName);
        Assert.Equal(0.5, l.Progress);
        Assert.Equal(100, l.PointsToNext(200));

        Assert.Equal(0, LevelCalculator.For(0).Progress);
        Assert.Equal(0, LevelCalculator.For(300).Progress);
        Assert.Equal(0.99, LevelCalculator.For(99).Progress, 2);
    }

    [Fact] public void Legend_HasNoNextLevel()
    {
        var l = LevelCalculator.For(6000);
        Assert.Null(l.NextFrom);
        Assert.Null(l.NextName);
        Assert.Null(l.PointsToNext(6000));
        Assert.Equal(1, l.Progress);
    }

    [Fact] public void ALowerTotal_GivesALowerLevel()
    {
        Assert.Equal(3, LevelCalculator.For(320).Number);
        Assert.Equal(2, LevelCalculator.For(280).Number);
        Assert.Equal(1, LevelCalculator.For(-5).Number);   // never below Beginner
        Assert.Equal(0, LevelCalculator.For(-5).Progress);
    }

    [Fact] public void Table_StartsAtZero_AndRises()
    {
        Assert.Equal(0, LevelCalculator.Levels[0].From);
        Assert.True(LevelCalculator.Levels.Zip(LevelCalculator.Levels.Skip(1)).All(p => p.First.From < p.Second.From));
    }
}
