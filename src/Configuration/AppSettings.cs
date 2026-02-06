namespace PipelineConverter.Configuration;

/// <summary>
/// Root configuration for the Pipeline Converter application.
/// </summary>
public class AppSettings
{
    public PathsSettings Paths { get; set; } = new();
    public CopilotSettings Copilot { get; set; } = new();
    public ConversionSettings Conversion { get; set; } = new();
    public ValidationSettings Validation { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
}

/// <summary>
/// Settings for input/output paths.
/// </summary>
public class PathsSettings
{
    /// <summary>
    /// Default input directory containing pipeline files to convert.
    /// Can be overridden by command line -i/--input.
    /// </summary>
    public string InputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Default output directory for converted workflows.
    /// Can be overridden by command line -o/--output.
    /// </summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Filter to specific source type: GitLab, AzureDevOps, Jenkins.
    /// Leave empty to process all sources. Can be overridden by -s/--source.
    /// </summary>
    public string SourceFilter { get; set; } = string.Empty;
}

/// <summary>
/// Settings for GitHub Copilot SDK.
/// </summary>
public class CopilotSettings
{
    /// <summary>
    /// The model to use for conversion and validation (e.g., "gpt-4.1", "claude-sonnet-4.5").
    /// </summary>
    public string Model { get; set; } = "gpt-4.1";

    /// <summary>
    /// Timeout in seconds for Copilot operations.
    /// </summary>
    public int Timeout { get; set; } = 120;

    /// <summary>
    /// Maximum number of parallel Copilot sessions for concurrent pipeline processing.
    /// Each pipeline gets its own session for conversion and validation.
    /// </summary>
    public int MaxParallelSessions { get; set; } = 3;

    /// <summary>
    /// Path to the custom agent markdown file for pipeline conversion.
    /// Relative to the application base directory.
    /// </summary>
    public string ConverterAgentFile { get; set; } = "Agents/pipeline-converter.md";

    /// <summary>
    /// Path to the custom agent markdown file for workflow validation.
    /// Relative to the application base directory.
    /// </summary>
    public string ValidatorAgentFile { get; set; } = "Agents/workflow-validator.md";
}

/// <summary>
/// Settings for the conversion process.
/// </summary>
public class ConversionSettings
{
    /// <summary>
    /// Whether to create .github/workflows subdirectory in output.
    /// </summary>
    public bool CreateWorkflowsSubdirectory { get; set; } = true;

    /// <summary>
    /// Whether to generate validation report files.
    /// </summary>
    public bool GenerateValidationReports { get; set; } = true;

    /// <summary>
    /// Whether to generate improved workflow files when suggestions are available.
    /// </summary>
    public bool GenerateImprovedWorkflows { get; set; } = true;
}

/// <summary>
/// Settings for workflow validation.
/// </summary>
public class ValidationSettings
{
    /// <summary>
    /// Whether to check YAML syntax.
    /// </summary>
    public bool CheckSyntax { get; set; } = true;

    /// <summary>
    /// Whether to check for security issues.
    /// </summary>
    public bool CheckSecurity { get; set; } = true;

    /// <summary>
    /// Whether to verify action versions are pinned.
    /// </summary>
    public bool CheckActionVersions { get; set; } = true;

    /// <summary>
    /// Maximum number of issues to display in console output.
    /// </summary>
    public int MaxIssuesInConsole { get; set; } = 5;
}

/// <summary>
/// Logging settings.
/// </summary>
public class LoggingSettings
{
    /// <summary>
    /// Enable verbose logging output.
    /// </summary>
    public bool Verbose { get; set; } = false;
}
