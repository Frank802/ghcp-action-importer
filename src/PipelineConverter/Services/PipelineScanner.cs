using PipelineConverter.Abstractions;
using PipelineConverter.Models;

namespace PipelineConverter.Services;

/// <summary>
/// Service for scanning directories to find pipeline files.
/// </summary>
public class PipelineScanner
{
    private readonly IReadOnlyList<IPipelineSource> _sources;

    public PipelineScanner(IEnumerable<IPipelineSource> sources)
    {
        _sources = sources.ToList();
    }

    /// <summary>
    /// Scans a directory for pipeline files and extracts their information.
    /// </summary>
    /// <param name="directory">The directory to scan.</param>
    /// <param name="filter">Optional filter to only scan for specific pipeline types.</param>
    /// <param name="recursive">Whether to scan subdirectories.</param>
    /// <returns>Collection of discovered pipeline information.</returns>
    public IEnumerable<PipelineInfo> Scan(
        string directory, 
        PipelineType? filter = null,
        bool recursive = true)
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
                var pipeline = TryExtractPipeline(file, filter);
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

                var pipeline = TryExtractPipeline(file, filter);
                if (pipeline is not null)
                {
                    discoveredPipelines.Add(pipeline);
                }
            }
        }

        return discoveredPipelines;
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
        return await Task.Run(() => Scan(directory, filter, recursive).ToList(), cancellationToken);
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

    private PipelineInfo? TryExtractPipeline(string filePath, PipelineType? filter)
    {
        try
        {
            var content = File.ReadAllText(filePath);

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
        catch (Exception)
        {
            // Skip files that can't be read
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
