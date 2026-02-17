namespace PipelineConverter.Models;

/// <summary>
/// Complexity level of a pipeline for conversion to GitHub Actions.
/// </summary>
public enum ComplexityLevel
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// A risk identified during pipeline analysis.
/// </summary>
public record AnalysisRisk
{
    /// <summary>
    /// Severity of the risk (Error, Warning, Info).
    /// </summary>
    public required ValidationSeverity Severity { get; init; }

    /// <summary>
    /// Category of the risk (e.g., "Unsupported Feature", "Security", "Platform-Specific").
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Description of the risk.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Suggested mitigation or workaround.
    /// </summary>
    public string? Mitigation { get; init; }
}

/// <summary>
/// Contains the result of analyzing a pipeline before conversion.
/// </summary>
public record AnalysisResult
{
    /// <summary>
    /// Gets whether the analysis completed successfully.
    /// </summary>
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// Gets whether conversion should proceed. False when critical/unconvertible issues are found.
    /// </summary>
    public required bool CanProceed { get; init; }

    /// <summary>
    /// Gets the overall complexity level of the pipeline.
    /// </summary>
    public ComplexityLevel ComplexityScore { get; init; }

    /// <summary>
    /// Gets a justification for the complexity score.
    /// </summary>
    public string? ComplexityJustification { get; init; }

    /// <summary>
    /// Gets the structured breakdown of the pipeline (stages, jobs, triggers, etc.).
    /// </summary>
    public IReadOnlyList<string>? StructureBreakdown { get; init; }

    /// <summary>
    /// Gets the list of identified risks.
    /// </summary>
    public IReadOnlyList<AnalysisRisk>? RiskItems { get; init; }

    /// <summary>
    /// Gets features with no direct GitHub Actions equivalent.
    /// </summary>
    public IReadOnlyList<string>? UnsupportedFeatures { get; init; }

    /// <summary>
    /// Gets an estimated effort summary for the conversion.
    /// </summary>
    public string? EstimatedEffort { get; init; }

    /// <summary>
    /// Gets the full raw analysis text from Copilot.
    /// </summary>
    public string? RawAnalysis { get; init; }

    /// <summary>
    /// Gets the error message if analysis failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful analysis result.
    /// </summary>
    public static AnalysisResult Success(
        ComplexityLevel complexity,
        string? complexityJustification,
        IReadOnlyList<string>? structureBreakdown,
        IReadOnlyList<AnalysisRisk>? riskItems,
        IReadOnlyList<string>? unsupportedFeatures,
        string? estimatedEffort,
        string rawAnalysis) => new()
    {
        IsSuccess = true,
        CanProceed = true,
        ComplexityScore = complexity,
        ComplexityJustification = complexityJustification,
        StructureBreakdown = structureBreakdown,
        RiskItems = riskItems,
        UnsupportedFeatures = unsupportedFeatures,
        EstimatedEffort = estimatedEffort,
        RawAnalysis = rawAnalysis
    };

    /// <summary>
    /// Creates an analysis result that blocks conversion due to critical issues.
    /// </summary>
    public static AnalysisResult Blocked(
        string reason,
        IReadOnlyList<AnalysisRisk>? riskItems,
        IReadOnlyList<string>? unsupportedFeatures,
        string rawAnalysis) => new()
    {
        IsSuccess = true,
        CanProceed = false,
        ComplexityScore = ComplexityLevel.Critical,
        ComplexityJustification = reason,
        RiskItems = riskItems,
        UnsupportedFeatures = unsupportedFeatures,
        RawAnalysis = rawAnalysis
    };

    /// <summary>
    /// Creates a failed analysis result (analysis itself failed).
    /// </summary>
    public static AnalysisResult Failed(string errorMessage) => new()
    {
        IsSuccess = false,
        CanProceed = true, // Don't block conversion if analysis fails
        ErrorMessage = errorMessage
    };
}
