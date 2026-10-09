using GymApp.Core;

namespace GymApp.Tests;

public class PasswordHasherTests
{
    [Fact] public void Verify_AcceptsCorrectPassword() =>
        Assert.True(PasswordHasher.Verify("hunter22!", PasswordHasher.Hash("hunter22!")));

    [Fact] public void Verify_RejectsWrongPassword() =>
        Assert.False(PasswordHasher.Verify("nope", PasswordHasher.Hash("hunter22!")));

    [Fact] public void Hash_IsSalted() =>
        Assert.NotEqual(PasswordHasher.Hash("same"), PasswordHasher.Hash("same"));
}