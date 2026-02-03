namespace PipelineConverter.Abstractions;

/// <summary>
/// Represents the type of CI/CD pipeline source.
/// </summary>
public enum PipelineType
{
    GitLab,
    AzureDevOps,
    Jenkins
}

/// <summary>
/// Interface for pipeline source handlers that detect and extract information from different CI/CD pipeline formats.
/// </summary>
public interface IPipelineSource
{
    /// <summary>
    /// Gets the type of pipeline this source handles.
    /// </summary>
    PipelineType Type { get; }

    /// <summary>
    /// Gets the file patterns this source can handle (e.g., ".gitlab-ci.yml", "Jenkinsfile").
    /// </summary>
    IReadOnlyList<string> FilePatterns { get; }

    /// <summary>
    /// Determines if this source can handle the given file based on its path and/or content.
    /// </summary>
    /// <param name="filePath">The path to the pipeline file.</param>
    /// <param name="content">The content of the file (optional, for content-based detection).</param>
    /// <returns>True if this source can handle the file; otherwise, false.</returns>
    bool CanHandle(string filePath, string? content = null);

    /// <summary>
    /// Extracts pipeline information from the file content.
    /// </summary>
    /// <param name="filePath">The path to the pipeline file.</param>
    /// <param name="content">The content of the pipeline file.</param>
    /// <returns>A PipelineInfo object containing the extracted information.</returns>
    Models.PipelineInfo ExtractInfo(string filePath, string content);
}
