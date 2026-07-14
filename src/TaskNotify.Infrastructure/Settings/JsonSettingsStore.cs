using System.Text;
using System.Text.Json;

namespace TaskNotify.Infrastructure.Settings;

/// <summary>
/// Atomic JSON settings file reader/writer. Reads+parses, lets caller mutate,
/// writes to temp file then moves into place (atomic on Windows for same-drive moves).
///
/// Default location: <c>%LOCALAPPDATA%\TaskNotify\settings.json</c>.
/// </summary>
public sealed class JsonSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;

    public JsonSettingsStore(string? customPath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(customPath) ? DefaultPath() : customPath;
    }

    public string FilePath => _filePath;

    /// <summary>Loads settings, or returns a new instance if the file does not exist / fails to parse.</summary>
    public T Load<T>() where T : new()
    {
        try
        {
            if (!File.Exists(_filePath)) return new T();
            using var stream = File.OpenRead(_filePath);
            return JsonSerializer.Deserialize<T>(stream, Options) ?? new T();
        }
        catch
        {
            // Bad JSON / IO error: fall back to defaults rather than crash the app.
            return new T();
        }
    }

    /// <summary>Saves settings atomically (temp file + move). Throws on IO errors.</summary>
    public void Save<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var tempPath = _filePath + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, value, Options);
            }
            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>Loads current settings, applies the mutation, and saves.</summary>
    public void Mutate<T>(Action<T> mutator) where T : new()
    {
        ArgumentNullException.ThrowIfNull(mutator);
        var current = Load<T>();
        mutator(current);
        Save(current);
    }

    public static string DefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "TaskNotify", "settings.json");
    }
}
