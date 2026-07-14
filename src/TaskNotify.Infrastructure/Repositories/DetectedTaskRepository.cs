using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TaskNotify.Core.Detection;
using TaskNotify.Core.Interfaces;
using TaskNotify.Core.Tasks;

namespace TaskNotify.Infrastructure.Repositories;

/// <summary>
/// SQLite-backed <see cref="IDetectedTaskRepository"/>.
///
/// Stores enums as ints (TaskState, CompletionConfidence) for compactness.
/// DateTimeOffset serialized as ISO-8601 round-trip ("O") for UTC round-tripping.
/// </summary>
public sealed class DetectedTaskRepository : IDetectedTaskRepository
{
    private readonly SqliteDatabase _database;
    private readonly ILogger<DetectedTaskRepository> _logger;

    public DetectedTaskRepository(SqliteDatabase database, ILogger<DetectedTaskRepository> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task SaveAsync(DetectedTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DetectedTasks (
                Id, Source, DisplayName, State, Confidence, ProbabilityScore,
                RootProcessId, ProcessName, CommandSummary, WorkingDirectory,
                DetectedAt, StartedAt, EndedAt, ExitCode, ResultMessage,
                OpenPath, LogPath, CorrelationKey, MetadataJson
            ) VALUES (
                @id, @source, @displayName, @state, @confidence, @probScore,
                @rootPid, @procName, @cmdSummary, @workDir,
                @detectedAt, @startedAt, @endedAt, @exitCode, @resultMsg,
                @openPath, @logPath, @correlationKey, @metadataJson
            )
            ON CONFLICT(Id) DO UPDATE SET
                Source = excluded.Source,
                DisplayName = excluded.DisplayName,
                State = excluded.State,
                Confidence = excluded.Confidence,
                ProbabilityScore = excluded.ProbabilityScore,
                RootProcessId = excluded.RootProcessId,
                ProcessName = excluded.ProcessName,
                CommandSummary = excluded.CommandSummary,
                WorkingDirectory = excluded.WorkingDirectory,
                StartedAt = excluded.StartedAt,
                EndedAt = excluded.EndedAt,
                ExitCode = excluded.ExitCode,
                ResultMessage = excluded.ResultMessage,
                OpenPath = excluded.OpenPath,
                LogPath = excluded.LogPath,
                MetadataJson = excluded.MetadataJson;
            """;

        AddParameter(command, "@id", task.Id.ToString());
        AddParameter(command, "@source", task.Source);
        AddParameter(command, "@displayName", task.DisplayName);
        AddParameter(command, "@state", (int)task.State);
        AddParameter(command, "@confidence", (int)task.CompletionConfidence);
        AddParameter(command, "@probScore", task.TaskProbability);
        AddParameter(command, "@rootPid", (object?)task.RootProcessId ?? DBNull.Value);
        AddParameter(command, "@procName", (object?)task.ProcessName ?? DBNull.Value);
        AddParameter(command, "@cmdSummary", (object?)task.CommandSummary ?? DBNull.Value);
        AddParameter(command, "@workDir", (object?)task.WorkingDirectory ?? DBNull.Value);
        AddParameter(command, "@detectedAt", task.DetectedAt.ToString("O"));
        AddParameter(command, "@startedAt", task.StartedAt is null ? DBNull.Value : task.StartedAt.Value.ToString("O"));
        AddParameter(command, "@endedAt", task.EndedAt is null ? DBNull.Value : task.EndedAt.Value.ToString("O"));
        AddParameter(command, "@exitCode", (object?)task.ExitCode ?? DBNull.Value);
        AddParameter(command, "@resultMsg", (object?)task.ResultMessage ?? DBNull.Value);
        AddParameter(command, "@openPath", (object?)task.OpenPath ?? DBNull.Value);
        AddParameter(command, "@logPath", (object?)task.LogPath ?? DBNull.Value);
        AddParameter(command, "@correlationKey", (object?)task.CorrelationKey ?? DBNull.Value);
        AddParameter(command, "@metadataJson", task.MetadataJson);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DetectedTask?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectClause + " WHERE Id = @id LIMIT 1;";
        command.Parameters.AddWithValue("@id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadTask(reader) : null;
    }

    public async Task<IReadOnlyList<DetectedTask>> FindActiveAsync(CancellationToken cancellationToken = default)
    {
        const string Sql = SelectClause + " WHERE State NOT IN (@s1, @s2, @s3, @s4, @s5, @s6) ORDER BY DetectedAt DESC;";
        return await QueryListAsync(Sql, cancellationToken,
            ("@s1", (int)TaskState.Succeeded),
            ("@s2", (int)TaskState.Failed),
            ("@s3", (int)TaskState.Cancelled),
            ("@s4", (int)TaskState.TimedOut),
            ("@s5", (int)TaskState.EndedUnknown),
            ("@s6", (int)TaskState.Ignored)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DetectedTask>> FindRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        var sql = SelectClause + " WHERE EndedAt IS NOT NULL ORDER BY EndedAt DESC LIMIT @limit;";
        return await QueryListAsync(sql, cancellationToken, ("@limit", Math.Max(1, limit))).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DateTimeOffset>> FindRecentForProcessAsync(
        string processName,
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT(substr(EndedAt, 1, 10)) AS Day
            FROM DetectedTasks
            WHERE ProcessName = @proc AND EndedAt IS NOT NULL AND EndedAt >= @since
            ORDER BY Day DESC;
            """;
        command.Parameters.AddWithValue("@proc", processName);
        command.Parameters.AddWithValue("@since", since.ToString("O"));

        var days = new List<DateTimeOffset>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // Each row is "YYYY-MM-DD"; parse to a UTC midnight DateTimeOffset.
            if (DateTimeOffset.TryParse(reader.GetString(0) + "T00:00:00Z",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var day))
            {
                days.Add(day);
            }
        }
        return days;
    }

    public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM DetectedTasks WHERE EndedAt IS NOT NULL AND EndedAt < @cutoff;";
        command.Parameters.AddWithValue("@cutoff", cutoff.ToString("O"));
        var rows = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Purged {Count} tasks older than {Cutoff:O}.", rows, cutoff);
        return rows;
    }

    public async Task<int> PurgeAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM DetectedTasks;";
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string SelectClause = """
        SELECT Id, Source, DisplayName, State, Confidence, ProbabilityScore,
               RootProcessId, ProcessName, CommandSummary, WorkingDirectory,
               DetectedAt, StartedAt, EndedAt, ExitCode, ResultMessage,
               OpenPath, LogPath, CorrelationKey, MetadataJson
        FROM DetectedTasks
        """;

    private async Task<IReadOnlyList<DetectedTask>> QueryListAsync(string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var results = new List<DetectedTask>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadTask(reader));
        }
        return results;
    }

    private static DetectedTask ReadTask(SqliteDataReader reader)
    {
        var task = new DetectedTask(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(5),
            DateTimeOffset.Parse(reader.GetString(10), null, System.Globalization.DateTimeStyles.RoundtripKind),
            reader.IsDBNull(6) ? null : reader.GetInt32(6),
            processName: reader.IsDBNull(7) ? null : reader.GetString(7),
            workingDirectory: reader.IsDBNull(9) ? null : reader.GetString(9),
            correlationKey: reader.IsDBNull(17) ? null : reader.GetString(17));

        // Restore mutable state via Set methods (only public surface).
        var state = (TaskState)reader.GetInt32(3);
        var confidence = (CompletionConfidence)reader.GetInt32(4);
        if (state != TaskState.Candidate || confidence != CompletionConfidence.Unknown)
        {
            // Apply a no-op transition via reflection-free reconstruction:
            // we expose internal fields by going through Apply with the recorded signal.
            // Since Apply validates transitions, simpler path: reset via Set methods on private setters
            // is not available — so we re-apply with a synthetic signal matching the stored state.
            RestoreState(task, state, confidence,
                reader.IsDBNull(11) ? null : DateTimeOffset.Parse(reader.GetString(11), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.IsDBNull(12) ? null : DateTimeOffset.Parse(reader.GetString(12), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.IsDBNull(13) ? null : reader.GetInt32(13));
        }

        if (!reader.IsDBNull(8)) task.SetCommandSummary(reader.GetString(8));
        if (!reader.IsDBNull(14)) task.SetResultMessage(reader.GetString(14));
        if (!reader.IsDBNull(15)) task.SetOpenPath(reader.GetString(15));
        if (!reader.IsDBNull(16)) task.SetLogPath(reader.GetString(16));
        if (!reader.IsDBNull(18)) task.SetMetadataJson(reader.GetString(18));

        return task;
    }

    /// <summary>
    /// Re-applies signals to land the task in the stored state.
    /// State-machine is monotonic forward; we replay Started then the terminal signal.
    /// </summary>
    private static void RestoreState(DetectedTask task, TaskState targetState, CompletionConfidence confidence, DateTimeOffset? startedAt, DateTimeOffset? endedAt, int? exitCode)
    {
        if (startedAt.HasValue)
        {
            task.Apply(TaskSignal.Started, confidence, startedAt.Value);
        }

        var signal = targetState switch
        {
            TaskState.Running => (TaskSignal?)null,
            TaskState.WaitingForInput => TaskSignal.WaitingForInput,
            TaskState.WaitingForPermission => TaskSignal.WaitingForPermission,
            TaskState.PossiblyCompleted => TaskSignal.PossiblyCompleted,
            TaskState.Succeeded => TaskSignal.Succeeded,
            TaskState.Failed => TaskSignal.Failed,
            TaskState.Cancelled => TaskSignal.Cancelled,
            TaskState.TimedOut => TaskSignal.TimedOut,
            TaskState.EndedUnknown => TaskSignal.ProcessEnded,
            TaskState.Ignored => TaskSignal.Ignored,
            _ => null
        };

        if (signal.HasValue && endedAt.HasValue)
        {
            task.Apply(signal.Value, confidence, endedAt.Value, exitCode);
        }
    }

    private static void AddParameter(SqliteCommand command, string name, object value)
    {
        command.Parameters.AddWithValue(name, value is null ? DBNull.Value : value);
    }
}
