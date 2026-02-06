using PipelineConverter.Abstractions;
using PipelineConverter.Models;

namespace PipelineConverter.Services;

/// <summary>
/// Service for scanning directories to find pipeline files.
/// </summary>
public sealed class PipelineScanner
{
    private readonly IReadOnlyList<IPipelineSource> _sources;
    private readonly Action<string, Exception>? _onFileSkipped;

    public PipelineScanner(IReadOnlyList<IPipelineSource> sources, Action<string, Exception>? onFileSkipped = null)
    {
        _sources = sources;
        _onFileSkipped = onFileSkipped;
    }

    /// <summary>
    /// Scans a directory asynchronously for pipeline files.
    /// </summary>
    public async Task<IReadOnlyList<PipelineInfo>> ScanAsync(
        string directory,
        PipelineType? filter = null,
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directory}");
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var discoveredPipelines = new List<PipelineInfo>();

        // Build list of file patterns to search
        var patterns = GetSearchPatterns(filter);

        foreach (var pattern in patterns)
        {
            var files = Directory.GetFiles(directory, pattern, searchOption);
            
            foreach (var file in files)
            {
                var pipeline = await TryExtractPipelineAsync(file, filter, cancellationToken);
                if (pipeline is not null)
                {
                    discoveredPipelines.Add(pipeline);
                }
            }
        }

        // Also check for Jenkinsfiles which don't have extensions
        var allFiles = Directory.GetFiles(directory, "*", searchOption);
        foreach (var file in allFiles)
        {
            var fileName = Path.GetFileName(file);
            if (fileName.StartsWith("Jenkinsfile", StringComparison.OrdinalIgnoreCase))
            {
                // Skip if already found or filtered out
                if (filter.HasValue && filter.Value != PipelineType.Jenkins)
                    continue;
                    
                if (discoveredPipelines.Any(p => p.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var pipeline = await TryExtractPipelineAsync(file, filter, cancellationToken);
                if (pipeline is not null)
                {
                    discoveredPipelines.Add(pipeline);
                }
            }
        }

        return discoveredPipelines;
    }

    private IEnumerable<string> GetSearchPatterns(PipelineType? filter)
    {
        var sources = filter.HasValue 
            ? _sources.Where(s => s.Type == filter.Value) 
            : _sources;

        return sources
            .SelectMany(s => s.FilePatterns)
            .Where(p => p.Contains('.')) // Only patterns with extensions
            .Select(p => p.StartsWith('.') ? $"*{p}" : p)
            .Distinct();
    }

    private async Task<PipelineInfo?> TryExtractPipelineAsync(string filePath, PipelineType? filter, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);

            // Find a source that can handle this file
            var sources = filter.HasValue 
                ? _sources.Where(s => s.Type == filter.Value) 
                : _sources;

            foreach (var source in sources)
            {
                if (source.CanHandle(filePath, content))
                {
                    return source.ExtractInfo(filePath, content);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            // Report skipped file if callback is provided
            _onFileSkipped?.Invoke(filePath, ex);
            return null;
        }
    }

    /// <summary>
    /// Gets a summary of what the scanner can detect.
    /// </summary>
    public IReadOnlyDictionary<PipelineType, IReadOnlyList<string>> GetSupportedPatterns()
    {
        return _sources
            .GroupBy(s => s.Type)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.SelectMany(s => s.FilePatterns).Distinct().ToList()
            );
    }
}
