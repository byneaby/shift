using System.Threading.Channels;
using ShiftClub.Application.Abstractions;

namespace ShiftClub.Infrastructure.Telegram;

public sealed class CustomerTelegramNotifySink : ICustomerTelegramNotifySink
{
    private readonly Channel<CustomerTelegramNotice> _channel =
        Channel.CreateBounded<CustomerTelegramNotice>(new BoundedChannelOptions(2000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    public ChannelReader<CustomerTelegramNotice> Reader => _channel.Reader;

    public ValueTask PublishAsync(CustomerTelegramNotice notice, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(notice, cancellationToken);
}
