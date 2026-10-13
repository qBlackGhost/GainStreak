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
    
        private static readonly DateOnly Today = new(2026, 10, 20);
    private static readonly DateOnly Earlier = new(2026, 10, 13);
    private static SetEntry Saved(DateOnly day, double kg, int reps) => new(day, 1, SetKind.Working, reps, kg);
    private static DraftSet Work(double? kg, int? reps) => new(SetKind.Working, kg, reps);
    private static DraftSet Warm(double? kg, int? reps) => new(SetKind.Warmup, kg, reps);

    private static PrPrompt? Row(IReadOnlyList<DraftSet> rows, int index, SetEntry[] saved,
                                 PrAdjustment[]? adjustments = null) =>
        PrPrompts.ForRow(rows, index, saved, adjustments ?? Array.Empty<PrAdjustment>(), Today);

    [Fact] public void Row_Warmup_HasNoPrompt() =>
        Assert.Null(Row([Warm(40, 10)], 0, [Saved(Earlier, 60, 12)]));

    [Fact] public void Row_NoHistory_SaysNoPrYet() =>
        Assert.Contains("No PR yet", Row([Work(60, null)], 0, [])!.Text);

    [Fact] public void Row_ComparesToTheBestBeforeToday() =>
        Assert.Equal(PrStatus.NewPr, Row([Work(62.5, null)], 0, [Saved(Earlier, 60, 12)])!.Status);

    [Fact] public void Row_IgnoresSetsAlreadySavedToday()
    {
        // the draft replaces what was saved today, so a saved 100 x 5 must not count
        var p = Row([Work(60, 13)], 0, [Saved(Earlier, 60, 12), Saved(Today, 100, 5)])!;
        Assert.Equal(PrStatus.NewPr, p.Status);
    }

    [Fact] public void Row_RowsAboveCountAsTodaysProgress()
    {
        var saved = new[] { Saved(Earlier, 60, 10) };
        Assert.Equal(PrStatus.Tie, Row([Work(65, 5), Work(65, 5)], 1, saved)!.Status);
        Assert.Equal(PrStatus.Below, Row([Work(65, 5), Work(60, 10)], 1, saved)!.Status);
    }

    [Fact] public void Row_WarmupsAboveDoNotCount() =>
        Assert.Equal(PrStatus.Below, Row([Warm(100, 5), Work(60, 10)], 1, [Saved(Earlier, 60, 12)])!.Status);

    [Fact] public void Row_IncompleteRowsAbove_AreIgnored() =>
        Assert.Equal(PrStatus.Below, Row([Work(null, 5), Work(60, 10)], 1, [Saved(Earlier, 60, 12)])!.Status);

    [Fact] public void Row_AnAdjustmentMadeToday_IsTheBaseline()
    {
        var saved = new[] { Saved(Earlier, 60, 12) };
        var adj = new[] { new PrAdjustment(Today, 55, 10) };
        Assert.Equal(PrStatus.NewPr, Row([Work(57.5, null)], 0, saved, adj)!.Status);
        Assert.Contains("11+ reps", Row([Work(55, null)], 0, saved, adj)!.Text);
    }

    [Fact] public void Row_ZeroReps_CountsAsNotTypedYet() =>
        Assert.Contains("13+ reps", Row([Work(60, 0)], 0, [Saved(Earlier, 60, 12)])!.Text);
}