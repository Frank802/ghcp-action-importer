using PipelineConverter.Abstractions;
using PipelineConverter.Models;

namespace PipelineConverter.Sources;

/// <summary>
/// Pipeline source handler for Azure DevOps (azure-pipelines.yml) files.
/// </summary>
public sealed class AzureDevOpsPipelineSource : IPipelineSource
{
    public PipelineType Type => PipelineType.AzureDevOps;

    public IReadOnlyList<string> FilePatterns => 
    [
        "azure-pipelines.yml",
        "azure-pipelines.yaml",
        "azure-pipeline.yml",
        "azure-pipeline.yaml"
    ];

    public bool CanHandle(string filePath, string? content = null)
    {
        var fileName = Path.GetFileName(filePath);
        
        // Check filename patterns
        if (FilePatterns.Any(pattern => 
            fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Also check for YAML files that contain ADO-specific keywords
        if (content is not null && 
            (fileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || 
             fileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
        {
            return ContainsAdoKeywords(content);
        }

        return false;
    }

    public PipelineInfo ExtractInfo(string filePath, string content)
    {
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

    private static bool ContainsAdoKeywords(string content)
    {
        // ADO-specific keywords that distinguish it from other YAML files
        return content.Contains("trigger:") && 
               (content.Contains("pool:") || content.Contains("vmImage:") || content.Contains("stages:"));
    }

    private static string? ExtractPipelineName(string content)
    {
        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("name:"))
            {
                return trimmed["name:".Length..].Trim().Trim('\'', '"');
            }
        }
        return null;
    }

    private static Dictionary<string, string> ExtractMetadata(string content)
    {
        var metadata = new Dictionary<string, string>
        {
            ["source_type"] = "azure-devops"
        };

        // Detect common ADO features
        if (content.Contains("stages:"))
            metadata["has_stages"] = "true";
        if (content.Contains("jobs:"))
            metadata["has_jobs"] = "true";
        if (content.Contains("pool:"))
            metadata["has_pool"] = "true";
        if (content.Contains("variables:"))
            metadata["has_variables"] = "true";
        if (content.Contains("resources:"))
            metadata["has_resources"] = "true";
        if (content.Contains("template:") || content.Contains("templates:"))
            metadata["has_templates"] = "true";
        if (content.Contains("task:"))
            metadata["has_tasks"] = "true";

        return metadata;
    }
}
