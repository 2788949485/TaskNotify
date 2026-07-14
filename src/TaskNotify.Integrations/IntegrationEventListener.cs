using TaskNotify.Core.Events;
using TaskNotify.Core.Tasks;
using TaskNotify.Ipc;
using TaskNotify.Integrations;

namespace TaskNotify.Integrations;

/// <summary>
/// Listens for integration events from the Named Pipe server and feeds them
/// into the ProcessTaskTracker. This is the central hub for all AI agent integrations.
/// </summary>
public sealed class IntegrationEventListener : IDisposable
{
    private readonly IntegrationPipeServer _pipeServer;
    private readonly IntegrationEventConverter _converter = new();
    private readonly ProcessTaskTracker _tracker;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _started;

    /// <summary>
    /// Event raised when an integration produces a completion notice.
    /// </summary>
    public event Action<TaskCompletionNotice>? OnCompletionNotice;

    public IntegrationEventListener(ProcessTaskTracker tracker)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _pipeServer = new(ProcessMessage);
    }

    /// <summary>
    /// Starts listening for integration events. Safe to call multiple times.
    /// </summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _pipeServer.Start(_cts.Token);
    }

    /// <summary>
    /// Stops listening and closes the pipe server.
    /// </summary>
    public void Stop()
    {
        _started = false;
        _cts.Cancel();
        _cts.Dispose();
        _pipeServer.Stop();
    }

    private void ProcessMessage(IntegrationPipeMessage pipeMsg)
    {
        var event_ = _converter.Map(pipeMsg);
        if (event_ is null) return;

        var notice = _tracker.Handle(event_);
        if (notice is not null)
        {
            OnCompletionNotice?.Invoke(notice);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
