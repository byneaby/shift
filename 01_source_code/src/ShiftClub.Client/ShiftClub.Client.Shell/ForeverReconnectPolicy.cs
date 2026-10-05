using Microsoft.AspNetCore.SignalR.Client;

namespace ShiftClub.Client.Shell;

/// <summary>SignalR: бесконечный реконнект с нарастающей паузой (потолок 15 с).</summary>
internal sealed class ForeverReconnectPolicy : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        var n = retryContext.PreviousRetryCount;
        var seconds = n switch
        {
            0 => 0,
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 5,
            5 => 8,
            6 => 10,
            _ => 15
        };
        return TimeSpan.FromSeconds(seconds);
    }
}
