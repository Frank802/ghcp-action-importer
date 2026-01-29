using System.ComponentModel;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using PipelineConverter.Abstractions;
using PipelineConverter.Configuration;
using PipelineConverter.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace PipelineConverter.Services;

/// <summary>
/// Result of processing a single pipeline through conversion and validation.
/// </summary>
public record PipelineProcessingResult
{
    public required PipelineInfo Pipeline { get; init; }
    public required ConversionResult Conversion { get; init; }
    public ValidationResult? Validation { get; init; }
    public string? WorkflowPath { get; init; }
    public string? ValidationReportPath { get; init; }
    public string? ImprovedWorkflowPath { get; init; }
    public TimeSpan Duration { get; init; }
    public Exception? Error { get; init; }
}

/// <summary>
/// Progress callback for pipeline processing.
/// </summary>
public enum ProcessingPhase
{
    Starting,
    Converting,
    ConversionComplete,
    Validating,
    ValidationComplete,
    Writing,
    Complete,
    Failed
}

public record ProcessingProgress(
    PipelineInfo Pipeline,
    ProcessingPhase Phase,
    string? Message = null);

/// <summary>
/// Parallel pipeline processor using multiple Copilot sessions.
/// Each pipeline gets its own session for conversion and validation.
/// </summary>
public class ParallelPipelineProcessor : IAsyncDisposable
{
    private readonly CopilotClient _client;
    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _sessionSemaphore;
    private readonly TimeSpan _timeout;
    private readonly List<AIFunction> _validationTools;
    private bool _isStarted;

    public ParallelPipelineProcessor(AppSettings settings)
    {
        _settings = settings;
        _client = new CopilotClient();
        _sessionSemaphore = new SemaphoreSlim(settings.Copilot.MaxParallelSessions);
        _timeout = TimeSpan.FromSeconds(settings.Copilot.Timeout);
        _validationTools = CreateValidationTools();
    }

    /// <summary>
    /// Starts the Copilot client connection.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isStarted) return;
        
        await _client.StartAsync(cancellationToken);
        _isStarted = true;
    }

    /// <summary>
    /// Processes multiple pipelines in parallel with configurable concurrency.
    /// </summary>
    public async Task<List<PipelineProcessingResult>> ProcessAsync(
        IReadOnlyList<PipelineInfo> pipelines,
        WorkflowWriter writer,
        bool skipValidation,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_isStarted)
        {
            await StartAsync(cancellationToken);
        }

        var tasks = pipelines.Select(pipeline => 
            ProcessPipelineAsync(pipeline, writer, skipValidation, progress, cancellationToken));

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    /// <summary>
    /// Processes a single pipeline in its own session.
    /// </summary>
    private async Task<PipelineProcessingResult> ProcessPipelineAsync(
        PipelineInfo pipeline,
        WorkflowWriter writer,
        bool skipValidation,
        IProgress<ProcessingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        
        // Wait for a session slot
        await _sessionSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Starting));

            // Create a session for this pipeline with a unique ID
            // Sanitize name to only contain alphanumeric and hyphens (no dots, spaces, etc.)
            var sanitizedName = SanitizeSessionId(pipeline.Name);
            var sessionConfig = new SessionConfig
            {
                SessionId = $"pipeline-{sanitizedName}-{Guid.NewGuid():N}",
                Model = _settings.Copilot.Model
            };

            await using var session = await _client.CreateSessionAsync(sessionConfig, cancellationToken);

            // Phase 1: Conversion
            progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Converting));
            
            var conversionResult = await ConvertInSessionAsync(session, pipeline, cancellationToken);
            
            if (!conversionResult.IsSuccess)
            {
                progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Failed, conversionResult.ErrorMessage));
                return new PipelineProcessingResult
                {
                    Pipeline = pipeline,
                    Conversion = conversionResult,
                    Duration = DateTime.UtcNow - startTime
                };
            }

            progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.ConversionComplete));

            // Write the workflow file
            progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Writing));
            var workflowPath = await writer.WriteAsync(conversionResult, pipeline, cancellationToken);

            ValidationResult? validationResult = null;
            string? validationReportPath = null;
            string? improvedWorkflowPath = null;

            // Phase 2: Validation (in same session, maintains context)
            if (!skipValidation)
            {
                progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Validating));
                
                validationResult = await ValidateInSessionAsync(
                    session, 
                    pipeline.OriginalContent, 
                    conversionResult.WorkflowYaml!, 
                    cancellationToken);

                progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.ValidationComplete));

                // Write validation report
                if (_settings.Conversion.GenerateValidationReports)
                {
                    validationReportPath = await writer.WriteValidationReportAsync(
                        workflowPath, validationResult, cancellationToken);
                }

                // Write improved workflow
                if (_settings.Conversion.GenerateImprovedWorkflows)
                {
                    improvedWorkflowPath = await writer.WriteImprovedWorkflowAsync(
                        workflowPath, validationResult, cancellationToken);
                }
            }

            progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Complete));

            return new PipelineProcessingResult
            {
                Pipeline = pipeline,
                Conversion = conversionResult,
                Validation = validationResult,
                WorkflowPath = workflowPath,
                ValidationReportPath = validationReportPath,
                ImprovedWorkflowPath = improvedWorkflowPath,
                Duration = DateTime.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Failed, ex.Message));
            
            return new PipelineProcessingResult
            {
                Pipeline = pipeline,
                Conversion = ConversionResult.Failed($"Processing failed: {ex.Message}"),
                Duration = DateTime.UtcNow - startTime,
                Error = ex
            };
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    /// <summary>
    /// Performs conversion within an existing session.
    /// </summary>
    private async Task<ConversionResult> ConvertInSessionAsync(
        dynamic session,
        PipelineInfo pipeline,
        CancellationToken cancellationToken)
    {
        try
        {
            var prompt = BuildConversionPrompt(pipeline);
            var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = prompt }, _timeout);
            string responseContent = (string)(response?.Data?.Content ?? "");

            var workflowYaml = ExtractYamlFromResponse(responseContent);
            
            if (string.IsNullOrWhiteSpace(workflowYaml))
            {
                return ConversionResult.Failed("Failed to extract valid GitHub Actions workflow from response.");
            }

            var suggestedFileName = GenerateFileName(pipeline);
            var notes = ExtractNotesFromResponse(responseContent);

            return ConversionResult.Success(workflowYaml, suggestedFileName, notes);
        }
        catch (Exception ex)
        {
            return ConversionResult.Failed($"Conversion failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Performs validation within an existing session (maintains conversation context).
    /// </summary>
    private async Task<ValidationResult> ValidateInSessionAsync(
        dynamic session,
        string originalPipeline,
        string generatedWorkflow,
        CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();

        // Local syntax validation
        var syntaxResult = ValidateYamlSyntax(generatedWorkflow);
        if (!syntaxResult.IsValid)
        {
            issues.AddRange(syntaxResult.Issues);
        }

        // Structure validation
        var structureIssues = ValidateWorkflowStructure(generatedWorkflow);
        issues.AddRange(structureIssues);

        try
        {
            // AI validation using same session (has context from conversion)
            var prompt = BuildValidationPrompt(originalPipeline, generatedWorkflow);
            var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = prompt }, _timeout);
            string responseContent = (string)(response?.Data?.Content ?? "");

            var validationResult = ParseCopilotValidation(responseContent);
            issues.AddRange(validationResult.Item2);

            var suggestions = ExtractSuggestions(responseContent);
            var improvedWorkflow = ExtractImprovedWorkflow(responseContent);

            return new ValidationResult
            {
                IsValid = !issues.Any(i => i.Severity == ValidationSeverity.Error),
                Issues = issues,
                Suggestions = suggestions,
                ImprovedWorkflow = improvedWorkflow
            };
        }
        catch (Exception ex)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Message = $"Could not complete AI validation: {ex.Message}"
            });

            return new ValidationResult
            {
                IsValid = !issues.Any(i => i.Severity == ValidationSeverity.Error),
                Issues = issues
            };
        }
    }

    /// <summary>
    /// Sanitizes a name for use in a session ID (alphanumeric and hyphens only).
    /// </summary>
    private static string SanitizeSessionId(string name)
    {
        // Replace dots and underscores with hyphens, remove other invalid chars
        var sanitized = name
            .Replace('.', '-')
            .Replace('_', '-')
            .Replace(' ', '-');
        
        // Remove any characters that aren't alphanumeric or hyphens
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[^a-zA-Z0-9\-]", "");
        
        // Remove leading hyphens and collapse multiple hyphens
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"-+", "-").Trim('-');
        
        // Ensure it's not empty
        return string.IsNullOrEmpty(sanitized) ? "unnamed" : sanitized.ToLowerInvariant();
    }

    #region Prompt Building

    private static string BuildConversionPrompt(PipelineInfo pipeline)
    {
        var sourceType = pipeline.SourceType switch
        {
            PipelineType.GitLab => "GitLab CI/CD (.gitlab-ci.yml)",
            PipelineType.AzureDevOps => "Azure DevOps (azure-pipelines.yml)",
            PipelineType.Jenkins => "Jenkins (Jenkinsfile)",
            _ => "CI/CD pipeline"
        };

        return $"""
            You are an expert in CI/CD pipeline migration. Convert the following {sourceType} pipeline to a GitHub Actions workflow.

            Requirements:
            1. Produce a valid GitHub Actions workflow YAML file
            2. Map all stages/jobs to appropriate GitHub Actions jobs
            3. Convert environment variables to GitHub Actions format
            4. Use appropriate GitHub Actions (e.g., actions/checkout@v4, actions/setup-node@v4)
            5. Preserve the original pipeline's logic and flow
            6. Add helpful comments where the mapping is not 1:1
            7. Use modern GitHub Actions best practices

            Source Pipeline ({pipeline.Name}):
            ```
            {pipeline.OriginalContent}
            ```

            Respond with ONLY the GitHub Actions workflow YAML, wrapped in ```yaml code blocks.
            After the YAML, you may add brief notes about any manual adjustments needed.
            """;
    }

    private static string BuildValidationPrompt(string originalPipeline, string generatedWorkflow)
    {
        return $"""
            Now validate the GitHub Actions workflow you just generated. Analyze it for:

            1. **Correctness**: Does it accurately represent the original pipeline's logic?
            2. **Best Practices**: Does it follow GitHub Actions best practices?
            3. **Security**: Are there any security concerns?
            4. **Efficiency**: Can it be optimized?

            Original Pipeline (for reference):
            ```
            {originalPipeline}
            ```

            Generated Workflow:
            ```yaml
            {generatedWorkflow}
            ```

            Provide your analysis in this format:
            
            ## Issues Found
            - [ERROR/WARNING/INFO]: Description (Line X if applicable)
            
            ## Suggestions
            - Suggestion 1
            - Suggestion 2
            
            ## Improved Workflow (if changes recommended)
            ```yaml
            # Only include if you have improvements
            ```
            """;
    }

    #endregion

    #region Response Parsing

    private static string? ExtractYamlFromResponse(string response)
    {
        const string yamlStart = "```yaml";
        const string altYamlStart = "```yml";
        const string codeEnd = "```";

        var startIndex = response.IndexOf(yamlStart, StringComparison.OrdinalIgnoreCase);
        if (startIndex == -1)
        {
            startIndex = response.IndexOf(altYamlStart, StringComparison.OrdinalIgnoreCase);
        }

        if (startIndex == -1)
        {
            var lines = response.Split('\n');
            var yamlLines = new List<string>();
            var inYaml = false;

            foreach (var line in lines)
            {
                if (!inYaml && (line.TrimStart().StartsWith("name:") || line.TrimStart().StartsWith("on:")))
                {
                    inYaml = true;
                }
                
                if (inYaml)
                {
                    yamlLines.Add(line);
                }
            }

            return yamlLines.Count > 0 ? string.Join('\n', yamlLines) : null;
        }

        var contentStart = response.IndexOf('\n', startIndex) + 1;
        var endIndex = response.IndexOf(codeEnd, contentStart);

        if (endIndex == -1)
        {
            return response[contentStart..].Trim();
        }

        return response[contentStart..endIndex].Trim();
    }

    private static List<string>? ExtractNotesFromResponse(string response)
    {
        const string codeEnd = "```";
        var lastCodeBlock = response.LastIndexOf(codeEnd, StringComparison.OrdinalIgnoreCase);
        
        if (lastCodeBlock == -1 || lastCodeBlock + codeEnd.Length >= response.Length)
        {
            return null;
        }

        var notesSection = response[(lastCodeBlock + codeEnd.Length)..].Trim();
        
        if (string.IsNullOrWhiteSpace(notesSection))
        {
            return null;
        }

        var notes = notesSection
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(n => n.Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        return notes.Count > 0 ? notes : null;
    }

    private static string GenerateFileName(PipelineInfo pipeline)
    {
        var baseName = Path.GetFileNameWithoutExtension(pipeline.FilePath)
            .ToLowerInvariant()
            .Replace('.', '-')
            .Replace('_', '-');

        if (baseName.StartsWith('-'))
        {
            baseName = baseName.TrimStart('-');
        }

        if (string.IsNullOrWhiteSpace(baseName) || baseName == "jenkinsfile")
        {
            baseName = pipeline.SourceType.ToString().ToLowerInvariant();
        }

        return $"{baseName}.yml";
    }

    private static (bool IsValid, List<ValidationIssue> Issues) ParseCopilotValidation(string response)
    {
        var issues = new List<ValidationIssue>();

        var lines = response.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            if (trimmed.StartsWith("- [ERROR]", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = ExtractIssueMessage(trimmed)
                });
            }
            else if (trimmed.StartsWith("- [WARNING]", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Message = ExtractIssueMessage(trimmed)
                });
            }
            else if (trimmed.StartsWith("- [INFO]", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Info,
                    Message = ExtractIssueMessage(trimmed)
                });
            }
        }

        var hasErrors = issues.Any(i => i.Severity == ValidationSeverity.Error);
        return (!hasErrors, issues);
    }

    private static string ExtractIssueMessage(string line)
    {
        var colonIndex = line.IndexOf(':');
        if (colonIndex > 0 && colonIndex < line.Length - 1)
        {
            return line[(colonIndex + 1)..].Trim();
        }
        
        var bracketEnd = line.IndexOf(']');
        if (bracketEnd > 0 && bracketEnd < line.Length - 1)
        {
            return line[(bracketEnd + 1)..].Trim().TrimStart(':').Trim();
        }

        return line.TrimStart('-', ' ');
    }

    private static List<string>? ExtractSuggestions(string response)
    {
        var suggestions = new List<string>();
        var inSuggestions = false;

        foreach (var line in response.Split('\n'))
        {
            var trimmed = line.Trim();
            
            if (trimmed.StartsWith("## Suggestions", StringComparison.OrdinalIgnoreCase))
            {
                inSuggestions = true;
                continue;
            }
            
            if (inSuggestions && trimmed.StartsWith("##"))
            {
                break;
            }

            if (inSuggestions && trimmed.StartsWith("-"))
            {
                suggestions.Add(trimmed.TrimStart('-', ' '));
            }
        }

        return suggestions.Count > 0 ? suggestions : null;
    }

    private static string? ExtractImprovedWorkflow(string response)
    {
        var improvedIndex = response.IndexOf("## Improved Workflow", StringComparison.OrdinalIgnoreCase);
        if (improvedIndex == -1) return null;

        var afterSection = response[improvedIndex..];
        
        const string yamlStart = "```yaml";
        const string codeEnd = "```";

        var startIndex = afterSection.IndexOf(yamlStart, StringComparison.OrdinalIgnoreCase);
        if (startIndex == -1) return null;

        var contentStart = afterSection.IndexOf('\n', startIndex) + 1;
        var endIndex = afterSection.IndexOf(codeEnd, contentStart);

        if (endIndex == -1) return null;

        var yaml = afterSection[contentStart..endIndex].Trim();
        
        if (yaml.StartsWith("#") && yaml.Split('\n').Length < 3)
        {
            return null;
        }

        return yaml;
    }

    #endregion

    #region Local Validation

    private static ValidationResult ValidateYamlSyntax(string yaml)
    {
        var issues = new List<ValidationIssue>();

        try
        {
            var deserializer = new DeserializerBuilder().Build();
            deserializer.Deserialize<object>(yaml);
        }
        catch (YamlException ex)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = $"YAML syntax error: {ex.Message}",
                LineNumber = (int)ex.Start.Line
            });
        }
        catch (Exception ex)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = $"Failed to parse YAML: {ex.Message}"
            });
        }

        return new ValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues
        };
    }

    private static List<ValidationIssue> ValidateWorkflowStructure(string yaml)
    {
        var issues = new List<ValidationIssue>();

        if (!yaml.Contains("on:") && !yaml.Contains("on :"))
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = "Workflow missing required 'on:' trigger definition"
            });
        }

        if (!yaml.Contains("jobs:") && !yaml.Contains("jobs :"))
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = "Workflow missing required 'jobs:' section"
            });
        }

        if (yaml.Contains("jobs:") && !yaml.Contains("runs-on:"))
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Message = "Jobs should specify 'runs-on:' to define the runner"
            });
        }

        return issues;
    }

    private List<AIFunction> CreateValidationTools()
    {
        return
        [
            AIFunctionFactory.Create(ValidateSyntaxTool, "validate_yaml_syntax",
                "Validates the YAML syntax of a GitHub Actions workflow"),
            AIFunctionFactory.Create(CheckSecurityIssuesTool, "check_security",
                "Checks for common security issues in a GitHub Actions workflow"),
            AIFunctionFactory.Create(ValidateActionVersionsTool, "validate_action_versions",
                "Validates that actions use pinned versions")
        ];
    }

    [Description("Validates YAML syntax and returns any parsing errors")]
    private static string ValidateSyntaxTool([Description("The YAML content to validate")] string yaml)
    {
        var result = ValidateYamlSyntax(yaml);
        if (result.IsValid)
        {
            return "YAML syntax is valid.";
        }
        return string.Join("\n", result.Issues.Select(i => $"Line {i.LineNumber}: {i.Message}"));
    }

    [Description("Checks for security issues like command injection, secret exposure")]
    private static string CheckSecurityIssuesTool([Description("The workflow YAML to check")] string yaml)
    {
        var issues = new List<string>();

        if (yaml.Contains("${{") && yaml.Contains("github.event."))
        {
            if (yaml.Contains("github.event.issue.title") || 
                yaml.Contains("github.event.issue.body") ||
                yaml.Contains("github.event.pull_request.title") ||
                yaml.Contains("github.event.pull_request.body"))
            {
                issues.Add("Potential command injection: User-controlled input used in expression.");
            }
        }

        if (yaml.Contains("GITHUB_TOKEN") && yaml.Contains("write"))
        {
            issues.Add("Workflow uses GITHUB_TOKEN with write permissions. Ensure this is necessary.");
        }

        if (yaml.Contains("pull_request_target"))
        {
            issues.Add("Uses pull_request_target trigger which can be security-sensitive.");
        }

        return issues.Count > 0 ? string.Join("\n", issues) : "No obvious security issues found.";
    }

    [Description("Checks if GitHub Actions use pinned versions")]
    private static string ValidateActionVersionsTool([Description("The workflow YAML to check")] string yaml)
    {
        var issues = new List<string>();
        var lines = yaml.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("uses:"))
            {
                var action = line["uses:".Length..].Trim();
                
                if (action.EndsWith("@main") || action.EndsWith("@master"))
                {
                    issues.Add($"Line {i + 1}: Action '{action}' uses unstable branch.");
                }
                else if (!action.Contains('@'))
                {
                    issues.Add($"Line {i + 1}: Action '{action}' has no version specified.");
                }
            }
        }

        return issues.Count > 0 ? string.Join("\n", issues) : "All actions use pinned versions.";
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        _sessionSemaphore.Dispose();
        if (_isStarted)
        {
            await _client.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }
}
