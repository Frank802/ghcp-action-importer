namespace PipelineConverter.Models;

/// <summary>
/// Represents an issue found during validation.
/// </summary>
public record ValidationIssue
{
    /// <summary>
    /// Gets the severity of the issue.
    /// </summary>
    public required ValidationSeverity Severity { get; init; }

    /// <summary>
    /// Gets the description of the issue.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the line number where the issue was found (if applicable).
    /// </summary>
    public int? LineNumber { get; init; }

    /// <summary>
    /// Gets a suggested fix for the issue (if available).
    /// </summary>
    public string? Suggestion { get; init; }
}

/// <summary>
/// Severity levels for validation issues.
/// </summary>
public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Contains the result of validating a converted workflow.
/// </summary>
public record ValidationResult
{
    /// <summary>
    /// Gets whether the validation passed without errors.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets the list of issues found during validation.
    /// </summary>
    public required IReadOnlyList<ValidationIssue> Issues { get; init; }

    /// <summary>
    /// Gets suggestions for improving the workflow.
    /// </summary>
    public IReadOnlyList<string>? Suggestions { get; init; }

    /// <summary>
    /// Gets the improved workflow content (if modifications were suggested).
    /// </summary>
    public string? ImprovedWorkflow { get; init; }

    /// <summary>
    /// Creates a successful validation result with no issues.
    /// </summary>
    public static ValidationResult Success() => new()
    {
        IsValid = true,
        Issues = []
    };

    /// <summary>
    /// Creates a failed validation result with the specified issues.
    /// </summary>
    public static ValidationResult Failed(IReadOnlyList<ValidationIssue> issues) => new()
    {
        IsValid = false,
        Issues = issues
    };
}
