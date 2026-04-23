using GitHub.Copilot.SDK;
using PipelineConverter.Abstractions;
using PipelineConverter.Models;

namespace PipelineConverter.Services;

/// <summary>
/// Service that uses GitHub Copilot SDK to analyze pipelines before conversion.
/// Produces a structured report with complexity scoring, risks, and structure breakdown.
/// </summary>
public sealed class CopilotAnalysisService
{
    private readonly TimeSpan _timeout;
    private readonly string _agentName;

    public CopilotAnalysisService(TimeSpan timeout, string agentName)
    {
        _timeout = timeout;
        _agentName = agentName;
    }

    /// <summary>
    /// Analyzes a pipeline using the provided session.
    /// </summary>
    public async Task<AnalysisResult> AnalyzeAsync(
        CopilotSession session,
        PipelineInfo pipeline,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await session.Rpc.Agent.SelectAsync(_agentName);
            var prompt = BuildAnalysisPrompt(pipeline);
            var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = prompt }, _timeout);
            string responseContent = (string)(response?.Data?.Content ?? "");

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                return AnalysisResult.Failed("Empty response from Copilot during analysis.");
            }

            return ParseAnalysisResponse(responseContent);
        }
        catch (Exception ex)
        {
            return AnalysisResult.Failed($"Analysis failed: {ex.Message}");
        }
    }

    private string BuildAnalysisPrompt(PipelineInfo pipeline)
    {
        var sourceType = pipeline.SourceType switch
        {
            PipelineType.GitLab => "GitLab CI/CD (.gitlab-ci.yml)",
            PipelineType.AzureDevOps => "Azure DevOps (azure-pipelines.yml)",
            PipelineType.Jenkins => "Jenkins (Jenkinsfile)",
            _ => "CI/CD pipeline"
        };

        return $"""
            Analyze the following {sourceType} pipeline and produce a pre-conversion report.
            Assess its complexity for conversion to GitHub Actions, identify risks, unsupported features,
            and break down its structure.

            Pipeline Name: {pipeline.Name}
            Pipeline Source: {sourceType}

            Pipeline Content:
            ```
            {pipeline.OriginalContent}
            ```

            You MUST respond using EXACTLY this structured format with the section markers as specified in your instructions.
            Do not deviate from the specified markers and do not skip any sections.
            """;
    }

    private static AnalysisResult ParseAnalysisResponse(string response)
    {
        var summary = ParseSection(response, "SUMMARY");
        var complexity = ParseComplexity(response);
        var complexityJustification = ParseSection(response, "COMPLEXITY_JUSTIFICATION");
        var structureBreakdown = ParseListSection(response, "STRUCTURE");
        var riskItems = ParseRisks(response);
        var unsupportedFeatures = ParseListSection(response, "UNSUPPORTED_FEATURES");
        var estimatedEffort = ParseSection(response, "ESTIMATED_EFFORT");
        var isCriticalBlock = ParseCriticalBlock(response);

        if (isCriticalBlock.blocked)
        {
            return AnalysisResult.Blocked(
                isCriticalBlock.reason ?? "Pipeline flagged as unconvertible.",
                riskItems,
                unsupportedFeatures,
                response);
        }

        return AnalysisResult.Success(
            complexity,
            complexityJustification,
            structureBreakdown,
            riskItems,
            unsupportedFeatures,
            estimatedEffort,
            summary,
            response);
    }

    private static ComplexityLevel ParseComplexity(string response)
    {
        var section = ParseSection(response, "COMPLEXITY");
        if (string.IsNullOrWhiteSpace(section))
            return ComplexityLevel.Medium;

        var trimmed = section.Trim();
        if (trimmed.Contains("Critical", StringComparison.OrdinalIgnoreCase))
            return ComplexityLevel.Critical;
        if (trimmed.Contains("High", StringComparison.OrdinalIgnoreCase))
            return ComplexityLevel.High;
        if (trimmed.Contains("Low", StringComparison.OrdinalIgnoreCase))
            return ComplexityLevel.Low;
        return ComplexityLevel.Medium;
    }

    private static string? ParseSection(string response, string sectionName)
    {
        var marker = $"### {sectionName}";
        var startIndex = response.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (startIndex == -1) return null;

        var contentStart = response.IndexOf('\n', startIndex);
        if (contentStart == -1) return null;
        contentStart++; // skip the newline

        // Find the next section marker
        var nextMarker = response.IndexOf("\n### ", contentStart, StringComparison.OrdinalIgnoreCase);
        var content = nextMarker == -1
            ? response[contentStart..]
            : response[contentStart..nextMarker];

        var trimmed = content.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static List<string>? ParseListSection(string response, string sectionName)
    {
        var content = ParseSection(response, sectionName);
        if (string.IsNullOrWhiteSpace(content) ||
            content.Equals("None", StringComparison.OrdinalIgnoreCase))
            return null;

        var items = content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimStart('-', '*', ' '))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        return items.Count > 0 ? items : null;
    }

    private static List<AnalysisRisk>? ParseRisks(string response)
    {
        var content = ParseSection(response, "RISKS");
        if (string.IsNullOrWhiteSpace(content) ||
            content.Equals("None", StringComparison.OrdinalIgnoreCase))
            return null;

        var risks = new List<AnalysisRisk>();

        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimStart('-', '*', ' ');
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Expected format: [ERROR|WARNING|INFO] | Category | Description | Mitigation
            var parts = trimmed.Split('|', StringSplitOptions.TrimEntries);

            if (parts.Length >= 3)
            {
                var severity = parts[0] switch
                {
                    var s when s.Contains("ERROR", StringComparison.OrdinalIgnoreCase) => ValidationSeverity.Error,
                    var s when s.Contains("WARNING", StringComparison.OrdinalIgnoreCase) => ValidationSeverity.Warning,
                    _ => ValidationSeverity.Info
                };

                risks.Add(new AnalysisRisk
                {
                    Severity = severity,
                    Category = parts[1],
                    Description = parts[2],
                    Mitigation = parts.Length >= 4 ? parts[3] : null
                });
            }
            else
            {
                // Fallback: treat the whole line as an info risk
                risks.Add(new AnalysisRisk
                {
                    Severity = ValidationSeverity.Info,
                    Category = "General",
                    Description = trimmed
                });
            }
        }

        return risks.Count > 0 ? risks : null;
    }

    private static (bool blocked, string? reason) ParseCriticalBlock(string response)
    {
        var content = ParseSection(response, "CRITICAL_BLOCK");
        if (string.IsNullOrWhiteSpace(content))
            return (false, null);

        var trimmed = content.Trim();
        if (trimmed.StartsWith("YES", StringComparison.OrdinalIgnoreCase))
        {
            var dashIndex = trimmed.IndexOf('-');
            var reason = dashIndex >= 0 && dashIndex + 1 < trimmed.Length
                ? trimmed[(dashIndex + 1)..].Trim()
                : "Pipeline flagged as unconvertible.";
            return (true, reason);
        }

        return (false, null);
    }
}
