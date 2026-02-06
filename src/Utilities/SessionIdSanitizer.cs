using System.Text.RegularExpressions;

namespace PipelineConverter.Utilities;

/// <summary>
/// Utility for sanitizing strings for use in session IDs and other identifiers.
/// Session IDs must contain only alphanumeric characters and hyphens.
/// </summary>
public static partial class SessionIdSanitizer
{
    // Cached regex patterns for better performance
    private static readonly Regex InvalidCharsRegex = CreateInvalidCharsRegex();
    private static readonly Regex MultipleHyphensRegex = CreateMultipleHyphensRegex();

    [GeneratedRegex(@"[^a-zA-Z0-9\-]")]
    private static partial Regex CreateInvalidCharsRegex();

    [GeneratedRegex(@"-+")]
    private static partial Regex CreateMultipleHyphensRegex();

    /// <summary>
    /// Sanitizes a name for use in a session ID (alphanumeric and hyphens only).
    /// </summary>
    /// <param name="name">The name to sanitize.</param>
    /// <returns>A sanitized string safe for use in session IDs.</returns>
    public static string SanitizeSessionId(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "unnamed";
        }

        // Replace dots and underscores with hyphens, remove other invalid chars
        var sanitized = name
            .Replace('.', '-')
            .Replace('_', '-')
            .Replace(' ', '-');
        
        // Remove any characters that aren't alphanumeric or hyphens
        sanitized = InvalidCharsRegex.Replace(sanitized, string.Empty);
        
        // Remove leading hyphens and collapse multiple hyphens
        sanitized = MultipleHyphensRegex.Replace(sanitized, "-").Trim('-');
        
        // Ensure it's not empty
        return string.IsNullOrEmpty(sanitized) ? "unnamed" : sanitized.ToLowerInvariant();
    }
}
