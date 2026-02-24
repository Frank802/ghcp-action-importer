using System.Text;
using PipelineConverter.Models;
using PipelineConverter.Utilities;

namespace PipelineConverter.Services;

/// <summary>
/// Service for writing converted workflows to disk.
/// </summary>
public sealed class WorkflowWriter
{
    private readonly string _outputDirectory;
    private readonly bool _createWorkflowsSubdir;
    private readonly Lock _fileWriteLock = new();

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

        var fileName = result.SuggestedFileName ?? FileNameGenerator.GenerateWorkflowFileName(originalPipeline);
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
    /// Writes an analysis report for a pipeline.
    /// </summary>
    public async Task<string> WriteAnalysisReportAsync(
        PipelineInfo pipeline,
        AnalysisResult analysis,
        CancellationToken cancellationToken = default)
    {
        EnsureDirectoryExists();

        var fileName = FileNameGenerator.GenerateWorkflowFileName(pipeline);
        var basePath = Path.Combine(WorkflowsDirectory, fileName);
        var reportPath = Path.ChangeExtension(basePath, ".analysis.md");

        var content = BuildAnalysisReport(pipeline, analysis);
        await File.WriteAllTextAsync(reportPath, content, cancellationToken);

        return reportPath;
    }

    /// <summary>
    /// Writes the validated/improved workflow to a separate "-validated" file.
    /// </summary>
    public async Task<string> WriteValidatedAsync(
        string workflowPath,
        ValidationResult validation,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(workflowPath)!;
        var fileName = Path.GetFileNameWithoutExtension(workflowPath);
        var extension = Path.GetExtension(workflowPath);
        var validatedPath = Path.Combine(directory, $"{fileName}-validated{extension}");

        await File.WriteAllTextAsync(validatedPath, validation.ImprovedWorkflow, cancellationToken);
        return validatedPath;
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(WorkflowsDirectory))
        {
            Directory.CreateDirectory(WorkflowsDirectory);
        }
    }

    private string GetUniqueFilePath(string filePath)
    {
        lock (_fileWriteLock)
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
    }

    private static string BuildAnalysisReport(PipelineInfo pipeline, AnalysisResult analysis)
    {
        var builder = new StringBuilder();

        builder.AppendLine("# Pipeline Analysis Report");
        builder.AppendLine();
        builder.AppendLine($"**Pipeline:** `{pipeline.Name}`");
        builder.AppendLine($"**Source Type:** {pipeline.SourceType}");
        builder.AppendLine($"**Complexity:** {analysis.ComplexityScore}");
        builder.AppendLine($"**Can Proceed:** {(analysis.CanProceed ? "\u2705 Yes" : "\u274c Blocked")}");
        builder.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(analysis.Summary))
        {
            builder.AppendLine("## Summary");
            builder.AppendLine();
            builder.AppendLine(analysis.Summary);
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(analysis.ComplexityJustification))
        {
            builder.AppendLine("## Complexity Justification");
            builder.AppendLine();
            builder.AppendLine(analysis.ComplexityJustification);
            builder.AppendLine();
        }

        if (analysis.StructureBreakdown?.Count > 0)
        {
            builder.AppendLine("## Pipeline Structure");
            builder.AppendLine();
            foreach (var item in analysis.StructureBreakdown)
            {
                builder.AppendLine($"- {item}");
            }
            builder.AppendLine();
        }

        if (analysis.RiskItems?.Count > 0)
        {
            builder.AppendLine("## Risks");
            builder.AppendLine();

            var errors = analysis.RiskItems.Where(r => r.Severity == ValidationSeverity.Error).ToList();
            var warnings = analysis.RiskItems.Where(r => r.Severity == ValidationSeverity.Warning).ToList();
            var infos = analysis.RiskItems.Where(r => r.Severity == ValidationSeverity.Info).ToList();

            if (errors.Count > 0)
            {
                builder.AppendLine("### Errors");
                foreach (var risk in errors)
                {
                    builder.AppendLine($"- \u274c **{risk.Category}**: {risk.Description}");
                    if (!string.IsNullOrEmpty(risk.Mitigation))
                        builder.AppendLine($"  - \ud83d\udca1 {risk.Mitigation}");
                }
                builder.AppendLine();
            }

            if (warnings.Count > 0)
            {
                builder.AppendLine("### Warnings");
                foreach (var risk in warnings)
                {
                    builder.AppendLine($"- \u26a0\ufe0f **{risk.Category}**: {risk.Description}");
                    if (!string.IsNullOrEmpty(risk.Mitigation))
                        builder.AppendLine($"  - \ud83d\udca1 {risk.Mitigation}");
                }
                builder.AppendLine();
            }

            if (infos.Count > 0)
            {
                builder.AppendLine("### Info");
                foreach (var risk in infos)
                {
                    builder.AppendLine($"- \u2139\ufe0f **{risk.Category}**: {risk.Description}");
                }
                builder.AppendLine();
            }
        }

        if (analysis.UnsupportedFeatures?.Count > 0)
        {
            builder.AppendLine("## Unsupported Features");
            builder.AppendLine();
            builder.AppendLine("The following features have no direct GitHub Actions equivalent and may require manual workarounds:");
            builder.AppendLine();
            foreach (var feature in analysis.UnsupportedFeatures)
            {
                builder.AppendLine($"- {feature}");
            }
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(analysis.EstimatedEffort))
        {
            builder.AppendLine("## Estimated Effort");
            builder.AppendLine();
            builder.AppendLine(analysis.EstimatedEffort);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildValidationReport(string workflowPath, ValidationResult validation)
    {
        var builder = new StringBuilder();

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
            builder.AppendLine("## Improvements Applied");
            builder.AppendLine();
            builder.AppendLine("The following improvements have been applied to the output workflow file:");
            builder.AppendLine();

            if (validation.ImprovementSummary?.Count > 0)
            {
                foreach (var change in validation.ImprovementSummary)
                {
                    builder.AppendLine($"- {change}");
                }
            }
            else if (validation.Suggestions?.Count > 0)
            {
                foreach (var suggestion in validation.Suggestions)
                {
                    builder.AppendLine($"- {suggestion}");
                }
            }
            else if (validation.Issues.Count > 0)
            {
                // Derive summary from issues that have suggestions
                var actionableIssues = validation.Issues
                    .Where(i => !string.IsNullOrEmpty(i.Suggestion))
                    .Select(i => i.Suggestion!)
                    .ToList();
                
                if (actionableIssues.Count > 0)
                {
                    foreach (var fix in actionableIssues)
                    {
                        builder.AppendLine($"- {fix}");
                    }
                }
                else
                {
                    // Last resort: summarize from issue messages
                    foreach (var issue in validation.Issues.Where(i => i.Severity != ValidationSeverity.Info).Take(10))
                    {
                        builder.AppendLine($"- Fixed: {issue.Message}");
                    }
                }
            }
            else
            {
                builder.AppendLine("- General improvements based on validation findings.");
            }
        }

        return builder.ToString();
    }
}
