using System.Text.Json;
using TaskNotify.Core.Detection;
using TaskNotify.Infrastructure.Settings;
using Xunit;

namespace TaskNotify.Core.Tests;

/// <summary>
/// Phase 2.5: verifies that user settings flow from disk through AppSettingsStore,
/// that the three detection modes produce materially different rule outcomes,
/// and that SnapshotProcessMonitor exposes the expected candidate shortlist per mode.
/// </summary>
public sealed class SettingsAndDetectionModeTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsAndDetectionModeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tasknotify-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Missing_settings_file_yields_defaults()
    {
        var path = Path.Combine(_tempDir, "missing.json");
        var store = new AppSettingsStore(new JsonSettingsStore(path));

        var current = store.Current;
        Assert.Equal(DetectionMode.Balanced, current.DetectionMode);
        Assert.NotEmpty(current.EnabledSources);
        Assert.True(current.EnabledSources["claude"]);
    }

    [Fact]
    public void Mutate_persists_and_updates_in_memory_snapshot()
    {
        var path = Path.Combine(_tempDir, "active.json");
        var store = new AppSettingsStore(new JsonSettingsStore(path));

        store.Mutate(s => s.DetectionMode = DetectionMode.Broad);

        Assert.Equal(DetectionMode.Broad, store.Current.DetectionMode);

        // A new store on the same file should see the persisted value.
        var reopened = new AppSettingsStore(new JsonSettingsStore(path));
        Assert.Equal(DetectionMode.Broad, reopened.Current.DetectionMode);
    }

    [Fact]
    public void Atomic_write_leaves_valid_json_even_when_temp_file_present()
    {
        var path = Path.Combine(_tempDir, "atomic.json");
        var store = new JsonSettingsStore(path);

        store.Save(new AppSettings { DetectionMode = DetectionMode.Precise });

        // File must be valid JSON (no half-written content). Use the same camelCase
        // policy that JsonSettingsStore uses for both directions.
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        using var stream = File.OpenRead(path);
        var deserialized = JsonSerializer.Deserialize<AppSettings>(stream, options);
        Assert.NotNull(deserialized);
        Assert.Equal(DetectionMode.Precise, deserialized!.DetectionMode);

        // No leftover temp file after a successful save.
        var leftovers = Directory.GetFiles(_tempDir, "atomic.json.tmp.*");
        Assert.Empty(leftovers);
    }

    [Fact]
    public void Precise_rule_set_ignores_python_from_gateway()
    {
        var candidate = new ProcessCandidate(
            10,
            DateTimeOffset.UtcNow,
            "python.exe",
            CommandLine: "python train.py",
            ParentProcessName: "Gateway.exe");

        var result = new DetectionRuleEngine().Evaluate(
            candidate,
            TimeSpan.FromSeconds(25),
            BuiltInDetectionRules.Precise);

        Assert.Equal(TaskProbability.Ignored, result.Probability);
    }

    [Fact]
    public void Balanced_rule_set_keeps_python_as_candidate()
    {
        var candidate = new ProcessCandidate(
            10,
            DateTimeOffset.UtcNow,
            "python.exe",
            CommandLine: "python train.py",
            ParentProcessName: "Gateway.exe");

        var result = new DetectionRuleEngine().Evaluate(
            candidate,
            TimeSpan.FromSeconds(25),
            BuiltInDetectionRules.Balanced);

        Assert.NotEqual(TaskProbability.Ignored, result.Probability);
    }

    [Fact]
    public void Broad_rule_set_recognises_java_msbuild_and_native_compilers()
    {
        var java = new ProcessCandidate(1, DateTimeOffset.UtcNow, "java.exe", CommandLine: "java -jar app.jar");
        var msbuild = new ProcessCandidate(2, DateTimeOffset.UtcNow, "msbuild.exe", CommandLine: "msbuild /t:Build");
        var cl = new ProcessCandidate(3, DateTimeOffset.UtcNow, "cl.exe", CommandLine: "cl main.cpp");

        foreach (var candidate in new[] { java, msbuild, cl })
        {
            var result = new DetectionRuleEngine().Evaluate(
                candidate,
                TimeSpan.FromSeconds(60),
                BuiltInDetectionRules.Broad);
            Assert.NotEqual(TaskProbability.Ignored, result.Probability);
        }
    }

    [Fact]
    public void Balanced_rule_set_ignores_java()
    {
        var java = new ProcessCandidate(1, DateTimeOffset.UtcNow, "java.exe", CommandLine: "java -jar app.jar");

        var result = new DetectionRuleEngine().Evaluate(
            java,
            TimeSpan.FromSeconds(60),
            BuiltInDetectionRules.Balanced);

        Assert.Equal(TaskProbability.Ignored, result.Probability);
    }

    [Fact]
    public void For_mode_resolves_to_the_expected_rule_set()
    {
        Assert.Same(BuiltInDetectionRules.Precise, BuiltInDetectionRules.For(DetectionMode.Precise));
        Assert.Same(BuiltInDetectionRules.Balanced, BuiltInDetectionRules.For(DetectionMode.Balanced));
        Assert.Same(BuiltInDetectionRules.Broad, BuiltInDetectionRules.For(DetectionMode.Broad));
    }

    [Fact]
    public void SnapshotProcessMonitor_broad_includes_java_msbuild_and_native_compilers()
    {
        var monitor = new TaskNotify.ProcessMonitor.SnapshotProcessMonitor(DetectionMode.Broad);

        // We can't read _candidateNames directly, but we can confirm via reflection
        // that the broad set was constructed. (Avoids spinning up a real session.)
        var field = typeof(TaskNotify.ProcessMonitor.SnapshotProcessMonitor)
            .GetField("_candidateNames", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        var set = (HashSet<string>)field!.GetValue(monitor)!;

        Assert.Contains("java.exe", set);
        Assert.Contains("msbuild.exe", set);
        Assert.Contains("cl.exe", set);
        Assert.Contains("python.exe", set);
    }

    [Fact]
    public void SnapshotProcessMonitor_precise_has_no_candidates()
    {
        var monitor = new TaskNotify.ProcessMonitor.SnapshotProcessMonitor(DetectionMode.Precise);
        var field = typeof(TaskNotify.ProcessMonitor.SnapshotProcessMonitor)
            .GetField("_candidateNames", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var set = (HashSet<string>)field!.GetValue(monitor)!;

        Assert.Empty(set);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }
}
