namespace TaskNotify.Core.Interfaces;

/// <summary>
/// Tracks install state of external integrations (Claude/Codex/Hermes/PowerShell/VSCode).
/// </summary>
public interface IIntegrationRepository
{
    Task<IReadOnlyList<IntegrationRecord>> LoadAllAsync(CancellationToken cancellationToken = default);

    Task<IntegrationRecord?> FindByTypeAsync(string integrationType, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates an integration row by type.</summary>
    Task UpsertAsync(IntegrationRecord record, CancellationToken cancellationToken = default);
}

/// <summary>One row in the Integrations table.</summary>
public sealed record IntegrationRecord(
    string IntegrationType,
    bool IsInstalled,
    bool IsEnabled,
    string? Version,
    string? ConfigPath,
    DateTimeOffset? LastCheckedAt);
