using ShiftClub.Infrastructure.Identity;

namespace ShiftClub.Application.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_And_Verify_Succeeds()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var hash = hasher.Hash("Owner123!");
        Assert.True(hasher.Verify("Owner123!", hash));
        Assert.False(hasher.Verify("wrong", hash));
    }
}
