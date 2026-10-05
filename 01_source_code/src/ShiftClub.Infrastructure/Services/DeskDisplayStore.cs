using System.Security.Cryptography;
using ShiftClub.Shared.Contracts.Cases;

namespace ShiftClub.Infrastructure.Services;

/// <summary>Команда для экрана акции: показать кейс гостя (касса → другой ПК / монитор).</summary>
public sealed class DeskDisplayStore
{
    private readonly object _gate = new();
    private DeskDisplayCommandDto? _pending;
    private DeskCaseResultDto? _lastResult;

    public DeskDisplayCommandDto Show(Guid customerId, string displayName, string phone)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var cmd = new DeskDisplayCommandDto(
            Guid.NewGuid(),
            customerId,
            displayName,
            phone,
            DateTimeOffset.UtcNow,
            token);
        lock (_gate)
        {
            _pending = cmd;
            // Новый показ — сбрасываем прошлый результат, чтобы касса не взяла старый приз.
            if (_lastResult?.CustomerId == customerId)
                _lastResult = null;
        }
        return cmd;
    }

    public DeskDisplayCommandDto? Peek(TimeSpan maxAge)
    {
        lock (_gate)
        {
            if (_pending is null) return null;
            if (DateTimeOffset.UtcNow - _pending.CreatedAt > maxAge)
            {
                _pending = null;
                return null;
            }
            return _pending;
        }
    }

    public DeskDisplayCommandDto? TryGetByToken(string? token, TimeSpan maxAge)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var t = token.Trim();
        lock (_gate)
        {
            if (_pending is null) return null;
            if (DateTimeOffset.UtcNow - _pending.CreatedAt > maxAge)
            {
                _pending = null;
                return null;
            }
            if (!string.Equals(_pending.DisplayToken, t, StringComparison.Ordinal))
                return null;
            return _pending;
        }
    }

    public bool TryConsumeToken(string? token, out DeskDisplayCommandDto? cmd)
    {
        cmd = null;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var t = token.Trim();
        lock (_gate)
        {
            if (_pending is null) return false;
            if (!string.Equals(_pending.DisplayToken, t, StringComparison.Ordinal))
                return false;
            if (DateTimeOffset.UtcNow - _pending.CreatedAt > TimeSpan.FromMinutes(5))
            {
                _pending = null;
                return false;
            }
            cmd = _pending;
            _pending = null;
            return true;
        }
    }

    public void PublishResult(DeskDisplayCommandDto cmd, CaseOpenResultDto open)
    {
        var result = new DeskCaseResultDto(
            cmd.CommandId,
            cmd.CustomerId,
            cmd.DisplayName,
            open.RewardId,
            open.PrizeCode,
            open.PrizeName,
            open.PrizeType,
            open.PayloadJson,
            open.RewardStatus,
            open.ImageUrl,
            open.ApplyMessage,
            open.KeysRemaining,
            DateTimeOffset.UtcNow);
        lock (_gate)
        {
            _lastResult = result;
            if (_pending?.CommandId == cmd.CommandId)
                _pending = null;
        }
    }

    public DeskCaseResultDto? PeekResult(Guid? commandId, TimeSpan maxAge)
    {
        lock (_gate)
        {
            if (_lastResult is null) return null;
            if (DateTimeOffset.UtcNow - _lastResult.OpenedAt > maxAge)
            {
                _lastResult = null;
                return null;
            }
            if (commandId is Guid id && _lastResult.CommandId != id)
                return null;
            return _lastResult;
        }
    }

    public void Clear(Guid? commandId = null)
    {
        lock (_gate)
        {
            if (commandId is null || _pending?.CommandId == commandId)
                _pending = null;
        }
    }
}
