using Microsoft.EntityFrameworkCore;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Infrastructure.Services;

public interface IDocumentNumberService
{
    Task<string> NextAsync(Guid branchId, DocumentSequenceType type, CancellationToken cancellationToken = default);
}

public sealed class DocumentNumberService : IDocumentNumberService
{
    private readonly ShiftClubDbContext _db;

    public DocumentNumberService(ShiftClubDbContext db)
    {
        _db = db;
    }

    public async Task<string> NextAsync(Guid branchId, DocumentSequenceType type, CancellationToken cancellationToken = default)
    {
        var year = DateTimeOffset.UtcNow.Year;
        var prefix = Prefix(type);
        var maxExisting = await GetMaxExistingValueAsync(branchId, type, year, prefix, cancellationToken);

        var seq = await _db.DocumentSequences
            .FirstOrDefaultAsync(s => s.BranchId == branchId && s.Type == type && s.Year == year, cancellationToken);

        if (seq is null)
        {
            seq = new DocumentSequence
            {
                BranchId = branchId,
                Type = type,
                Year = year,
                // Start from existing docs so a missing/reset sequence cannot reissue BK-2026-000001.
                LastValue = maxExisting
            };
            _db.DocumentSequences.Add(seq);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else if (seq.LastValue < maxExisting)
        {
            // Sequence lagged behind table (reset / missing increments) — catch up before +1.
            seq.LastValue = maxExisting;
            seq.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE document_sequences
            SET ""LastValue"" = ""LastValue"" + 1, ""UpdatedAt"" = {DateTimeOffset.UtcNow}
            WHERE ""Id"" = {seq.Id};", cancellationToken);

        await _db.Entry(seq).ReloadAsync(cancellationToken);

        return $"{prefix}-{year}-{seq.LastValue:D6}";
    }

    private static string Prefix(DocumentSequenceType type) => type switch
    {
        DocumentSequenceType.Receipt => "CHK",
        DocumentSequenceType.CashShift => "SHF",
        DocumentSequenceType.Refund => "REF",
        DocumentSequenceType.BarOrder => "BAR",
        DocumentSequenceType.Booking => "BK",
        _ => "DOC"
    };

    private async Task<int> GetMaxExistingValueAsync(
        Guid branchId,
        DocumentSequenceType type,
        int year,
        string prefix,
        CancellationToken cancellationToken)
    {
        var head = $"{prefix}-{year}-";
        List<string> numbers = type switch
        {
            DocumentSequenceType.Booking => await _db.Bookings.AsNoTracking()
                .Where(b => b.BranchId == branchId && b.Number.StartsWith(head))
                .Select(b => b.Number)
                .ToListAsync(cancellationToken),
            DocumentSequenceType.Receipt => await _db.Receipts.AsNoTracking()
                .Where(r => r.BranchId == branchId && r.Number.StartsWith(head))
                .Select(r => r.Number)
                .ToListAsync(cancellationToken),
            DocumentSequenceType.BarOrder => await _db.BarOrders.AsNoTracking()
                .Where(o => o.BranchId == branchId && o.Number.StartsWith(head))
                .Select(o => o.Number)
                .ToListAsync(cancellationToken),
            DocumentSequenceType.CashShift => await _db.CashShifts.AsNoTracking()
                .Where(s => s.BranchId == branchId && s.Number.StartsWith(head))
                .Select(s => s.Number)
                .ToListAsync(cancellationToken),
            DocumentSequenceType.Refund => await _db.Receipts.AsNoTracking()
                .Where(r => r.BranchId == branchId && r.Number.StartsWith(head))
                .Select(r => r.Number)
                .ToListAsync(cancellationToken),
            _ => []
        };

        var max = 0;
        foreach (var number in numbers)
        {
            var dash = number.LastIndexOf('-');
            if (dash < 0 || dash + 1 >= number.Length)
                continue;
            if (int.TryParse(number[(dash + 1)..], out var value) && value > max)
                max = value;
        }

        return max;
    }
}
