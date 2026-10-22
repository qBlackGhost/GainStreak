using GymApp.Core;

namespace GymApp.Tests;

public class BarWeightTests
{
    [Theory]
    [InlineData("Bench Press")]
    [InlineData("Incline Bench Press")]
    [InlineData("JM Bench Press")]
    [InlineData("Deadlift")]
    [InlineData("Romanian Deadlift")]
    [InlineData("Barbell Row")]
    [InlineData("T-Bar Row")]
    [InlineData("Overhead Press")]
    [InlineData("Barbell Curl")]
    [InlineData("Preacher Curl")]
    [InlineData("Reverse Curls")]
    [InlineData("Skull Crusher")]
    [InlineData("Squat")]
    [InlineData("Front Squat")]
    [InlineData("Good Morning")]
    [InlineData("Hip Thrust")]
    [InlineData("Smith Press")]
    [InlineData("Incline Smith Press")]
    [InlineData("Smith Machine Squat")]
    [InlineData("barbell lunge")]
    public void UsesBar(string name) => Assert.True(BarWeight.UsesBar(name));

    [Theory]
    [InlineData("Dumbbell Bench Press")]
    [InlineData("Incline Dumbbell Press")]
    [InlineData("Dumbbell Curl")]
    [InlineData("Hammer Curl")]
    [InlineData("Preacher Hammer Curl")]
    [InlineData("Lying Leg Curl")]
    [InlineData("Seated Cable Row")]
    [InlineData("One-Arm Dumbbell Row")]
    [InlineData("Low Row")]
    [InlineData("Lat Pulldown")]
    [InlineData("Bulgarian Split Squat")]
    [InlineData("Hack Squat")]
    [InlineData("Leg Press")]
    [InlineData("Wrist Curls")]
    [InlineData("Forearm Cable Curl")]
    [InlineData("Walking Lunge")]
    [InlineData("Pull-Up")]
    [InlineData("Plank")]
    public void DoesNotUseBar(string name) => Assert.False(BarWeight.UsesBar(name));

    [Theory]
    [InlineData(60, 20.0, 140)]
    [InlineData(60, null, 120)]
    [InlineData(21.25, 20.0, 62.5)]
    [InlineData(0, 20.0, 20)]
    public void Total_IsBothSidesPlusTheBar(double side, double? bar, double total) =>
        Assert.Equal(total, BarWeight.Total(side, bar));

    [Theory]
    [InlineData(140, 20.0, 60)]
    [InlineData(120, null, 60)]
    [InlineData(62.5, 20.0, 21.25)]
    [InlineData(15, 20.0, 0)]   // lighter than the bar: nothing on the sides
    public void PerSide_IsTheInverse(double total, double? bar, double side) =>
        Assert.Equal(side, BarWeight.PerSide(total, bar));
}
