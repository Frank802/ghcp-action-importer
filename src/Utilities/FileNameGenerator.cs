using PipelineConverter.Models;

namespace PipelineConverter.Utilities;

/// <summary>
/// Utility for generating workflow file names from pipeline information.
/// </summary>
public static class FileNameGenerator
{
    /// <summary>
    /// Generates a safe, descriptive workflow filename from pipeline information.
    /// </summary>
    /// <param name="pipeline">The pipeline information.</param>
    /// <returns>A lowercase filename with .yml extension.</returns>
    public static string GenerateWorkflowFileName(PipelineInfo pipeline)
    {
        var baseName = Path.GetFileNameWithoutExtension(pipeline.FilePath)
            .ToLowerInvariant()
            .Replace('.', '-')
            .Replace('_', '-')
            .Replace(' ', '-');

        // Collapse multiple consecutive dashes
        baseName = string.Join("-", baseName.Split('-', StringSplitOptions.RemoveEmptyEntries));

        // Handle Jenkinsfile or empty names
        if (string.IsNullOrWhiteSpace(baseName) || baseName.Equals("jenkinsfile", StringComparison.OrdinalIgnoreCase))
        {
            baseName = $"{pipeline.SourceType.ToString().ToLowerInvariant()}-pipeline";
        }

        return $"{baseName}.yml";
    }
}
