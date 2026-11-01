using GymApp.Core;

namespace GymApp.Tests;

public class UsernameFilterTests
{
    [Theory]
    [InlineData("fuck")]
    [InlineData("BIGFUCK99")]
    [InlineData("f_u.c-k")]
    [InlineData("fuuuuck")]
    [InlineData("sh1t")]
    [InlineData("b1tch")]
    [InlineData("rape")]
    [InlineData("my.rape")]
    [InlineData("r-a-p-e")]
    [InlineData("n4zi")]
    [InlineData("pula")]
    [InlineData("big_pula")]
    [InlineData("admin")]
    [InlineData("support")]
    [InlineData("GainStreak_team")]
    public void Refuses(string username) => Assert.False(UsernameFilter.IsAllowed(username));

    // Real names and words with a bad word inside them
    [Theory]
    [InlineData("cristi")]
    [InlineData("alice")]
    [InlineData("scunthorpe")]
    [InlineData("hancock")]
    [InlineData("grape")]
    [InlineData("popular")]
    [InlineData("torpedo")]
    [InlineData("bass_player")]
    [InlineData("dickson")]
    [InlineData("nazim")]
    [InlineData("niger")]
    [InlineData("modern.lifter")]
    [InlineData("user123")]
    public void Allows(string username) => Assert.True(UsernameFilter.IsAllowed(username));

    [Fact] public void CheckUsername_SaysToPickAnother() =>
        Assert.Equal("That username isn't allowed. Pick another one.", Credentials.CheckUsername("fuck"));
}
