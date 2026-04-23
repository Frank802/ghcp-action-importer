using GitHub.Copilot.SDK;
using PipelineConverter.Abstractions;
using PipelineConverter.Models;
using PipelineConverter.Utilities;

namespace PipelineConverter.Services;

/// <summary>
/// Service that uses GitHub Copilot SDK to convert pipelines to GitHub Actions.
/// </summary>
public sealed class CopilotConverterService
{
    private readonly TimeSpan _timeout;
    private readonly string _agentName;

    public CopilotConverterService(TimeSpan timeout, string agentName)
    {
        _timeout = timeout;
        _agentName = agentName;
    }

    /// <summary>
    /// Converts a pipeline to a GitHub Actions workflow using the provided session.
    /// </summary>
    public async Task<ConversionResult> ConvertAsync(
        CopilotSession session,
        PipelineInfo pipeline,
        AnalysisResult? analysis = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await session.Rpc.Agent.SelectAsync(_agentName);
            var prompt = BuildConversionPrompt(pipeline, analysis);
            var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = prompt }, _timeout);
            string responseContent = (string)(response?.Data?.Content ?? "");

            var workflowYaml = ExtractYamlFromResponse(responseContent);

            if (string.IsNullOrWhiteSpace(workflowYaml))
            {
                return ConversionResult.Failed("Failed to extract valid GitHub Actions workflow from response.");
            }

            var suggestedFileName = FileNameGenerator.GenerateWorkflowFileName(pipeline);
            var notes = ExtractNotesFromResponse(responseContent);

            return ConversionResult.Success(workflowYaml, suggestedFileName, notes);
        }
        catch (Exception ex)
        {
            return ConversionResult.Failed($"Conversion failed: {ex.Message}");
        }
    }

    private string BuildConversionPrompt(PipelineInfo pipeline, AnalysisResult? analysis = null)
    {
        var sourceType = pipeline.SourceType switch
        {
            PipelineType.GitLab => "GitLab CI/CD (.gitlab-ci.yml)",
            PipelineType.AzureDevOps => "Azure DevOps (azure-pipelines.yml)",
            PipelineType.Jenkins => "Jenkins (Jenkinsfile)",
            _ => "CI/CD pipeline"
        };

        var prompt = $"""
            Convert the following {sourceType} pipeline to a GitHub Actions workflow.

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
            """;

        // Inject analysis findings so the converter addresses identified risks
        if (analysis is { IsSuccess: true })
        {
            prompt += $"\n\n    Pre-conversion analysis results (complexity: {analysis.ComplexityScore}):";

            if (analysis.RiskItems?.Count > 0)
            {
                prompt += "\n    Identified risks:";
                foreach (var risk in analysis.RiskItems)
                {
                    prompt += $"\n    - [{risk.Severity}] {risk.Category}: {risk.Description}";
                    if (!string.IsNullOrWhiteSpace(risk.Mitigation))
                        prompt += $" (mitigation: {risk.Mitigation})";
                }
            }

            if (analysis.UnsupportedFeatures?.Count > 0)
            {
                prompt += "\n    Unsupported features requiring workarounds:";
                foreach (var feature in analysis.UnsupportedFeatures)
                {
                    prompt += $"\n    - {feature}";
                }
            }

            prompt += "\n\n    Pay special attention to the risks and unsupported features listed above.";
            prompt += "\n    Add TODO comments in the workflow where manual attention is needed for these items.";
        }

        prompt += "\n\n    Respond with ONLY the GitHub Actions workflow YAML, wrapped in ```yaml code blocks.";
        prompt += "\n    After the YAML, you may add brief notes about any manual adjustments needed.";

        return prompt;
    }

    private static string? ExtractYamlFromResponse(string response)
    {
        // Extract YAML from markdown code blocks
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
            // Fallback: Extract YAML without code fences
            // This is a heuristic approach for malformed responses where the model didn't wrap YAML in code blocks.
            // Look for lines starting with typical workflow keys (name:, on:) to detect the start.
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
                    // Heuristic: Continue collecting lines as long as they look like YAML.
                    // Stop when we encounter a line that looks like prose (non-indented, non-key-value, non-comment).
                    var trimmed = line.TrimStart();
                    
                    // Empty lines are allowed in YAML (especially between sections)
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        yamlLines.Add(line);
                        continue;
                    }
                    
                    // Lines that look like YAML: keys, lists, comments, or indented content
                    if (trimmed.Contains(':') || 
                        trimmed.StartsWith('-') || 
                        trimmed.StartsWith('#') || 
                        line.StartsWith(" ") || 
                        line.StartsWith("\t"))
                    {
                        yamlLines.Add(line);
                    }
                    else if (yamlLines.Count > 0)
                    {
                        // We've hit a line that doesn't look like YAML after collecting some YAML.
                        // This is likely the end of the YAML block.
                        break;
                    }
                }
            }

            return yamlLines.Count > 0 ? string.Join('\n', yamlLines) : null;
        }

        // Find the end of the code block
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
        // Look for notes after the YAML block
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
}
