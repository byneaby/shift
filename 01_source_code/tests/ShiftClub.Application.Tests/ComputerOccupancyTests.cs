using ShiftClub.Application.Computers;
using ShiftClub.Domain.Entities;
using ShiftClub.Shared.Enums;
using Xunit;

namespace ShiftClub.Application.Tests;

public class ComputerOccupancyTests
{
    [Fact]
    public void Offline_without_session_is_free()
    {
        var pc = new Computer
        {
            IsApproved = true,
            Status = ComputerStatus.Offline,
            CurrentSessionId = null,
            CurrentSession = null
        };

        var (occ, detail) = ComputerOccupancy.Resolve(pc);

        Assert.Equal("Free", occ);
        Assert.Equal("Офлайн", detail);
    }

    [Fact]
    public void Offline_with_active_session_is_busy()
    {
        var session = new GamingSession
        {
            Status = SessionStatus.Active,
            PaymentMethod = PaymentMethod.Cash,
            GuestName = "Гость"
        };
        var pc = new Computer
        {
            IsApproved = true,
            Status = ComputerStatus.Offline,
            CurrentSessionId = Guid.NewGuid(),
            CurrentSession = session
        };

        var (occ, detail) = ComputerOccupancy.Resolve(pc);

        Assert.Equal("Busy", occ);
        Assert.Contains("Офлайн", detail ?? "");
    }

    [Fact]
    public void Online_free_has_no_offline_detail()
    {
        var pc = new Computer
        {
            IsApproved = true,
            Status = ComputerStatus.Free,
            CurrentSessionId = null
        };

        var (occ, detail) = ComputerOccupancy.Resolve(pc);

        Assert.Equal("Free", occ);
        Assert.Null(detail);
    }

    [Fact]
    public void InSession_stays_busy()
    {
        var session = new GamingSession
        {
            Status = SessionStatus.Active,
            CustomerId = Guid.NewGuid(),
            PaymentMethod = PaymentMethod.Balance
        };
        var pc = new Computer
        {
            IsApproved = true,
            Status = ComputerStatus.InSession,
            CurrentSessionId = Guid.NewGuid(),
            CurrentSession = session
        };

        var (occ, _) = ComputerOccupancy.Resolve(pc);

        Assert.Equal("Busy", occ);
    }
}

