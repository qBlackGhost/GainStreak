using GymApp.Core;

namespace GymApp.Tests;

public class AvatarTests
{
    [Theory]
    [InlineData("violet", "violet", null)]
    [InlineData("teal:flame", "teal", "flame")]
    public void Parse_ReadsKnownChoices(string text, string color, string? icon)
    {
        var choice = AvatarChoice.Parse(text);
        Assert.Equal(new AvatarChoice(color, icon), choice);
        Assert.Equal(text, choice!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("beige")]
    [InlineData("violet:unicorn")]
    [InlineData("violet:flame:extra")]
    public void Parse_GivesNullForAnythingElse(string? text) => Assert.Null(AvatarChoice.Parse(text));

    [Fact] public void EveryChoice_FitsTheColumn() =>
        Assert.All(AvatarChoice.Colors.SelectMany(c => AvatarChoice.Icons.Select(i => new AvatarChoice(c, i).ToString())),
            s => Assert.True(s.Length <= AvatarChoice.MaxLength));

    [Theory]
    [InlineData(1, AvatarRing.Starter)]
    [InlineData(2, AvatarRing.Starter)]
    [InlineData(3, AvatarRing.Silver)]
    [InlineData(4, AvatarRing.Silver)]
    [InlineData(5, AvatarRing.Lime)]
    [InlineData(6, AvatarRing.Lime)]
    [InlineData(7, AvatarRing.Gold)]
    public void TheRing_FollowsTheLevel(int level, AvatarRing ring) => Assert.Equal(ring, AvatarRings.For(level));
}
