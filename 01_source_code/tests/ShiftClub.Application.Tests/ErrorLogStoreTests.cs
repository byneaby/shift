using ShiftClub.Application.Diagnostics;
using Xunit;

namespace ShiftClub.Application.Tests;

public class ErrorLogStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SameFaultIsCountedInOneGroup()
    {
        var store = new ErrorLogStore();

        store.Record("error", "SessionService", "InvalidOperationException", "Сеанс 11 уже закрыт", null, Now);
        store.Record("error", "SessionService", "InvalidOperationException", "Сеанс 42 уже закрыт", null, Now.AddMinutes(1));

        var groups = store.Snapshot();

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Count);
        Assert.Equal(Now, groups[0].FirstAt);
        Assert.Equal(Now.AddMinutes(1), groups[0].LastAt);
    }

    [Fact]
    public void DifferentFaultsStaySeparate()
    {
        var store = new ErrorLogStore();

        store.Record("error", "SessionService", "InvalidOperationException", "Сеанс уже закрыт", null, Now);
        store.Record("error", "BarService", "DbUpdateException", "Товара нет на складе", null, Now);

        Assert.Equal(2, store.Snapshot().Count);
    }

    [Fact]
    public void PersonalDataNeverReachesTheStore()
    {
        var store = new ErrorLogStore();

        store.Record("error", "CustomerService", "Exception", "Клиент +7 701 234 56 78 не найден", null, Now);

        Assert.DoesNotContain("701", store.Snapshot()[0].Message);
    }

    [Fact]
    public void OldestGroupIsEvictedWhenFull()
    {
        var store = new ErrorLogStore();

        for (var i = 0; i < ErrorLogStore.MaxGroups; i++)
            store.Record("error", "Svc", $"Fault{i}", "сломалось", null, Now.AddSeconds(i));

        store.Record("error", "Svc", "BrandNewFault", "сломалось", null, Now.AddHours(1));

        var groups = store.Snapshot(ErrorLogStore.MaxGroups);

        Assert.Equal(ErrorLogStore.MaxGroups, groups.Count);
        Assert.Contains(groups, g => g.Kind == "BrandNewFault");
        Assert.DoesNotContain(groups, g => g.Kind == "Fault0");
    }

    [Fact]
    public void ReportedGroupsAreNotSentTwice()
    {
        var store = new ErrorLogStore();
        store.Record("error", "Svc", "Fault", "сломалось", null, Now);

        var pending = store.TakeUnreported();
        Assert.Single(pending);

        store.MarkReported(pending.Select(p => p.Fingerprint));

        Assert.Empty(store.TakeUnreported());
    }

    [Fact]
    public void CountSinceIgnoresOlderGroups()
    {
        var store = new ErrorLogStore();
        store.Record("error", "Svc", "Old", "сломалось", null, Now.AddDays(-3));
        store.Record("error", "Svc", "Fresh", "сломалось", null, Now);
        store.Record("error", "Svc", "Fresh", "сломалось", null, Now);

        Assert.Equal(2, store.CountSince(Now.AddHours(-1)));
    }

    [Fact]
    public void ClearEmptiesTheList()
    {
        var store = new ErrorLogStore();
        store.Record("error", "Svc", "Fault", "сломалось", null, Now);

        store.Clear();

        Assert.Empty(store.Snapshot());
    }
}
