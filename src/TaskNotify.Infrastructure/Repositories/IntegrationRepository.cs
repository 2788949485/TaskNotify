using Microsoft.Data.Sqlite;
using TaskNotify.Core.Interfaces;

namespace TaskNotify.Infrastructure.Repositories;

public sealed class IntegrationRepository : IIntegrationRepository
{
    private readonly SqliteDatabase _database;

    public IntegrationRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<IntegrationRecord>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT IntegrationType, IsInstalled, IsEnabled, Version, ConfigPath, LastCheckedAt
            FROM Integrations
            ORDER BY IntegrationType ASC;
            """;

        var results = new List<IntegrationRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new IntegrationRecord(
                IntegrationType: reader.GetString(0),
                IsInstalled: reader.GetInt32(1) != 0,
                IsEnabled: reader.GetInt32(2) != 0,
                Version: reader.IsDBNull(3) ? null : reader.GetString(3),
                ConfigPath: reader.IsDBNull(4) ? null : reader.GetString(4),
                LastCheckedAt: reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        }
        return results;
    }

    public async Task<IntegrationRecord?> FindByTypeAsync(string integrationType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(integrationType);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT IntegrationType, IsInstalled, IsEnabled, Version, ConfigPath, LastCheckedAt
            FROM Integrations
            WHERE IntegrationType = @type
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@type", integrationType);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        return new IntegrationRecord(
            IntegrationType: reader.GetString(0),
            IsInstalled: reader.GetInt32(1) != 0,
            IsEnabled: reader.GetInt32(2) != 0,
            Version: reader.IsDBNull(3) ? null : reader.GetString(3),
            ConfigPath: reader.IsDBNull(4) ? null : reader.GetString(4),
            LastCheckedAt: reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind));
    }

    public async Task UpsertAsync(IntegrationRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.IntegrationType);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Integrations (Id, IntegrationType, IsInstalled, IsEnabled, Version, ConfigPath, LastCheckedAt)
            VALUES (@id, @type, @inst, @en, @ver, @cfg, @last)
            ON CONFLICT(IntegrationType) DO UPDATE SET
                IsInstalled = excluded.IsInstalled,
                IsEnabled = excluded.IsEnabled,
                Version = excluded.Version,
                ConfigPath = excluded.ConfigPath,
                LastCheckedAt = excluded.LastCheckedAt;
            """;

        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@type", record.IntegrationType);
        command.Parameters.AddWithValue("@inst", record.IsInstalled ? 1 : 0);
        command.Parameters.AddWithValue("@en", record.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@ver", (object?)record.Version ?? DBNull.Value);
        command.Parameters.AddWithValue("@cfg", (object?)record.ConfigPath ?? DBNull.Value);
        command.Parameters.AddWithValue("@last", record.LastCheckedAt is null ? DBNull.Value : record.LastCheckedAt.Value.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
