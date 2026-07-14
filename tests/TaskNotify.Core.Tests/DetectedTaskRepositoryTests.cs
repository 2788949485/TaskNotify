using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TaskNotify.Core.Detection;
using TaskNotify.Core.Tasks;
using TaskNotify.Infrastructure;
using TaskNotify.Infrastructure.Repositories;
using Xunit;

namespace TaskNotify.Core.Tests;

/// <summary>
/// Phase 0.8 — verifies SQLite schema migration + DetectedTaskRepository round-trip.
/// Uses an isolated temp file per test class instance to avoid cross-test contamination.
/// </summary>
public sealed class DetectedTaskRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteDatabase _database;
    private readonly DetectedTaskRepository _repository;

    public DetectedTaskRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tasknotify-test-{Guid.NewGuid():N}.db");
        _database = new SqliteDatabase(_dbPath, NullLogger<SqliteDatabase>.Instance, NullLoggerFactory.Instance);
        _database.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        _repository = new DetectedTaskRepository(_database, NullLogger<DetectedTaskRepository>.Instance);
    }

    [Fact]
    public async Task Save_and_FindById_round_trip_preserves_all_fields()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new DetectedTask(
            Guid.NewGuid(),
            "claude",
            "Refactor module",
            85,
            now,
            1234,
            processName: "claude.exe",
            workingDirectory: @"D:\code",
            correlationKey: "claude-deadbeef");
        task.SetCommandSummary("claude --build");
        task.SetResultMessage("ok");
        task.SetOpenPath(@"D:\code\out");
        task.SetLogPath(@"D:\code\log.txt");
        task.SetMetadataJson("""{"k":"v"}""");

        task.Apply(TaskSignal.Started, CompletionConfidence.IntegrationConfirmed, now);
        task.Apply(TaskSignal.Succeeded, CompletionConfidence.IntegrationConfirmed, now.AddSeconds(30), 0);

        await _repository.SaveAsync(task, CancellationToken.None);

        var loaded = await _repository.FindByIdAsync(task.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(task.Id, loaded!.Id);
        Assert.Equal("claude", loaded.Source);
        Assert.Equal("Refactor module", loaded.DisplayName);
        Assert.Equal(85, loaded.TaskProbability);
        Assert.Equal(1234, loaded.RootProcessId);
        Assert.Equal("claude.exe", loaded.ProcessName);
        Assert.Equal("claude --build", loaded.CommandSummary);
        Assert.Equal(@"D:\code", loaded.WorkingDirectory);
        Assert.Equal(0, loaded.ExitCode);
        Assert.Equal(TaskState.Succeeded, loaded.State);
        Assert.Equal(CompletionConfidence.IntegrationConfirmed, loaded.CompletionConfidence);
        Assert.Equal("ok", loaded.ResultMessage);
        Assert.Equal(@"D:\code\out", loaded.OpenPath);
        Assert.Equal(@"D:\code\log.txt", loaded.LogPath);
        Assert.Equal("claude-deadbeef", loaded.CorrelationKey);
        Assert.Equal("""{"k":"v"}""", loaded.MetadataJson);
        Assert.NotNull(loaded.StartedAt);
        Assert.NotNull(loaded.EndedAt);
    }

    [Fact]
    public async Task Save_upserts_on_conflict()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new DetectedTask(Guid.NewGuid(), "wmi", "python.exe", 50, now, 999);

        await _repository.SaveAsync(task, CancellationToken.None);
        task.SetDisplayName("renamed");
        task.SetTaskProbability(70);
        await _repository.SaveAsync(task, CancellationToken.None);

        var loaded = await _repository.FindByIdAsync(task.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("renamed", loaded!.DisplayName);
        Assert.Equal(70, loaded.TaskProbability);
    }

    [Fact]
    public async Task FindActive_excludes_terminal_states()
    {
        var now = DateTimeOffset.UtcNow;
        var running = new DetectedTask(Guid.NewGuid(), "wmi", "running", 50, now, 1);
        running.Apply(TaskSignal.Started, CompletionConfidence.Unknown, now);

        var done = new DetectedTask(Guid.NewGuid(), "wmi", "done", 50, now, 2);
        done.Apply(TaskSignal.Started, CompletionConfidence.Unknown, now);
        done.Apply(TaskSignal.Succeeded, CompletionConfidence.ExitCodeConfirmed, now, 0);

        await _repository.SaveAsync(running, CancellationToken.None);
        await _repository.SaveAsync(done, CancellationToken.None);

        var active = await _repository.FindActiveAsync(CancellationToken.None);
        Assert.Single(active);
        Assert.Equal(running.Id, active[0].Id);
    }

    [Fact]
    public async Task PurgeOlderThan_removes_only_old_ended_tasks()
    {
        var now = DateTimeOffset.UtcNow;
        var old = new DetectedTask(Guid.NewGuid(), "wmi", "old", 50, now.AddDays(-100), 1);
        old.Apply(TaskSignal.Started, CompletionConfidence.Unknown, now.AddDays(-100));
        old.Apply(TaskSignal.Succeeded, CompletionConfidence.ExitCodeConfirmed, now.AddDays(-99), 0);

        var recent = new DetectedTask(Guid.NewGuid(), "wmi", "recent", 50, now.AddHours(-1), 2);
        recent.Apply(TaskSignal.Started, CompletionConfidence.Unknown, now.AddHours(-1));
        recent.Apply(TaskSignal.Succeeded, CompletionConfidence.ExitCodeConfirmed, now.AddMinutes(-30), 0);

        await _repository.SaveAsync(old, CancellationToken.None);
        await _repository.SaveAsync(recent, CancellationToken.None);

        var purged = await _repository.PurgeOlderThanAsync(now.AddDays(-90), CancellationToken.None);

        Assert.Equal(1, purged);
        Assert.NotNull(await _repository.FindByIdAsync(recent.Id, CancellationToken.None));
        Assert.Null(await _repository.FindByIdAsync(old.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Initialize_is_idempotent()
    {
        // Initialize again on the same database should not throw and should not duplicate anything.
        await _database.InitializeAsync(CancellationToken.None);
        await _database.InitializeAsync(CancellationToken.None);

        var recent = await _repository.FindRecentAsync(10, CancellationToken.None);
        Assert.Empty(recent);
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* ignore */ }
        try { File.Delete(_dbPath + "-wal"); } catch { /* ignore */ }
        try { File.Delete(_dbPath + "-shm"); } catch { /* ignore */ }
    }
}
