using ShiftClub.Domain.Entities;

namespace ShiftClub.Domain.Tests;

public class EntityTests
{
    [Fact]
    public void Entity_Has_Id_And_CreatedAt()
    {
        var branch = new Branch
        {
            Name = "SHIFT Club Almaty",
            Code = "ALA-01"
        };

        Assert.NotEqual(Guid.Empty, branch.Id);
        Assert.True(branch.CreatedAt <= DateTimeOffset.UtcNow);
        Assert.Equal("Asia/Almaty", branch.TimeZoneId);
        Assert.Equal("KZT", branch.CurrencyCode);
    }
}
