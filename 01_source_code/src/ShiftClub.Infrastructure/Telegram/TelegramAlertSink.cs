using System.Threading.Channels;
using ShiftClub.Application.Abstractions;
using ShiftClub.Shared.Contracts.Settings;

namespace ShiftClub.Infrastructure.Telegram;

public sealed class TelegramAlertSink : ITelegramAlertSink
{
    private readonly Channel<StaffAlertMessage> _channel =
        Channel.CreateBounded<StaffAlertMessage>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    public ChannelReader<StaffAlertMessage> Reader => _channel.Reader;

    public ValueTask PublishAsync(StaffAlertMessage message, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(message, cancellationToken);
}
