namespace PipelineConverter.Models;

/// <summary>
/// Contains the result of converting a pipeline to GitHub Actions.
/// </summary>
public record ConversionResult
{
    /// <summary>
    /// Gets whether the conversion was successful.
    /// </summary>
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// Gets the converted GitHub Actions workflow YAML.
    /// </summary>
    public string? WorkflowYaml { get; init; }

    /// <summary>
    /// Gets the suggested workflow filename.
    /// </summary>
    public string? SuggestedFileName { get; init; }

    /// <summary>
    /// Gets the error message if conversion failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets notes or warnings from the conversion process.
    /// </summary>
    public IReadOnlyList<string>? Notes { get; init; }

    /// <summary>
    /// Creates a successful conversion result.
    /// </summary>
    public static ConversionResult Success(string workflowYaml, string suggestedFileName, IReadOnlyList<string>? notes = null) => new()
    {
        IsSuccess = true,
        WorkflowYaml = workflowYaml,
        SuggestedFileName = suggestedFileName,
        Notes = notes
    };

    /// <summary>
    /// Creates a failed conversion result.
    /// </summary>
    public static ConversionResult Failed(string errorMessage) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage
    };
}
