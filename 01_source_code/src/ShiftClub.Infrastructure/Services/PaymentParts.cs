using ShiftClub.Shared.Contracts.Cash;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Infrastructure.Services;

/// <summary>Сборка частей оплаты для чека (в т.ч. смешанная нал+Kaspi).</summary>
public static class PaymentParts
{
    private static readonly HashSet<PaymentMethod> SplitMethods =
    [
        PaymentMethod.Cash,
        PaymentMethod.Card,
        PaymentMethod.KaspiQr,
        PaymentMethod.Transfer,
    ];

    public static IReadOnlyList<PaymentPartDto> Resolve(
        PaymentMethod method,
        decimal amount,
        IReadOnlyList<PaymentPartDto>? parts)
    {
        var total = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (total <= 0)
            return Array.Empty<PaymentPartDto>();

        if (method == PaymentMethod.Mixed)
        {
            if (parts is null || parts.Count == 0)
                throw new InvalidOperationException(
                    "Смешанная оплата: укажите суммы частей (например наличные + Kaspi).");

            var cleaned = new List<PaymentPartDto>(parts.Count);
            foreach (var p in parts)
            {
                var a = Math.Round(p.Amount, 2, MidpointRounding.AwayFromZero);
                if (a <= 0)
                    continue;
                if (!SplitMethods.Contains(p.Method))
                    throw new InvalidOperationException(
                        "В смешанной оплате допустимы только наличные, карта, Kaspi QR или перевод.");
                cleaned.Add(new PaymentPartDto(p.Method, a));
            }

            if (cleaned.Count < 2)
                throw new InvalidOperationException(
                    "Смешанная оплата: нужны минимум две части с суммой > 0.");

            var sum = cleaned.Sum(x => x.Amount);
            if (Math.Abs(sum - total) > 0.009m)
                throw new InvalidOperationException(
                    $"Сумма частей ({sum:0.##} ₸) не равна итогу ({total:0.##} ₸).");

            return cleaned;
        }

        if (parts is { Count: > 0 })
            throw new InvalidOperationException("Части оплаты указываются только для смешанной оплаты.");

        if (method is PaymentMethod.Free or PaymentMethod.Postpay or PaymentMethod.Balance)
            throw new InvalidOperationException("Этот способ оплаты не создаёт кассовые части чека.");

        return [new PaymentPartDto(method, total)];
    }
}
