namespace TaskNotify.Desktop.Models;

/// <summary>
/// One row in the left navigation menu. Pages marked <see cref="IsPlaceholder"/>
/// render a "coming soon" panel — Phase 5 fills them in.
/// </summary>
public sealed record AppMenuItem(
    string Title,
    string Glyph,
    Type? ViewModelType = null,
    bool IsPlaceholder = false);
