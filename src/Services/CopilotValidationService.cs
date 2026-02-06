using System.ComponentModel;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using PipelineConverter.Extensions;
using PipelineConverter.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace PipelineConverter.Services;

/// <summary>
/// Service that uses GitHub Copilot SDK to validate GitHub Actions workflows.
/// Can be used standalone or within an existing Copilot session.
/// </summary>
public sealed class CopilotValidationService : CopilotServiceBase
{
    private readonly List<AIFunction> _tools;

    /// <summary>
    /// Creates a standalone validation service with its own Copilot client.
    /// </summary>
    public CopilotValidationService(string model = "gpt-4.1", int timeoutSeconds = 120, CustomAgentConfig? customAgent = null)
        : base(model, timeoutSeconds, customAgent)
    {
        _tools = CreateValidationTools();
    }

    /// <summary>
    /// Creates a validation service that uses an external client (for session reuse).
    /// </summary>
    public CopilotValidationService(TimeSpan timeout, CustomAgentConfig? customAgent = null)
        : base(timeout, customAgent)
    {
        _tools = CreateValidationTools();
    }

    /// <summary>
    /// Creates a CopilotValidationService with a custom agent loaded from a markdown file.
    /// </summary>
    public static CopilotValidationService WithAgentFromFile(string model, int timeoutSeconds, string agentFilePath)
    {
        return CreateWithAgentFromFile(model, timeoutSeconds, agentFilePath,
            (m, t, a) => new CopilotValidationService(m, t, a));
    }

    /// <summary>
    /// Validates a converted GitHub Actions workflow using a new session.
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(
        string originalPipeline,
        string generatedWorkflow,
        CancellationToken cancellationToken = default)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Standalone validation requires a CopilotClient. Use ValidateInSessionAsync for session-based validation.");
        }

        if (!_isStarted)
        {
            await StartAsync(cancellationToken);
        }

        var issues = new List<ValidationIssue>();

        // First, perform local syntax validation
        var syntaxResult = ValidateYamlSyntax(generatedWorkflow);
        if (!syntaxResult.IsValid)
        {
            issues.AddRange(syntaxResult.Issues);
        }

        // Check for common GitHub Actions structure issues
        var structureIssues = ValidateWorkflowStructure(generatedWorkflow);
        issues.AddRange(structureIssues);

        try
        {
            // Use Copilot for semantic validation and improvements
            var sessionConfig = new SessionConfig 
            { 
                Model = _model,
                Tools = _tools 
            };

            if (CustomAgent is not null)
            {
                sessionConfig.CustomAgents = [CustomAgent];
            }

            await using var session = await _client.CreateSessionAsync(sessionConfig, cancellationToken);

            var prompt = BuildValidationPrompt(originalPipeline, generatedWorkflow);
            var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = prompt }, _timeout);
            var responseContent = response?.Data?.Content ?? string.Empty;

            // Parse Copilot's feedback
            var (_, copilotIssues) = ParseCopilotValidation(responseContent);
            issues.AddRange(copilotIssues);

            var suggestions = ExtractSuggestions(responseContent);
            var improvedWorkflow = ExtractImprovedWorkflow(responseContent);

            return new ValidationResult
            {
                IsValid = !issues.Exists(i => i.Severity == ValidationSeverity.Error),
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
                IsValid = !issues.Exists(i => i.Severity == ValidationSeverity.Error),
                Issues = issues
            };
        }
    }

    /// <summary>
    /// Validates a converted GitHub Actions workflow within an existing session.
    /// This maintains conversation context from the conversion step.
    /// </summary>
    public async Task<ValidationResult> ValidateInSessionAsync(
        CopilotSession session,
        string originalPipeline,
        string generatedWorkflow,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        // First, perform local syntax validation
        var syntaxResult = ValidateYamlSyntax(generatedWorkflow);
        if (!syntaxResult.IsValid)
        {
            issues.AddRange(syntaxResult.Issues);
        }

        // Check for common GitHub Actions structure issues
        var structureIssues = ValidateWorkflowStructure(generatedWorkflow);
        issues.AddRange(structureIssues);

        try
        {
            // Use existing session for semantic validation (maintains context from conversion)
            var prompt = BuildValidationPrompt(originalPipeline, generatedWorkflow);
            var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = prompt }, _timeout);
            var responseContent = response?.Data?.Content ?? string.Empty;

            // Parse Copilot's feedback
            var (_, copilotIssues) = ParseCopilotValidation(responseContent);
            issues.AddRange(copilotIssues);

            var suggestions = ExtractSuggestions(responseContent);
            var improvedWorkflow = ExtractImprovedWorkflow(responseContent);

            return new ValidationResult
            {
                IsValid = !issues.Exists(i => i.Severity == ValidationSeverity.Error),
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
                IsValid = !issues.Exists(i => i.Severity == ValidationSeverity.Error),
                Issues = issues
            };
        }
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

        // Check for potentially dangerous patterns
        if (yaml.Contains("${{") && yaml.Contains("github.event."))
        {
            if (yaml.Contains("github.event.issue.title") || 
                yaml.Contains("github.event.issue.body") ||
                yaml.Contains("github.event.pull_request.title") ||
                yaml.Contains("github.event.pull_request.body"))
            {
                issues.Add("Potential command injection: User-controlled input used in expression. Consider sanitizing.");
            }
        }

        if (yaml.Contains("GITHUB_TOKEN") && yaml.Contains("write"))
        {
            issues.Add("Workflow uses GITHUB_TOKEN with write permissions. Ensure this is necessary.");
        }

        if (yaml.Contains("pull_request_target"))
        {
            issues.Add("Uses pull_request_target trigger which can be security-sensitive. Ensure proper handling.");
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
                
                // Check if using @main, @master, or no version
                if (action.EndsWith("@main") || action.EndsWith("@master"))
                {
                    issues.Add($"Line {i + 1}: Action '{action}' uses unstable branch. Consider pinning to a version.");
                }
                else if (!action.Contains('@'))
                {
                    issues.Add($"Line {i + 1}: Action '{action}' has no version specified.");
                }
            }
        }

        return issues.Count > 0 ? string.Join("\n", issues) : "All actions use pinned versions.";
    }

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

        // Try to parse YAML and check top-level keys properly
        try
        {
            var deserializer = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build();
            
            var workflow = deserializer.Deserialize<Dictionary<object, object>>(yaml);
            
            if (workflow == null || workflow.Count == 0)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = "Workflow YAML is empty or invalid"
                });
                return issues;
            }

            // Check for required top-level keys
            if (!workflow.ContainsKey("on"))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = "Workflow missing required 'on:' trigger definition"
                });
            }

            if (!workflow.ContainsKey("jobs"))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = "Workflow missing required 'jobs:' section"
                });
            }
            
            // Check for runs-on in jobs
            if (workflow.ContainsKey("jobs") && workflow["jobs"] is Dictionary<object, object> jobs)
            {
                var hasRunsOn = jobs.Values
                    .OfType<Dictionary<object, object>>()
                    .Any(job => job.ContainsKey("runs-on"));
                
                if (!hasRunsOn)
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = "Jobs should specify 'runs-on:' to define the runner"
                    });
                }
            }
        }
        catch (Exception)
        {
            // Fall back to string matching if YAML parsing fails
            if (!yaml.Contains("on:") && !yaml.Contains("on "))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = "Workflow missing required 'on:' trigger definition"
                });
            }

            if (!yaml.Contains("jobs:") && !yaml.Contains("jobs "))
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
        }

        return issues;
    }

    private static string BuildValidationPrompt(string originalPipeline, string generatedWorkflow)
    {
        return $"""
            You are a GitHub Actions expert reviewing a converted workflow. Analyze the generated workflow for:

            1. **Correctness**: Does it accurately represent the original pipeline's logic?
            2. **Best Practices**: Does it follow GitHub Actions best practices?
            3. **Security**: Are there any security concerns?
            4. **Efficiency**: Can it be optimized?

            Use the available tools to validate syntax, check security, and verify action versions.

            Original Pipeline:
            ```
            {originalPipeline}
            ```

            Generated GitHub Actions Workflow:
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

    private static (bool IsValid, List<ValidationIssue> Issues) ParseCopilotValidation(string response)
    {
        var issues = new List<ValidationIssue>();

        var lines = response.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            if (trimmed.StartsWith("- [ERROR]", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("-[ERROR]", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = ExtractIssueMessage(trimmed)
                });
            }
            else if (trimmed.StartsWith("- [WARNING]", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("-[WARNING]", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Message = ExtractIssueMessage(trimmed)
                });
            }
            else if (trimmed.StartsWith("- [INFO]", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("-[INFO]", StringComparison.OrdinalIgnoreCase))
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
        // Remove the prefix like "- [ERROR]: " or similar
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
        // Look for improved workflow section
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
        
        // Don't return if it's just a placeholder comment
        if (yaml.StartsWith("#") && yaml.Split('\n').Length < 3)
        {
            return null;
        }

        return yaml;
    }
}
