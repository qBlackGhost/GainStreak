using GymApp.Core;

namespace GymApp.Tests;

public class UserInitialsTests
{
    [Theory]
    [InlineData("cristi", "C")]           // one word: its initial
    [InlineData("Ptf2285e", "P")]
    [InlineData("andrei.m", "AM")]        // two words: one letter each
    [InlineData("vlad_t", "VT")]
    [InlineData("mary-jane", "MJ")]
    [InlineData("ion.pop.jr", "IP")]      // more words: the first two
    [InlineData("john_99", "J")]          // a word without letters doesn't count
    [InlineData("99_red", "R")]
    [InlineData("1cristi", "C")]          // digits before the first letter are skipped
    [InlineData("2fast.4you", "FY")]
    [InlineData("_lead.trail_", "LT")]    // separators at the ends or doubled
    [InlineData("a..b", "AB")]
    [InlineData("12345", "1")]            // no letters: the first character
    [InlineData("ștefan.ăla", "ȘĂ")]      // letters beyond ASCII
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void Of(string? username, string expected) => Assert.Equal(expected, UserInitials.Of(username));
}
