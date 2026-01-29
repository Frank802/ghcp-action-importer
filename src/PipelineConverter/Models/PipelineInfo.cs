using PipelineConverter.Abstractions;

namespace PipelineConverter.Models;

/// <summary>
/// Contains information about a pipeline file to be converted.
/// </summary>
public record PipelineInfo
{
    /// <summary>
    /// Gets the name of the pipeline (derived from filename or content).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the type of pipeline source.
    /// </summary>
    public required PipelineType SourceType { get; init; }

    /// <summary>
    /// Gets the original content of the pipeline file.
    /// </summary>
    public required string OriginalContent { get; init; }

    /// <summary>
    /// Gets the path to the original pipeline file.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Gets additional metadata extracted from the pipeline (optional).
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}
