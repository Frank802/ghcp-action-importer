using PipelineConverter.Abstractions;
using PipelineConverter.Models;

namespace PipelineConverter.Sources;

/// <summary>
/// Pipeline source handler for Jenkins (Jenkinsfile) files.
/// </summary>
public class JenkinsPipelineSource : IPipelineSource
{
    public PipelineType Type => PipelineType.Jenkins;

    public IReadOnlyList<string> FilePatterns => ["Jenkinsfile", "jenkinsfile", "Jenkinsfile.groovy"];

    public bool CanHandle(string filePath, string? content = null)
    {
        var fileName = Path.GetFileName(filePath);
        
        // Check filename patterns
        if (FilePatterns.Any(pattern => 
            fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("Jenkinsfile", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Check content for Groovy pipeline syntax
        if (content is not null && ContainsJenkinsKeywords(content))
        {
            return true;
        }

        return false;
    }

    public PipelineInfo ExtractInfo(string filePath, string content)
    {
        var name = ExtractPipelineName(filePath) ?? "Jenkins Pipeline";

        return new PipelineInfo
        {
            Name = name,
            SourceType = Type,
            OriginalContent = content,
            FilePath = filePath,
            Metadata = ExtractMetadata(content)
        };
    }

    private static bool ContainsJenkinsKeywords(string content)
    {
        // Jenkins Declarative Pipeline keywords
        return content.Contains("pipeline {") || 
               content.Contains("pipeline{") ||
               (content.Contains("node {") && content.Contains("stage("));
    }

    private static string? ExtractPipelineName(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName.Equals("Jenkinsfile", StringComparison.OrdinalIgnoreCase))
        {
            // Use parent directory name as pipeline name
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                return new DirectoryInfo(directory).Name + " Pipeline";
            }
        }
        return fileName;
    }

    private static Dictionary<string, string> ExtractMetadata(string content)
    {
        var metadata = new Dictionary<string, string>
        {
            ["source_type"] = "jenkins"
        };

        // Detect pipeline style
        if (content.Contains("pipeline {") || content.Contains("pipeline{"))
            metadata["pipeline_style"] = "declarative";
        else if (content.Contains("node {") || content.Contains("node("))
            metadata["pipeline_style"] = "scripted";

        // Detect common Jenkins features
        if (content.Contains("agent "))
            metadata["has_agent"] = "true";
        if (content.Contains("stages {"))
            metadata["has_stages"] = "true";
        if (content.Contains("environment {"))
            metadata["has_environment"] = "true";
        if (content.Contains("parameters {"))
            metadata["has_parameters"] = "true";
        if (content.Contains("post {"))
            metadata["has_post"] = "true";
        if (content.Contains("when {"))
            metadata["has_when"] = "true";
        if (content.Contains("parallel"))
            metadata["has_parallel"] = "true";

        return metadata;
    }
}
