using Microsoft.Data.Sqlite;
using TaskNotify.Core.Detection;
using TaskNotify.Core.Interfaces;

namespace TaskNotify.Infrastructure.Repositories;

/// <summary>
/// SQLite-backed <see cref="IDetectionRuleRepository"/>.
/// Stores regex patterns verbatim. MinimumDuration is stored as seconds.
/// </summary>
public sealed class DetectionRuleRepository : IDetectionRuleRepository
{
    private readonly SqliteDatabase _database;

    public DetectionRuleRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<DetectionRule>> LoadEnabledAsync(CancellationToken cancellationToken = default)
    {
        const string Sql = """
            SELECT RuleName, ProcessPattern, CommandPattern, ParentPattern,
                   MinimumDurationSeconds, ScoreAdjustment, Action, IsUserCreated
            FROM DetectionRules
            WHERE IsEnabled = 1
            ORDER BY IsUserCreated DESC, RuleName ASC;
            """;
        return await QueryRulesAsync(Sql, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DetectionRule>> LoadUserRulesAsync(CancellationToken cancellationToken = default)
    {
        const string Sql = """
            SELECT RuleName, ProcessPattern, CommandPattern, ParentPattern,
                   MinimumDurationSeconds, ScoreAdjustment, Action, IsUserCreated
            FROM DetectionRules
            WHERE IsUserCreated = 1
            ORDER BY RuleName ASC;
            """;
        return await QueryRulesAsync(Sql, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveUserRuleAsync(DetectionRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rule.Name);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DetectionRules (
                Id, RuleName, ProcessPattern, CommandPattern, ParentPattern,
                MinimumDurationSeconds, ScoreAdjustment, Action,
                IsEnabled, IsUserCreated
            ) VALUES (
                @id, @name, @procPat, @cmdPat, @parPat,
                @minDur, @scoreAdj, @action,
                1, 1
            )
            ON CONFLICT(Id) DO UPDATE SET
                RuleName = excluded.RuleName,
                ProcessPattern = excluded.ProcessPattern,
                CommandPattern = excluded.CommandPattern,
                ParentPattern = excluded.ParentPattern,
                MinimumDurationSeconds = excluded.MinimumDurationSeconds,
                ScoreAdjustment = excluded.ScoreAdjustment,
                Action = excluded.Action;
            """;

        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@name", rule.Name);
        command.Parameters.AddWithValue("@procPat", (object?)rule.ProcessNamePattern ?? DBNull.Value);
        command.Parameters.AddWithValue("@cmdPat", (object?)rule.CommandLinePattern ?? DBNull.Value);
        command.Parameters.AddWithValue("@parPat", (object?)rule.ParentProcessNamePattern ?? DBNull.Value);
        command.Parameters.AddWithValue("@minDur", rule.MinimumDuration is { } duration ? (int)duration.TotalSeconds : 0);
        command.Parameters.AddWithValue("@scoreAdj", rule.ScoreAdjustment);
        command.Parameters.AddWithValue("@action", (int)rule.Action);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertUserRuleByNameAsync(DetectionRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rule.Name);

        await using var connection = _database.OpenConnection();
        await using var lookup = connection.CreateCommand();
        lookup.CommandText = "SELECT Id FROM DetectionRules WHERE RuleName = @name AND IsUserCreated = 1 LIMIT 1;";
        lookup.Parameters.AddWithValue("@name", rule.Name);
        var existingIdObj = await lookup.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var existingId = existingIdObj as string;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DetectionRules (
                Id, RuleName, ProcessPattern, CommandPattern, ParentPattern,
                MinimumDurationSeconds, ScoreAdjustment, Action,
                IsEnabled, IsUserCreated
            ) VALUES (
                @id, @name, @procPat, @cmdPat, @parPat,
                @minDur, @scoreAdj, @action,
                1, 1
            )
            ON CONFLICT(Id) DO UPDATE SET
                RuleName = excluded.RuleName,
                ProcessPattern = excluded.ProcessPattern,
                CommandPattern = excluded.CommandPattern,
                ParentPattern = excluded.ParentPattern,
                MinimumDurationSeconds = excluded.MinimumDurationSeconds,
                ScoreAdjustment = excluded.ScoreAdjustment,
                Action = excluded.Action;
            """;

        command.Parameters.AddWithValue("@id", existingId ?? Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@name", rule.Name);
        command.Parameters.AddWithValue("@procPat", (object?)rule.ProcessNamePattern ?? DBNull.Value);
        command.Parameters.AddWithValue("@cmdPat", (object?)rule.CommandLinePattern ?? DBNull.Value);
        command.Parameters.AddWithValue("@parPat", (object?)rule.ParentProcessNamePattern ?? DBNull.Value);
        command.Parameters.AddWithValue("@minDur", rule.MinimumDuration is { } duration ? (int)duration.TotalSeconds : 0);
        command.Parameters.AddWithValue("@scoreAdj", rule.ScoreAdjustment);
        command.Parameters.AddWithValue("@action", (int)rule.Action);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteUserRuleAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM DetectionRules WHERE RuleName = @name AND IsUserCreated = 1;";
        command.Parameters.AddWithValue("@name", name);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    private async Task<IReadOnlyList<DetectionRule>> QueryRulesAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var results = new List<DetectionRule>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new DetectionRule(
                Name: reader.GetString(0),
                ScoreAdjustment: reader.GetInt32(5),
                Action: (RuleAction)reader.GetInt32(6),
                ProcessNamePattern: reader.IsDBNull(1) ? null : reader.GetString(1),
                CommandLinePattern: reader.IsDBNull(2) ? null : reader.GetString(2),
                ParentProcessNamePattern: reader.IsDBNull(3) ? null : reader.GetString(3),
                MinimumDuration: TimeSpan.FromSeconds(reader.GetInt32(4)),
                IsUserCreated: reader.GetInt32(7) != 0));
        }
        return results;
    }
}
