using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace TaskNotify.Ipc;

/// <summary>
/// Named Pipe server that listens for integration events from external agents.
///
/// Protocol:
/// - Pipe name: "TaskNotifyPipe" (local-only, current-user ACL)
/// - Frame format: 4-byte big-endian length prefix + UTF-8 JSON payload
/// - Max message size: 256 KB
/// - Single connection per client; server handles one client at a time
/// </summary>
public sealed class IntegrationPipeServer : IDisposable
{
    private const string PipeName = "TaskNotifyPipe";
    private const int MaxMessageBytes = 256 * 1024; // 256 KB
    private const int ReadBufferSize = 4096;
    private readonly Action<IntegrationPipeMessage> _onMessage;
    private readonly string _pipeName;
    private volatile bool _listening;
    private Task? _acceptLoop;

    public IntegrationPipeServer(Action<IntegrationPipeMessage> onMessage, string pipeName = PipeName)
    {
        _onMessage = onMessage ?? throw new ArgumentNullException(nameof(onMessage));
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? throw new ArgumentException("Pipe name is required.", nameof(pipeName)) : pipeName;
    }

    /// <summary>
    /// Starts listening for incoming connections and messages.
    /// Non-blocking: returns immediately; messages arrive via _onMessage.
    /// </summary>
    public void Start(CancellationToken cancellationToken = default)
    {
        if (_listening) return;
        _listening = true;
        _acceptLoop = AcceptLoop(cancellationToken);
    }

    /// <summary>
    /// Stops accepting new connections.
    /// </summary>
    public void Stop()
    {
        _listening = false;
        _acceptLoop?.Wait();
    }

    private async Task AcceptLoop(CancellationToken cancellationToken)
    {
        while (_listening && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                // Timeout: give clients 10 seconds to connect
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    await pipe.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    continue;
                }

                if (!_listening)
                {
                    continue;
                }

                await HandleClientAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                // Pipe server recycled; continue
                continue;
            }
            catch (Exception)
            {
                // Don't crash the server on unexpected errors
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[4];
        var payloadBuffer = new byte[ReadBufferSize];
        var pending = Array.Empty<byte>();

        while (_listening && !cancellationToken.IsCancellationRequested)
        {
            int bytesRead;
            try
            {
                bytesRead = await pipe.ReadAsync(payloadBuffer, 0, payloadBuffer.Length, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                break;
            }

            if (bytesRead == 0) break; // client disconnected

            // Prepend any leftover bytes from previous read
            if (pending.Length > 0)
            {
                var combined = new byte[pending.Length + bytesRead];
                Buffer.BlockCopy(pending, 0, combined, 0, pending.Length);
                Buffer.BlockCopy(payloadBuffer, 0, combined, pending.Length, bytesRead);
                pending = combined;
            }
            else
            {
                pending = new byte[bytesRead];
                Buffer.BlockCopy(payloadBuffer, 0, pending, 0, bytesRead);
            }

            // Process complete frames
            while (pending.Length >= 4)
            {
                // Need at least 4 bytes for length prefix
                if (pending.Length < 4) break;

                // Read 4-byte big-endian length
                var msgLen = (pending[0] << 24) | (pending[1] << 16) | (pending[2] << 8) | pending[3];

                // Sanity check
                if (msgLen <= 0 || msgLen > MaxMessageBytes)
                {
                    // Bad frame; skip and try next 4 bytes
                    pending = pending.Skip(1).ToArray();
                    continue;
                }

                if (pending.Length < 4 + msgLen)
                {
                    // Incomplete message; wait for more data
                    break;
                }

                // Extract the message payload
                var payload = new byte[msgLen];
                Buffer.BlockCopy(pending, 4, payload, 0, msgLen);

                // Keep remaining bytes
                pending = pending.Skip(4 + msgLen).ToArray();

                // Parse JSON
                try
                {
                    var msg = JsonSerializer.Deserialize<IntegrationPipeMessage>(
                        Encoding.UTF8.GetString(payload),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (msg is not null)
                    {
                        _onMessage(msg);
                    }
                }
                catch (JsonException)
                {
                    // Malformed JSON; silently drop
                }
            }
        }
    }

    public void Dispose()
    {
        _listening = false;
        _acceptLoop?.Wait();
    }
}
