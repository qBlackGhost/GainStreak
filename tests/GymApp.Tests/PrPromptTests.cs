using GymApp.Core;

namespace GymApp.Tests;

public class PrPromptTests
{
    private static readonly PrEntry Pr = new(new DateOnly(2026, 10, 12), 60, 12, 84, PrEntryKind.Pr);

    [Fact] public void NoPrYet_SaysSo() =>
        Assert.Contains("No PR yet", PrPrompts.For(null, 60).Text);

    [Fact] public void NothingTyped_ShowsThePr()
    {
        var p = PrPrompts.For(Pr, null);
        Assert.Equal("PR: 60 kg × 12", p.Text);
        Assert.Equal(PrStatus.Info, p.Status);
    }

    [Fact] public void HeavierWeight_IsAlreadyANewPr() =>
        Assert.Equal(PrStatus.NewPr, PrPrompts.For(Pr, 62.5).Status);

    [Fact] public void LighterWeight_IsBelow() =>
        Assert.Equal(PrStatus.Below, PrPrompts.For(Pr, 55, 20).Status);

    [Fact] public void SameWeight_NoReps_TellsYouTheTarget() =>
        Assert.Contains("13+ reps", PrPrompts.For(Pr, 60).Text);

    [Fact] public void SameWeight_MoreReps_IsNewPr() =>
        Assert.Equal(PrStatus.NewPr, PrPrompts.For(Pr, 60, 13).Status);

    [Fact] public void SameWeight_SameReps_IsTie() =>
        Assert.Equal(PrStatus.Tie, PrPrompts.For(Pr, 60, 12).Status);

    [Fact] public void SameWeight_FewerReps_SaysHowManyMore()
    {
        var p = PrPrompts.For(Pr, 60, 10);
        Assert.Equal(PrStatus.Below, p.Status);
        Assert.Contains("3 more rep(s)", p.Text);
    }
}