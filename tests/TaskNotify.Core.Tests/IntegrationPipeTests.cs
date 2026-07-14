using System.Text.Json;
using TaskNotify.Ipc;
using Xunit;

namespace TaskNotify.Core.Tests;

public sealed class IntegrationPipeTests
{
    [Fact]
    public void External_event_type_uses_the_string_protocol_format()
    {
        var json = JsonSerializer.Serialize(new IntegrationPipeMessage
        {
            Type = IntegrationEventType.TaskSucceeded
        });

        Assert.Contains("\"type\":\"TaskSucceeded\"", json);
    }

    [Fact]
    public async Task Current_user_pipe_delivers_a_framed_message()
    {
        var delivered = new TaskCompletionSource<IntegrationPipeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipeName = $"TaskNotifyTest-{Guid.NewGuid():N}";
        using var cancellation = new CancellationTokenSource();
        using var server = new IntegrationPipeServer(message => delivered.TrySetResult(message), pipeName);
        using var client = new IntegrationPipeClient(pipeName);
        server.Start(cancellation.Token);
        await Task.Delay(20);

        Assert.True(await client.SendAsync(new IntegrationPipeMessage
        {
            Source = "test",
            TaskId = "task-1",
            Type = IntegrationEventType.TaskStarted
        }));

        var message = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("task-1", message.TaskId);

        cancellation.Cancel();
        server.Stop();
    }
}
