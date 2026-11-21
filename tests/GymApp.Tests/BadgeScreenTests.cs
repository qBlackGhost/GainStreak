using GymApp.App.ViewModels;
using GymApp.App.Views;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Tests;

/// <summary>Phase 25c: what the badge screens show.</summary>
public class BadgeScreenTests
{
    private static BadgeItemViewModel Item(string id, bool earned, int tier = 0, double value = 0, double? target = null) =>
        new(BadgeCatalog.Find(id)!, new BadgeProgress(id, earned, tier, earned ? new DateOnly(2027, 3, 1) : null, value, target), false);

    [Fact] public void AHiddenBadgeNotEarned_IsMasked()
    {
        var item = Item("night-owl", false);
        Assert.Equal("Hidden", item.Name);
        Assert.Equal(BadgeLook.Hidden, item.Look);
        Assert.Equal("Trained while the city slept.", item.Rule);
        Assert.Equal("?", item.Short);
        Assert.False(item.ShowsProgress);
    }

    [Fact] public void AnEarnedHiddenBadge_ShowsItself()
    {
        var item = Item("night-owl", true);
        Assert.Equal("Night Owl", item.Name);
        Assert.Equal(BadgeLook.Lime, item.Look);
        Assert.Equal("EARNED", item.Short);
    }

    [Fact] public void Tiers_PickTheMaterialAndTheLadder()
    {
        var item = Item("streak", true, 3, 128, 365);
        Assert.Equal(BadgeLook.Fire, item.Look);
        Assert.Equal("FIRE", item.Short);
        Assert.Equal("128 / 365 days", item.ProgressText);
        Assert.Equal([true, true, true, false], item.Tiers.Select(t => t.Reached));
        Assert.Equal("365 days", item.Tiers[3].Threshold);
    }

    [Fact] public void HeavyLifter_ShowsItsLevelsPicture()
    {
        var item = Item("heavy-lifter", true, 2, 61_000, 150_000);
        Assert.Equal("elephant", item.Glyph);
        Assert.Equal(["car", "elephant", "whale", "station"], item.Tiers.Select(t => t.Glyph));
        Assert.Equal("ELEPHANT", item.Short);
    }

    [Fact] public void FixedMaterials_IceAndLimited()
    {
        Assert.Equal(BadgeLook.Ice, Item("on-ice", true).Look);
        Assert.Equal(BadgeLook.Limited, Item("monthly-2026-12", true).Look);
        Assert.Equal(BadgeLook.Locked, Item("on-ice", false).Look);
    }

    [Fact] public void Lists_KeepThisMonthsChallenge_AndEarnedPastOnes()
    {
        var progress = new List<BadgeProgress>
        {
            new("monthly-2026-12", true, 0, new(2026, 12, 20), 12, null),
            new("monthly-2027-01", false, 0, null, 3, 16),
            new("monthly-2027-02", false, 0, null, 1, 12),
            new("streak", true, 1, null, 7, 30),
            new("night-owl", false, 0, null, 0, 1),
        };
        var ids = BadgeItems.From(progress, false, new DateOnly(2027, 2, 10)).Select(i => i.Id).ToList();
        Assert.Equal(["streak", "monthly-2026-12", "monthly-2027-02", "night-owl"], ids);
    }

    [Fact] public void Automatic_HighestTierThenNewest()
    {
        var earned = new[]
        {
            new EarnedBadge { BadgeId = "on-ice", Tier = 0, EarnedOn = new(2027, 3, 9) },
            new EarnedBadge { BadgeId = "century", Tier = 2, EarnedOn = new(2027, 2, 1) },
            new EarnedBadge { BadgeId = "streak", Tier = 2, EarnedOn = new(2027, 3, 1) },
        };
        Assert.Equal("streak", BadgeService.Automatic(earned)!.BadgeId);
        Assert.Null(BadgeService.Automatic([]));
    }
}
