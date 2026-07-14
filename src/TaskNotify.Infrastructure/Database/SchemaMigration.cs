using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace TaskNotify.Infrastructure.Database;

/// <summary>
/// Versioned schema migrations. Tracks applied versions in a <c>schema_versions</c> table
/// and applies pending embedded SQL resources in order.
///
/// Versioning scheme: filename <c>v{NNNN}_*.sql</c>, applied numerically.
/// Forward-only — no down migrations (per AGENTS.md: "向前兼容、可恢复").
/// </summary>
public sealed class SchemaMigration
{
    private const string VersionTable = "schema_versions";
    private readonly SqliteDatabase _database;
    private readonly ILogger<SchemaMigration> _logger;

    public SchemaMigration(SqliteDatabase database, ILogger<SchemaMigration> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task ApplyPendingMigrationsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureVersionTableAsync(cancellationToken).ConfigureAwait(false);
        var applied = await LoadAppliedVersionsAsync(cancellationToken).ConfigureAwait(false);
        var pending = DiscoverMigrations().Where(m => !applied.Contains(m.Version)).OrderBy(m => m.Version).ToList();

        if (pending.Count == 0)
        {
            _logger.LogDebug("Schema up to date; {Count} migrations already applied.", applied.Count);
            return;
        }

        foreach (var migration in pending)
        {
            await ApplyOneAsync(migration, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureVersionTableAsync(CancellationToken cancellationToken)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE IF NOT EXISTS {VersionTable} (
                Version INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                AppliedAt TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlySet<int>> LoadAppliedVersionsAsync(CancellationToken cancellationToken)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Version FROM {VersionTable};";
        var versions = new HashSet<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            versions.Add(reader.GetInt32(0));
        }
        return versions;
    }

    private async Task ApplyOneAsync(MigrationFile migration, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Applying schema migration v{Version}: {Name}", migration.Version, migration.Name);

        await using var connection = _database.OpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using (var script = connection.CreateCommand())
            {
                script.Transaction = (SqliteTransaction)transaction;
                script.CommandText = migration.Sql;
                await script.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var record = connection.CreateCommand())
            {
                record.Transaction = (SqliteTransaction)transaction;
                record.CommandText = $"INSERT INTO {VersionTable} (Version, Name, AppliedAt) VALUES (@v, @n, @t);";
                record.Parameters.AddWithValue("@v", migration.Version);
                record.Parameters.AddWithValue("@n", migration.Name);
                record.Parameters.AddWithValue("@t", DateTimeOffset.UtcNow.ToString("O"));
                await record.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static IReadOnlyList<MigrationFile> DiscoverMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = $"{typeof(SchemaMigration).Namespace}.Migrations.";
        var results = new List<MigrationFile>();

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // e.g. "TaskNotify.Infrastructure.Database.Migrations.v0001_initial.sql"
            var fileName = name.Substring(prefix.Length);
            var dash = fileName.IndexOf('_');
            if (dash <= 0) continue;

            if (!int.TryParse(fileName.Substring(1, dash - 1), out var version))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            var sql = reader.ReadToEnd();

            results.Add(new MigrationFile(version, fileName, sql));
        }

        return results;
    }

    private sealed record MigrationFile(int Version, string Name, string Sql);
}
