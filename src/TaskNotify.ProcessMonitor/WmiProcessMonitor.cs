using System.Management;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading.Channels;
using TaskNotify.Core.Events;

namespace TaskNotify.ProcessMonitor;

public sealed class WmiProcessMonitor
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);
    private readonly Action<Exception>? _reportSessionFault;
    private readonly Action? _reportSessionStarted;

    public WmiProcessMonitor(Action<Exception>? reportSessionFault = null, Action? reportSessionStarted = null)
    {
        _reportSessionFault = reportSessionFault;
        _reportSessionStarted = reportSessionStarted;
    }

    public async Task RunAsync(
        Func<ProcessLifecycleEvent, CancellationToken, ValueTask> handleEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handleEvent);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(handleEvent, _reportSessionStarted, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _reportSessionFault?.Invoke(exception);
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
        Action? reportSessionStarted,
        CancellationToken cancellationToken)
    {
        var events = Channel.CreateBounded<ProcessLifecycleEvent>(new BoundedChannelOptions(1024)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite
        });
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var activeProcesses = new ConcurrentDictionary<int, ProcessIdentity>();
        var observedStarts = new ConcurrentDictionary<int, byte>();
        var observedStops = new ConcurrentDictionary<int, byte>();
        var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeProcessId = 0;
        Exception? stoppedException = null;
        using var started = CreateWatcher("SELECT * FROM Win32_ProcessStartTrace");
        using var stopped = CreateWatcher("SELECT * FROM Win32_ProcessStopTrace");

        started.EventArrived += (_, args) =>
        {
            var processEvent = WmiProcessEventMapper.Started(args.NewEvent);
            observedStarts.TryAdd(processEvent.Identity.ProcessId, 0);
            if (processEvent.Identity.ProcessId == Volatile.Read(ref probeProcessId)) probeStarted.TrySetResult();
            activeProcesses[processEvent.Identity.ProcessId] = processEvent.Identity;
            _ = events.Writer.TryWrite(processEvent);
        };
        stopped.EventArrived += (_, args) =>
        {
            var processEvent = WmiProcessEventMapper.Stopped(args.NewEvent);
            observedStops.TryAdd(processEvent.ProcessId, 0);
            if (processEvent.ProcessId == Volatile.Read(ref probeProcessId)) probeStopped.TrySetResult();
            activeProcesses.TryRemove(processEvent.ProcessId, out var identity);
            _ = events.Writer.TryWrite(processEvent with { Identity = identity });
        };
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
            using (var probe = Process.Start(new ProcessStartInfo(
                Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", "/d /c exit 0")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }) ?? throw new InvalidOperationException("无法启动 WMI 事件流自检进程。"))
            {
                Volatile.Write(ref probeProcessId, probe.Id);
                if (observedStarts.ContainsKey(probe.Id)) probeStarted.TrySetResult();
                if (observedStops.ContainsKey(probe.Id)) probeStopped.TrySetResult();
                await Task.WhenAll(probeStarted.Task, probeStopped.Task)
                    .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken)
                    .ConfigureAwait(false);
            }
            reportSessionStarted?.Invoke();
            await foreach (var processEvent in events.Reader.ReadAllAsync(sessionCancellation.Token).ConfigureAwait(false))
            {
                var enrichedEvent = processEvent is ProcessStartedEvent startedEvent
                    ? EnrichStarted(startedEvent)
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

    private static ProcessStartedEvent EnrichStarted(ProcessStartedEvent started)
    {
        var parentName = NameForProcess(started.ParentProcessId);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ExecutablePath, CommandLine FROM Win32_Process WHERE ProcessId = {started.Identity.ProcessId}");
            using var results = searcher.Get();
            var process = results.Cast<ManagementObject>().FirstOrDefault();
            return started with
            {
                ExecutablePath = process?["ExecutablePath"]?.ToString(),
                CommandLine = process?["CommandLine"]?.ToString(),
                ParentProcessName = parentName
            };
        }
        catch (ManagementException)
        {
            return started with { ParentProcessName = parentName };
        }
        catch (UnauthorizedAccessException)
        {
            return started with { ParentProcessName = parentName };
        }
    }

    private static string? NameForProcess(int? processId)
    {
        if (processId is null) return null;

        try
        {
            using var process = Process.GetProcessById(processId.Value);
            return $"{process.ProcessName}.exe";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static ManagementEventWatcher CreateWatcher(string query) => new(new WqlEventQuery(query));
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
