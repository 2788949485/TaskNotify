using TaskNotify.Core.Detection;

namespace TaskNotify.Infrastructure.SystemInfo;

/// <summary>
/// Default <see cref="ISystemBootProvider"/>. Uses <see cref="Environment.TickCount64"/>
/// (ms since system boot) to derive the boot time. Accuracy is ~16ms which is more than
/// enough for the 5-minute resident-detection window.
/// </summary>
public sealed class SystemBootProvider : ISystemBootProvider
{
    public DateTimeOffset? GetBootTimeUtc()
    {
        try
        {
            return DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
        }
        catch
        {
            return null;
        }
    }
}
