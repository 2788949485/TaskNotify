using System.Diagnostics;
using System.Runtime.InteropServices;
using TaskNotify.Core.Detection;
using TaskNotify.Core.Events;

namespace TaskNotify.ProcessMonitor;

public sealed class SnapshotProcessMonitor
{
    // ponytail: 15-second degraded-mode polling catches the 20-second notification threshold; replace with ETW when available.
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly HashSet<string> _candidateNames;

    public SnapshotProcessMonitor() : this(DetectionMode.Balanced) { }

    public SnapshotProcessMonitor(DetectionMode detectionMode)
    {
        _candidateNames = new HashSet<string>(CandidatesFor(detectionMode), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Snapshot polling only catches processes whose name we watch. In Precise mode the
    /// set is empty (no inference); in Broad mode we expand to additional toolchains.
    /// </summary>
    private static IEnumerable<string> CandidatesFor(DetectionMode mode)
    {
        // Common shortlist shared by Balanced and Broad. Precise returns nothing.
        if (mode == DetectionMode.Precise) yield break;

        yield return "python.exe";
        yield return "pythonw.exe";
        yield return "py.exe";
        yield return "node.exe";
        yield return "npm.exe";
        yield return "npm.cmd";
        yield return "pnpm.exe";
        yield return "pnpm.cmd";
        yield return "yarn.exe";
        yield return "yarn.cmd";
        yield return "ffmpeg.exe";

        if (mode != DetectionMode.Broad) yield break;

        yield return "java.exe";
        yield return "javaw.exe";
        yield return "dotnet.exe";
        yield return "msbuild.exe";
        yield return "vstest.console.exe";
        yield return "cl.exe";
        yield return "link.exe";
        yield return "cargo.exe";
        yield return "rustc.exe";
        yield return "cmake.exe";
        yield return "ninja.exe";
        yield return "make.exe";
        yield return "gcc.exe";
        yield return "g++.exe";
    }

    public async Task RunAsync(
        Func<ProcessLifecycleEvent, CancellationToken, ValueTask> handleEvent,
        CancellationToken cancellationToken)
    {
        var tracked = new Dictionary<int, SnapshotProcess>();

        while (!cancellationToken.IsCancellationRequested)
        {
            var current = ReadCurrentSessionProcesses();
            if (current is not null)
            {
                foreach (var previous in tracked.Values.Where(process => !current.ContainsKey(process.Identity.ProcessId)).ToArray())
                {
                    await handleEvent(new ProcessStoppedEvent(previous.Identity.ProcessId, previous.ProcessName, DateTimeOffset.UtcNow, previous.Identity), cancellationToken).ConfigureAwait(false);
                    tracked.Remove(previous.Identity.ProcessId);
                }

                foreach (var process in current.Values.OrderBy(process => current.ContainsKey(process.ParentProcessId ?? -1)))
                {
                    if (tracked.TryGetValue(process.Identity.ProcessId, out var previous) && previous.Identity == process.Identity)
                    {
                        continue;
                    }

                    if (previous is not null)
                    {
                        await handleEvent(new ProcessStoppedEvent(previous.Identity.ProcessId, previous.ProcessName, DateTimeOffset.UtcNow, previous.Identity), cancellationToken).ConfigureAwait(false);
                    }

                    await handleEvent(new ProcessStartedEvent(process.Identity, process.ParentProcessId, process.ProcessName, process.Identity.StartedAt, ParentProcessName: process.ParentProcessName), cancellationToken).ConfigureAwait(false);
                    tracked[process.Identity.ProcessId] = process;
                }
            }

            await Task.Delay(Interval, cancellationToken).ConfigureAwait(false);
        }
    }

    private Dictionary<int, SnapshotProcess>? ReadCurrentSessionProcesses()
    {
        var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == InvalidHandleValue)
        {
            return null;
        }

        try
        {
            var entries = new Dictionary<int, ProcessEntry>();
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (!Process32First(snapshot, ref entry))
            {
                return new Dictionary<int, SnapshotProcess>();
            }

            do
            {
                entries[(int)entry.ProcessId] = entry;
                entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            }
            while (Process32Next(snapshot, ref entry));

            using var currentProcess = Process.GetCurrentProcess();
            var currentSessionId = currentProcess.SessionId;
            var result = new Dictionary<int, SnapshotProcess>();
            foreach (var entryValue in entries.Values.Where(entryValue => _candidateNames.Contains(entryValue.ExecutableName)))
            {
                try
                {
                    using var process = Process.GetProcessById((int)entryValue.ProcessId);
                    if (process.SessionId != currentSessionId)
                    {
                        continue;
                    }

                    entries.TryGetValue((int)entryValue.ParentProcessId, out var parent);
                    result[(int)entryValue.ProcessId] = new(
                        new((int)entryValue.ProcessId, new DateTimeOffset(process.StartTime)),
                        entryValue.ExecutableName,
                        (int?)entryValue.ParentProcessId,
                        parent.ExecutableName);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }
            }

            return result;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    private sealed record SnapshotProcess(ProcessIdentity Identity, string ProcessName, int? ParentProcessId, string? ParentProcessName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint ThreadCount;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExecutableName;
    }

    private const uint Th32csSnapProcess = 0x00000002;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
