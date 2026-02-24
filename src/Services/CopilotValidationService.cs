using System.ComponentModel;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using PipelineConverter.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace PipelineConverter.Services;

/// <summary>
/// Service that uses GitHub Copilot SDK to validate GitHub Actions workflows.
/// </summary>
public sealed class CopilotValidationService
{
    private readonly TimeSpan _timeout;

    public CopilotValidationService(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    /// <summary>
    /// Validates a converted GitHub Actions workflow using the provided session.
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(
        CopilotSession session,
        PipelineInfo pipeline,
        string generatedWorkflow,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        // Local syntax + structure validation (fast, no session needed)
        var syntaxResult = ValidateYamlSyntax(generatedWorkflow);
        if (!syntaxResult.IsValid)
            issues.AddRange(syntaxResult.Issues);

        issues.AddRange(ValidateWorkflowStructure(generatedWorkflow));

        try
        {
            var prompt = BuildValidationPrompt(pipeline.OriginalContent, generatedWorkflow);
            var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = prompt }, _timeout);
            string responseContent = (string)(response?.Data?.Content ?? "");

            var (_, copilotIssues) = ParseCopilotValidation(responseContent);
            issues.AddRange(copilotIssues);

            return new ValidationResult
            {
                IsValid = !issues.Exists(i => i.Severity == ValidationSeverity.Error),
                Issues = issues,
                Suggestions = ExtractSuggestions(responseContent),
                ImprovedWorkflow = ExtractImprovedWorkflow(responseContent),
                ImprovementSummary = ExtractImprovementSummary(responseContent)
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
            
            ### Changes Made
            You MUST list every change you made to the workflow below. Be specific.
            - Description of change 1
            - Description of change 2
            
            ```yaml
            # The full improved workflow YAML
            ```
            
            IMPORTANT: If you provide an improved workflow, you MUST include a "### Changes Made" section with a bullet list describing each specific change. Do not skip this section.
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

    private static List<string>? ExtractImprovementSummary(string response)
    {
        // Look for "### Changes Made" section within "## Improved Workflow"
        var improvedIndex = response.IndexOf("## Improved Workflow", StringComparison.OrdinalIgnoreCase);
        if (improvedIndex == -1) return null;

        var afterImproved = response[improvedIndex..];
        var changesIndex = afterImproved.IndexOf("### Changes Made", StringComparison.OrdinalIgnoreCase);
        if (changesIndex == -1)
        {
            // Fallback: also look for "Changes Made" without ### or "### Changes"
            changesIndex = afterImproved.IndexOf("Changes Made", StringComparison.OrdinalIgnoreCase);
            if (changesIndex == -1) return null;
        }

        var changes = new List<string>();
        var lines = afterImproved[changesIndex..].Split('\n');
        var started = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip the header line itself
            if (trimmed.Contains("Changes Made", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("Changes", StringComparison.OrdinalIgnoreCase) && trimmed.StartsWith('#'))
            {
                started = true;
                continue;
            }

            if (!started) continue;

            // Stop at the next section header or code block
            if (trimmed.StartsWith("##") || trimmed.StartsWith("```"))
                break;

            if (trimmed.StartsWith("-") || trimmed.StartsWith("*"))
            {
                var text = trimmed.TrimStart('-', '*', ' ');
                if (!string.IsNullOrWhiteSpace(text))
                    changes.Add(text);
            }
        }

        return changes.Count > 0 ? changes : null;
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
