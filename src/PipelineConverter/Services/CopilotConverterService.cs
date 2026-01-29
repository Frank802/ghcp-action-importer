using GitHub.Copilot.SDK;
using PipelineConverter.Abstractions;
using PipelineConverter.Extensions;
using PipelineConverter.Models;

namespace PipelineConverter.Services;

/// <summary>
/// Service that uses GitHub Copilot SDK to convert pipelines to GitHub Actions.
/// </summary>
public class CopilotConverterService : IAsyncDisposable
{
    private readonly CopilotClient _client;
    private readonly string _model;
    private readonly CustomAgentConfig? _customAgent;
    private bool _isStarted;

    public CopilotConverterService(string model = "gpt-4.1", CustomAgentConfig? customAgent = null)
    {
        _client = new CopilotClient();
        _model = model;
        _customAgent = customAgent;
    }

    /// <summary>
    /// Creates a CopilotConverterService with a custom agent loaded from a markdown file.
    /// </summary>
    /// <param name="model">The model to use.</param>
    /// <param name="agentFilePath">Path to the agent markdown file.</param>
    /// <returns>A configured CopilotConverterService instance.</returns>
    public static CopilotConverterService WithAgentFromFile(string model, string agentFilePath)
    {
        var customAgent = CustomAgentConfigExtensions.FromMarkdownFile(agentFilePath);
        return new CopilotConverterService(model, customAgent);
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
    /// Converts a pipeline to GitHub Actions workflow using Copilot.
    /// </summary>
    public async Task<ConversionResult> ConvertAsync(PipelineInfo pipeline, CancellationToken cancellationToken = default)
    {
        if (!_isStarted)
        {
            await StartAsync(cancellationToken);
        }

        try
        {
            var sessionConfig = new SessionConfig { Model = _model };
            
            // Add custom agent if configured
            if (_customAgent is not null)
            {
                sessionConfig.CustomAgents = [_customAgent];
            }

            await using var session = await _client.CreateSessionAsync(sessionConfig, cancellationToken);

            var prompt = BuildConversionPrompt(pipeline);
            
            var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = prompt });
            var responseContent = response?.Data?.Content ?? "";

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
            // Try to extract without code blocks - look for 'name:' or 'on:' at start of line
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
                    if (string.IsNullOrWhiteSpace(line) && yamlLines.Count > 0 && 
                        !yamlLines[^1].TrimEnd().EndsWith(":"))
                    {
                        // Might be end of YAML
                        continue;
                    }
                    yamlLines.Add(line);
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

    private static string GenerateFileName(PipelineInfo pipeline)
    {
        var baseName = Path.GetFileNameWithoutExtension(pipeline.FilePath)
            .ToLowerInvariant()
            .Replace('.', '-')
            .Replace('_', '-');

        // Ensure it doesn't start with a dot
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

    public async ValueTask DisposeAsync()
    {
        if (_isStarted)
        {
            await _client.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }
}
