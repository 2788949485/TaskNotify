using System.Text.RegularExpressions;

namespace TaskNotify.Core.Detection;

public static partial class CommandSanitizer
{
    [GeneratedRegex("""(?ix)(?<key>--?(?:password|passwd|token|api-key|apikey|secret|authorization|connection-string|database-url)|OPENAI_API_KEY|ANTHROPIC_API_KEY|DATABASE_URL)(?<separator>\s*(?:=|\s)\s*)(?<value>"[^"]*"|'[^']*'|\S+)""", RegexOptions.CultureInvariant, 100)]
    private static partial Regex SensitiveValue();

    public static string? Sanitize(string? command) => command is null
        ? null
        : SensitiveValue().Replace(command, match => $"{match.Groups["key"].Value}{match.Groups["separator"].Value}***");
}
