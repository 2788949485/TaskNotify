using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TaskNotify.Infrastructure.Database;

namespace TaskNotify.Infrastructure;

/// <summary>
/// Owns the SQLite connection string and runs schema migrations on startup.
/// Database file lives at <c>%LOCALAPPDATA%\TaskNotify\tasknotify.db</c>.
///
/// Concurrency: SQLite with WAL journaling handles concurrent readers + 1 writer.
/// All repositories share this single connection string; each opens a short-lived
/// connection per operation to avoid threading issues (Microsoft.Data.Sqlite's
/// connection object is not thread-safe).
/// </summary>
public sealed class SqliteDatabase
{
    public string ConnectionString { get; }
    private readonly ILogger<SqliteDatabase> _logger;
    private readonly SchemaMigration _migration;
    private int _initialized;

    public SqliteDatabase(string? customPath, ILogger<SqliteDatabase> logger, ILoggerFactory loggerFactory)
    {
        var path = string.IsNullOrWhiteSpace(customPath)
            ? DefaultPath()
            : customPath;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private
        }.ToString();

        _logger = logger;
        _migration = new SchemaMigration(this, loggerFactory.CreateLogger<SchemaMigration>());
    }

    /// <summary>Opens a new connection. Caller is responsible for disposing.</summary>
    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        // Enable WAL for better concurrency + durability on crash.
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            pragma.ExecuteNonQuery();
        }
        return connection;
    }

    /// <summary>Idempotently runs all pending migrations.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0) return;
        await _migration.ApplyPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string DefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "TaskNotify", "tasknotify.db");
    }
}
