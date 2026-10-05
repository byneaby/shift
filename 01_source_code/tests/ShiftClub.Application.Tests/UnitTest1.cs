namespace ShiftClub.Application.Tests;

public class SmokeTests
{
    [Fact]
    public void Application_Assembly_Loads()
    {
        var type = typeof(ShiftClub.Application.DependencyInjection);
        Assert.NotNull(type.Assembly);
    }
}
