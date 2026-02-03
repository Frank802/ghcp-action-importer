using GitHub.Copilot.SDK;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PipelineConverter.Extensions;

/// <summary>
/// Extension methods for CustomAgentConfig to support loading from markdown files.
/// </summary>
public static class CustomAgentConfigExtensions
{
    private const string FrontMatterDelimiter = "---";

    /// <summary>
    /// Creates a CustomAgentConfig from a markdown file with YAML front matter.
    /// </summary>
    /// <param name="filePath">Path to the markdown file.</param>
    /// <returns>A configured CustomAgentConfig instance.</returns>
    /// <remarks>
    /// Expected markdown format:
    /// <code>
    /// ---
    /// name: my-agent
    /// displayName: My Custom Agent
    /// description: A helpful agent description
    /// tools:
    ///   - tool1
    ///   - tool2
    /// infer: true
    /// ---
    /// 
    /// # Agent Prompt
    /// 
    /// Your prompt content goes here...
    /// This is the system prompt that will be used.
    /// </code>
    /// </remarks>
    public static CustomAgentConfig FromMarkdownFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Agent markdown file not found: {filePath}", filePath);
        }

        var content = File.ReadAllText(filePath);
        return FromMarkdown(content);
    }

    /// <summary>
    /// Creates a CustomAgentConfig from a markdown file with YAML front matter asynchronously.
    /// </summary>
    /// <param name="filePath">Path to the markdown file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A configured CustomAgentConfig instance.</returns>
    public static async Task<CustomAgentConfig> FromMarkdownFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Agent markdown file not found: {filePath}", filePath);
        }

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        return FromMarkdown(content);
    }

    /// <summary>
    /// Creates a CustomAgentConfig from markdown content with YAML front matter.
    /// </summary>
    /// <param name="markdownContent">The markdown content string.</param>
    /// <returns>A configured CustomAgentConfig instance.</returns>
    public static CustomAgentConfig FromMarkdown(string markdownContent)
    {
        if (string.IsNullOrWhiteSpace(markdownContent))
        {
            throw new ArgumentException("Markdown content cannot be empty.", nameof(markdownContent));
        }

        var (frontMatter, promptContent) = ParseMarkdown(markdownContent);
        var config = ParseFrontMatter(frontMatter);

        // Use prompt content from markdown body (overrides front matter if present)
        if (!string.IsNullOrWhiteSpace(promptContent))
        {
            config.Prompt = promptContent.Trim();
        }

        // Validate required fields
        if (string.IsNullOrWhiteSpace(config.Name))
        {
            throw new InvalidOperationException("Agent configuration must have a 'name' field in front matter.");
        }

        if (string.IsNullOrWhiteSpace(config.Prompt))
        {
            throw new InvalidOperationException("Agent configuration must have prompt content in the markdown body.");
        }

        return config;
    }

    /// <summary>
    /// Parses markdown content into front matter YAML and body content.
    /// </summary>
    private static (string FrontMatter, string Body) ParseMarkdown(string content)
    {
        var lines = content.Split('\n');
        var frontMatterLines = new List<string>();
        var bodyLines = new List<string>();
        var state = ParseState.Start;

        foreach (var line in lines)
        {
            var trimmedLine = line.TrimEnd('\r');

            switch (state)
            {
                case ParseState.Start:
                    if (trimmedLine.Trim() == FrontMatterDelimiter)
                    {
                        state = ParseState.InFrontMatter;
                    }
                    else if (!string.IsNullOrWhiteSpace(trimmedLine))
                    {
                        // No front matter, entire content is the prompt
                        state = ParseState.InBody;
                        bodyLines.Add(trimmedLine);
                    }
                    break;

                case ParseState.InFrontMatter:
                    if (trimmedLine.Trim() == FrontMatterDelimiter)
                    {
                        state = ParseState.InBody;
                    }
                    else
                    {
                        frontMatterLines.Add(trimmedLine);
                    }
                    break;

                case ParseState.InBody:
                    bodyLines.Add(trimmedLine);
                    break;
            }
        }

        var frontMatter = string.Join('\n', frontMatterLines);
        var body = string.Join('\n', bodyLines);

        return (frontMatter, body);
    }

    /// <summary>
    /// Parses YAML front matter into a CustomAgentConfig.
    /// </summary>
    private static CustomAgentConfig ParseFrontMatter(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return new CustomAgentConfig();
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        try
        {
            var metadata = deserializer.Deserialize<AgentMetadata>(yaml);
            return metadata?.ToCustomAgentConfig() ?? new CustomAgentConfig();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse agent front matter: {ex.Message}", ex);
        }
    }

    private enum ParseState
    {
        Start,
        InFrontMatter,
        InBody
    }

    /// <summary>
    /// Internal class for deserializing YAML front matter.
    /// </summary>
    private class AgentMetadata
    {
        public string? Name { get; set; }
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public List<string>? Tools { get; set; }
        public string? Prompt { get; set; }
        public bool? Infer { get; set; }

        public CustomAgentConfig ToCustomAgentConfig()
        {
            return new CustomAgentConfig
            {
                Name = Name ?? string.Empty,
                DisplayName = DisplayName,
                Description = Description,
                Tools = Tools,
                Prompt = Prompt ?? string.Empty,
                Infer = Infer
            };
        }
    }
}
