using TaskNotify.Core.Detection;

namespace TaskNotify.Core.Interfaces;

/// <summary>
/// Persistence contract for detection rules (built-in and user-created).
/// </summary>
public interface IDetectionRuleRepository
{
    /// <summary>Returns all enabled rules, user-created first.</summary>
    Task<IReadOnlyList<DetectionRule>> LoadEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns user-created rules only.</summary>
    Task<IReadOnlyList<DetectionRule>> LoadUserRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates a user rule by name.</summary>
    Task SaveUserRuleAsync(DetectionRule rule, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a user rule, or updates the existing one with the same <see cref="DetectionRule.Name"/>.
    /// Idempotent — safe to call repeatedly for the same learning action.
    /// </summary>
    Task UpsertUserRuleByNameAsync(DetectionRule rule, CancellationToken cancellationToken = default);

    /// <summary>Deletes a user rule by name. Returns true if a row was removed.</summary>
    Task<bool> DeleteUserRuleAsync(string name, CancellationToken cancellationToken = default);
}
