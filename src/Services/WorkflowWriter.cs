using PipelineConverter.Models;

namespace PipelineConverter.Services;

/// <summary>
/// Service for writing converted workflows to disk.
/// </summary>
public class WorkflowWriter
{
    private readonly string _outputDirectory;
    private readonly bool _createWorkflowsSubdir;

    /// <summary>
    /// Initializes a new WorkflowWriter.
    /// </summary>
    /// <param name="outputDirectory">The base output directory.</param>
    /// <param name="createWorkflowsSubdir">Whether to create a .github/workflows subdirectory.</param>
    public WorkflowWriter(string outputDirectory, bool createWorkflowsSubdir = true)
    {
        _outputDirectory = outputDirectory;
        _createWorkflowsSubdir = createWorkflowsSubdir;
    }

    /// <summary>
    /// Gets the target directory for workflows.
    /// </summary>
    public string WorkflowsDirectory => _createWorkflowsSubdir 
        ? Path.Combine(_outputDirectory, ".github", "workflows")
        : _outputDirectory;

    /// <summary>
    /// Writes a converted workflow to disk.
    /// </summary>
    /// <param name="result">The conversion result containing the workflow.</param>
    /// <param name="originalPipeline">The original pipeline info (for naming).</param>
    /// <returns>The path to the written workflow file.</returns>
    public async Task<string> WriteAsync(
        ConversionResult result, 
        PipelineInfo originalPipeline,
        CancellationToken cancellationToken = default)
    {
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.WorkflowYaml))
        {
            throw new InvalidOperationException("Cannot write a failed conversion result.");
        }

        // Ensure directory exists
        EnsureDirectoryExists();

        var fileName = result.SuggestedFileName ?? GenerateFileName(originalPipeline);
        var filePath = Path.Combine(WorkflowsDirectory, fileName);

        // Handle file conflicts
        filePath = GetUniqueFilePath(filePath);

        await File.WriteAllTextAsync(filePath, result.WorkflowYaml, cancellationToken);

        return filePath;
    }

    /// <summary>
    /// Writes a validation report alongside the workflow.
    /// </summary>
    public async Task<string> WriteValidationReportAsync(
        string workflowPath,
        ValidationResult validation,
        CancellationToken cancellationToken = default)
    {
        var reportPath = Path.ChangeExtension(workflowPath, ".validation.md");

        var content = BuildValidationReport(workflowPath, validation);
        await File.WriteAllTextAsync(reportPath, content, cancellationToken);

        return reportPath;
    }

    /// <summary>
    /// Writes an improved workflow if validation provided one.
    /// </summary>
    public async Task<string?> WriteImprovedWorkflowAsync(
        string originalWorkflowPath,
        ValidationResult validation,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(validation.ImprovedWorkflow))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(originalWorkflowPath)!;
        var fileName = Path.GetFileNameWithoutExtension(originalWorkflowPath);
        var extension = Path.GetExtension(originalWorkflowPath);
        
        var improvedPath = Path.Combine(directory, $"{fileName}.improved{extension}");
        improvedPath = GetUniqueFilePath(improvedPath);

        await File.WriteAllTextAsync(improvedPath, validation.ImprovedWorkflow, cancellationToken);

        return improvedPath;
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(WorkflowsDirectory))
        {
            Directory.CreateDirectory(WorkflowsDirectory);
        }
    }

    private static string GenerateFileName(PipelineInfo pipeline)
    {
        var baseName = Path.GetFileNameWithoutExtension(pipeline.FilePath)
            .ToLowerInvariant()
            .Replace('.', '-')
            .Replace('_', '-')
            .Replace(' ', '-');

        // Clean up the name
        baseName = string.Join("-", baseName.Split('-', StringSplitOptions.RemoveEmptyEntries));

        if (string.IsNullOrWhiteSpace(baseName) || baseName.Equals("jenkinsfile", StringComparison.OrdinalIgnoreCase))
        {
            baseName = $"{pipeline.SourceType.ToString().ToLowerInvariant()}-pipeline";
        }

        return $"{baseName}.yml";
    }

    private static string GetUniqueFilePath(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return filePath;
        }

        var directory = Path.GetDirectoryName(filePath)!;
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);

        var counter = 1;
        string newPath;
        do
        {
            newPath = Path.Combine(directory, $"{fileName}-{counter}{extension}");
            counter++;
        } while (File.Exists(newPath));

        return newPath;
    }

    private static string BuildValidationReport(string workflowPath, ValidationResult validation)
    {
        var builder = new System.Text.StringBuilder();

        builder.AppendLine($"# Validation Report");
        builder.AppendLine();
        builder.AppendLine($"**Workflow:** `{Path.GetFileName(workflowPath)}`");
        builder.AppendLine($"**Status:** {(validation.IsValid ? "✅ Valid" : "❌ Has Issues")}");
        builder.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        builder.AppendLine();

        if (validation.Issues.Count > 0)
        {
            builder.AppendLine("## Issues");
            builder.AppendLine();

            var errors = validation.Issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
            var warnings = validation.Issues.Where(i => i.Severity == ValidationSeverity.Warning).ToList();
            var infos = validation.Issues.Where(i => i.Severity == ValidationSeverity.Info).ToList();

            if (errors.Count > 0)
            {
                builder.AppendLine("### Errors");
                foreach (var issue in errors)
                {
                    var line = issue.LineNumber.HasValue ? $" (Line {issue.LineNumber})" : "";
                    builder.AppendLine($"- ❌ {issue.Message}{line}");
                    if (!string.IsNullOrEmpty(issue.Suggestion))
                    {
                        builder.AppendLine($"  - 💡 {issue.Suggestion}");
                    }
                }
                builder.AppendLine();
            }

            if (warnings.Count > 0)
            {
                builder.AppendLine("### Warnings");
                foreach (var issue in warnings)
                {
                    var line = issue.LineNumber.HasValue ? $" (Line {issue.LineNumber})" : "";
                    builder.AppendLine($"- ⚠️ {issue.Message}{line}");
                    if (!string.IsNullOrEmpty(issue.Suggestion))
                    {
                        builder.AppendLine($"  - 💡 {issue.Suggestion}");
                    }
                }
                builder.AppendLine();
            }

            if (infos.Count > 0)
            {
                builder.AppendLine("### Info");
                foreach (var issue in infos)
                {
                    builder.AppendLine($"- ℹ️ {issue.Message}");
                }
                builder.AppendLine();
            }
        }

        if (validation.Suggestions?.Count > 0)
        {
            builder.AppendLine("## Suggestions for Improvement");
            builder.AppendLine();
            foreach (var suggestion in validation.Suggestions)
            {
                builder.AppendLine($"- {suggestion}");
            }
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(validation.ImprovedWorkflow))
        {
            builder.AppendLine("## Improved Workflow Available");
            builder.AppendLine();
            builder.AppendLine("An improved version of the workflow has been generated. Check the `.improved.yml` file.");
        }

        return builder.ToString();
    }
}
