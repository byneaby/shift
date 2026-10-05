namespace ShiftClub.Application.Abstractions;

public sealed record CustomerTelegramNotice(
    long TelegramUserId,
    string HtmlText,
    string? ReplyKeyboardHint = null);

public interface ICustomerTelegramNotifySink
{
    ValueTask PublishAsync(CustomerTelegramNotice notice, CancellationToken cancellationToken = default);
}
