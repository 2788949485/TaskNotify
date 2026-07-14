using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TaskNotify.Core.Interfaces;

namespace TaskNotify.Infrastructure.Repositories;

public sealed class ProcessEventRepository : IProcessEventRepository
{
    private readonly SqliteDatabase _database;
    private readonly ILogger<ProcessEventRepository> _logger;

    public ProcessEventRepository(SqliteDatabase database, ILogger<ProcessEventRepository> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task AppendAsync(int processId, int? parentProcessId, string processName, string eventType, DateTimeOffset eventTime, Guid? taskId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ProcessEvents (Id, TaskId, ProcessId, ParentProcessId, ProcessName, EventType, EventTime)
            VALUES (@id, @taskId, @pid, @ppid, @pname, @etype, @etime);
            """;

        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@taskId", taskId is null ? DBNull.Value : taskId.Value.ToString());
        command.Parameters.AddWithValue("@pid", processId);
        command.Parameters.AddWithValue("@ppid", (object?)parentProcessId ?? DBNull.Value);
        command.Parameters.AddWithValue("@pname", processName);
        command.Parameters.AddWithValue("@etype", eventType);
        command.Parameters.AddWithValue("@etime", eventTime.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ProcessEvents WHERE EventTime < @cutoff;";
        command.Parameters.AddWithValue("@cutoff", cutoff.ToString("O"));
        var rows = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Purged {Count} process events older than {Cutoff:O}.", rows, cutoff);
        return rows;
    }
}
