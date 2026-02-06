using GitHub.Copilot.SDK;
using PipelineConverter.Abstractions;
using PipelineConverter.Extensions;
using PipelineConverter.Models;
using PipelineConverter.Utilities;

namespace PipelineConverter.Services;

/// <summary>
/// Service that uses GitHub Copilot SDK to convert pipelines to GitHub Actions.
/// Can be used standalone or within an existing Copilot session.
/// </summary>
public sealed class CopilotConverterService : CopilotServiceBase
{
    /// <summary>
    /// Creates a standalone converter service with its own Copilot client.
    /// </summary>
    public CopilotConverterService(string model = "gpt-4.1", int timeoutSeconds = 120, CustomAgentConfig? customAgent = null)
        : base(model, timeoutSeconds, customAgent)
    {
    }

    /// <summary>
    /// Creates a converter service that uses an external client (for session reuse).
    /// </summary>
    public CopilotConverterService(TimeSpan timeout, CustomAgentConfig? customAgent = null)
        : base(timeout, customAgent)
    {
    }

    /// <summary>
    /// Creates a CopilotConverterService with a custom agent loaded from a markdown file.
    /// </summary>
    /// <param name="model">The model to use.</param>
    /// <param name="timeoutSeconds">Timeout in seconds for API calls.</param>
    /// <param name="agentFilePath">Path to the agent markdown file.</param>
    /// <returns>A configured CopilotConverterService instance.</returns>
    public static CopilotConverterService WithAgentFromFile(string model, int timeoutSeconds, string agentFilePath)
    {
        return CreateWithAgentFromFile(model, timeoutSeconds, agentFilePath, 
            (m, t, a) => new CopilotConverterService(m, t, a));
    }

    /// <summary>
    /// Converts a pipeline to GitHub Actions workflow using Copilot (creates new session).
    /// </summary>
    public async Task<ConversionResult> ConvertAsync(PipelineInfo pipeline, CancellationToken cancellationToken = default)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Standalone conversion requires a CopilotClient. Use ConvertInSessionAsync for session-based conversion.");
        }

        if (!_isStarted)
        {
            await StartAsync(cancellationToken);
        }

        try
        {
            var sessionConfig = new SessionConfig { Model = _model };
            
            // Add custom agent if configured
            if (CustomAgent is not null)
            {
                sessionConfig.CustomAgents = [CustomAgent];
            }

            await using var session = await _client.CreateSessionAsync(sessionConfig, cancellationToken);

            var prompt = BuildConversionPrompt(pipeline);
            
            var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = prompt }, _timeout);
            var responseContent = response?.Data?.Content ?? string.Empty;

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

    /// <summary>
    /// Converts a pipeline to GitHub Actions workflow within an existing session.
    /// This allows the session to be reused for subsequent validation.
    /// </summary>
    public async Task<ConversionResult> ConvertInSessionAsync(
        CopilotSession session,
        PipelineInfo pipeline,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = BuildConversionPrompt(pipeline);
            var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = prompt }, _timeout);
            var responseContent = response?.Data?.Content ?? string.Empty;

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
