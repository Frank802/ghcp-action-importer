using GitHub.Copilot.SDK;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PipelineConverter.Extensions;

public static class CustomAgentConfigExtensions
{
    private const string FrontMatterDelimiter = "---";

    public static async Task<CustomAgentConfig> FromMarkdownFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var (frontMatter, body) = ParseMarkdown(content);
        var config = ParseFrontMatter(frontMatter);

        if (!string.IsNullOrWhiteSpace(body))
            config.Prompt = body.Trim();

        return config;
    }

    private static (string FrontMatter, string Body) ParseMarkdown(string content)
    {
        var lines = content.Split('\n');
        var frontMatterLines = new List<string>();
        var bodyLines = new List<string>();
        var inFrontMatter = false;
        var frontMatterClosed = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');

            if (!frontMatterClosed && trimmed.Trim() == FrontMatterDelimiter)
            {
                if (!inFrontMatter) { inFrontMatter = true; continue; }
                frontMatterClosed = true;
                continue;
            }

            if (inFrontMatter && !frontMatterClosed)
                frontMatterLines.Add(trimmed);
            else if (frontMatterClosed || !inFrontMatter)
                bodyLines.Add(trimmed);
        }

        return (string.Join('\n', frontMatterLines), string.Join('\n', bodyLines));
    }

    private static CustomAgentConfig ParseFrontMatter(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return new CustomAgentConfig();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var metadata = deserializer.Deserialize<AgentMetadata>(yaml);
        return metadata?.ToCustomAgentConfig() ?? new CustomAgentConfig();
    }

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
