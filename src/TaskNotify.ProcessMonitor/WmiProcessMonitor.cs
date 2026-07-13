using System.Management;
using System.Collections.Concurrent;
using System.Threading.Channels;
using TaskNotify.Core;

namespace TaskNotify.ProcessMonitor;

public sealed class WmiProcessMonitor
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);
    private readonly Action<Exception>? _reportFault;

    public WmiProcessMonitor(Action<Exception>? reportFault = null) => _reportFault = reportFault;

    public async Task RunAsync(
        Func<ProcessLifecycleEvent, CancellationToken, ValueTask> handleEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handleEvent);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(handleEvent, _reportFault, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _reportFault?.Invoke(exception);
                try
                {
                    await Task.Delay(RestartDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private static async Task RunSessionAsync(
        Func<ProcessLifecycleEvent, CancellationToken, ValueTask> handleEvent,
        Action<Exception>? reportFault,
        CancellationToken cancellationToken)
    {
        var events = Channel.CreateBounded<ProcessLifecycleEvent>(new BoundedChannelOptions(1024)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite
        });
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var activeProcesses = new ConcurrentDictionary<int, ProcessIdentity>();
        Exception? stoppedException = null;
        using var started = CreateWatcher("SELECT * FROM Win32_ProcessStartTrace");
        using var stopped = CreateWatcher("SELECT * FROM Win32_ProcessStopTrace");

        started.EventArrived += (_, args) => Publish(events.Writer, () =>
        {
            var processEvent = WmiProcessEventMapper.Started(args.NewEvent);
            activeProcesses[processEvent.Identity.ProcessId] = processEvent.Identity;
            return processEvent;
        }, reportFault);
        stopped.EventArrived += (_, args) => Publish(events.Writer, () =>
        {
            var processEvent = WmiProcessEventMapper.Stopped(args.NewEvent);
            activeProcesses.TryRemove(processEvent.ProcessId, out var identity);
            return processEvent with { Identity = identity };
        }, reportFault);
        StoppedEventHandler onStopped = (_, args) =>
        {
            stoppedException = new InvalidOperationException($"WMI 进程事件监听已停止：{args.Status}。");
            sessionCancellation.Cancel();
        };
        started.Stopped += onStopped;
        stopped.Stopped += onStopped;

        try
        {
            started.Start();
            stopped.Start();
            await foreach (var processEvent in events.Reader.ReadAllAsync(sessionCancellation.Token).ConfigureAwait(false))
            {
                var enrichedEvent = processEvent is ProcessStartedEvent startedEvent
                    ? WmiProcessMetadataReader.Enrich(startedEvent, reportFault)
                    : processEvent;
                await handleEvent(enrichedEvent, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppedException is not null)
        {
            throw stoppedException;
        }
        finally
        {
            events.Writer.TryComplete();
            started.Stop();
            stopped.Stop();
        }

        if (stoppedException is not null)
        {
            throw stoppedException;
        }
    }

    private static ManagementEventWatcher CreateWatcher(string query) => new(new WqlEventQuery(query));

    private static void Publish(
        ChannelWriter<ProcessLifecycleEvent> writer,
        Func<ProcessLifecycleEvent> createEvent,
        Action<Exception>? reportFault)
    {
        try
        {
            writer.TryWrite(createEvent());
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            reportFault?.Invoke(exception);
        }
    }
}

public static class WmiProcessEventMapper
{
    public static ProcessStartedEvent Started(ManagementBaseObject source) => new(
        new ProcessIdentity(ToInt32(source["ProcessID"]), Timestamp(source)),
        ToNullableInt32(source["ParentProcessID"]),
        source["ProcessName"]?.ToString() ?? "unknown",
        Timestamp(source));

    public static ProcessStoppedEvent Stopped(ManagementBaseObject source) => new(
        ToInt32(source["ProcessID"]),
        source["ProcessName"]?.ToString() ?? "unknown",
        Timestamp(source));

    private static int ToInt32(object? value) => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);

    private static int? ToNullableInt32(object? value) => value is null ? null : ToInt32(value);

    private static DateTimeOffset Timestamp(ManagementBaseObject source) => source["TIME_CREATED"] is ulong timestamp
        ? DateTimeOffset.FromFileTime((long)timestamp)
        : DateTimeOffset.UtcNow;
}

internal static class WmiProcessMetadataReader
{
    public static ProcessStartedEvent Enrich(ProcessStartedEvent started, Action<Exception>? reportFault)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ExecutablePath, CommandLine FROM Win32_Process WHERE ProcessId = {started.Identity.ProcessId}");
            using var results = searcher.Get();
            var process = results.Cast<ManagementObject>().FirstOrDefault();
            var parentName = NameForProcess(started.ParentProcessId);
            return started with
            {
                ExecutablePath = process?["ExecutablePath"]?.ToString(),
                CommandLine = process?["CommandLine"]?.ToString(),
                ParentProcessName = parentName
            };
        }
        catch (ManagementException exception)
        {
            reportFault?.Invoke(exception);
            return started;
        }
        catch (UnauthorizedAccessException exception)
        {
            reportFault?.Invoke(exception);
            return started;
        }
    }

    private static string? NameForProcess(int? processId)
    {
        if (processId is null) return null;

        using var searcher = new ManagementObjectSearcher(
            $"SELECT Name FROM Win32_Process WHERE ProcessId = {processId.Value}");
        using var results = searcher.Get();
        return results.Cast<ManagementObject>().FirstOrDefault()?["Name"]?.ToString();
    }
}
