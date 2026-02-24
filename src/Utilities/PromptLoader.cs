namespace PipelineConverter.Utilities;

/// <summary>
/// Loads prompt text from markdown files in the Prompts directory.
/// </summary>
public static class PromptLoader
{
    /// <summary>
    /// Loads a prompt file's content as a string.
    /// Validates that the resolved path is under the application base directory.
    /// </summary>
    public static async Task<string> LoadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(AppContext.BaseDirectory, relativePath);

        var normalizedBasePath = Path.GetFullPath(AppContext.BaseDirectory);
        var normalizedFullPath = Path.GetFullPath(fullPath);

        if (!normalizedFullPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Prompt file path '{relativePath}' resolves outside the application directory.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Prompt file not found: {relativePath}", fullPath);
        }

        return await File.ReadAllTextAsync(fullPath, cancellationToken);
    }
}
