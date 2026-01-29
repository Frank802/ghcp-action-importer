using PipelineConverter.Abstractions;
using PipelineConverter.Models;

namespace PipelineConverter.Sources;

/// <summary>
/// Pipeline source handler for GitLab CI/CD (.gitlab-ci.yml) files.
/// </summary>
public class GitLabPipelineSource : IPipelineSource
{
    public PipelineType Type => PipelineType.GitLab;

    public IReadOnlyList<string> FilePatterns => [".gitlab-ci.yml", ".gitlab-ci.yaml"];

    public bool CanHandle(string filePath, string? content = null)
    {
        var fileName = Path.GetFileName(filePath);
        return FilePatterns.Any(pattern => 
            fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase));
    }

    public PipelineInfo ExtractInfo(string filePath, string content)
    {
        // Extract pipeline name from content or use filename
        var name = ExtractPipelineName(content) ?? Path.GetFileNameWithoutExtension(filePath);

        return new PipelineInfo
        {
            Name = name,
            SourceType = Type,
            OriginalContent = content,
            FilePath = filePath,
            Metadata = ExtractMetadata(content)
        };
    }

    private static string? ExtractPipelineName(string content)
    {
        // GitLab CI doesn't have a top-level name, but we can look for workflow name
        // or use the first stage name as a hint
        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("workflow:"))
            {
                return "GitLab CI Workflow";
            }
        }
        return null;
    }

    private static Dictionary<string, string> ExtractMetadata(string content)
    {
        var metadata = new Dictionary<string, string>
        {
            ["source_type"] = "gitlab"
        };

        // Simple detection of common GitLab CI features
        if (content.Contains("stages:"))
            metadata["has_stages"] = "true";
        if (content.Contains("include:"))
            metadata["has_includes"] = "true";
        if (content.Contains("variables:"))
            metadata["has_variables"] = "true";
        if (content.Contains("cache:"))
            metadata["has_cache"] = "true";
        if (content.Contains("artifacts:"))
            metadata["has_artifacts"] = "true";

        return metadata;
    }
}
