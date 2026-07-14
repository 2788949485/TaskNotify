using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace TaskNotify.Ipc;

/// <summary>
/// Named Pipe client that sends integration events to the TaskNotify Desktop process.
///
/// Usage (external agents):
///   using var client = new IntegrationPipeClient();
///   await client.SendAsync(message);
///
/// If the server is not running, SendAsync returns false (non-blocking failure).
/// </summary>
public sealed class IntegrationPipeClient : IDisposable
{
    private const string PipeName = "TaskNotifyPipe";
    private const int MaxMessageBytes = 256 * 1024;
    private readonly string _pipeName;

    public IntegrationPipeClient(string pipeName = PipeName)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? throw new ArgumentException("Pipe name is required.", nameof(pipeName)) : pipeName;
    }

    /// <summary>
    /// Sends a message to the TaskNotify server.
    /// Returns true if sent successfully, false if the server is unavailable.
    /// </summary>
    public async Task<bool> SendAsync(IntegrationPipeMessage message, CancellationToken cancellationToken = default)
    {
        if (message is null) return false;

        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        var payload = Encoding.UTF8.GetBytes(json);
        if (payload.Length > MaxMessageBytes)
        {
            return false;
        }

        var frame = new byte[4 + payload.Length];
        // Big-endian length prefix
        frame[0] = (byte)((payload.Length >> 24) & 0xFF);
        frame[1] = (byte)((payload.Length >> 16) & 0xFF);
        frame[2] = (byte)((payload.Length >> 8) & 0xFF);
        frame[3] = (byte)(payload.Length & 0xFF);
        Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);

        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            // Connect with timeout
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromMilliseconds(100));

            await pipe.ConnectAsync(connectCts.Token).ConfigureAwait(false);
            await pipe.WriteAsync(frame, 0, frame.Length, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() { }
}
