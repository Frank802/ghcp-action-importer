using System.Diagnostics;
using GitHub.Copilot.SDK;
using PipelineConverter.Abstractions;
using PipelineConverter.Configuration;
using PipelineConverter.Extensions;
using PipelineConverter.Models;
using PipelineConverter.Utilities;

namespace PipelineConverter.Services;

/// <summary>
/// Result of processing a single pipeline through conversion and validation.
/// </summary>
public record PipelineProcessingResult
{
    public required PipelineInfo Pipeline { get; init; }
    public required ConversionResult Conversion { get; init; }
    public ValidationResult? Validation { get; init; }
    public string? WorkflowPath { get; init; }
    public string? ValidationReportPath { get; init; }
    public TimeSpan Duration { get; init; }
    public Exception? Error { get; init; }
}

/// <summary>
/// Progress callback for pipeline processing.
/// </summary>
public enum ProcessingPhase
{
    Starting,
    Converting,
    ConversionComplete,
    Validating,
    ValidationComplete,
    Writing,
    Complete,
    Failed
}

public record ProcessingProgress(
    PipelineInfo Pipeline,
    ProcessingPhase Phase,
    string? Message = null);

/// <summary>
/// Parallel pipeline processor using multiple Copilot sessions.
/// Each pipeline gets its own session for conversion and validation.
/// </summary>
public sealed class ParallelPipelineProcessor : IAsyncDisposable
{
    private readonly CopilotClient _client;
    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _sessionSemaphore;
    private readonly TimeSpan _timeout;
    private readonly CopilotConverterService _converterService;
    private readonly CopilotValidationService _validationService;
    private bool _isStarted;
    private bool _disposed;

    public ParallelPipelineProcessor(AppSettings settings)
    {
        _settings = settings;
        _client = new CopilotClient();
        _sessionSemaphore = new SemaphoreSlim(settings.Copilot.MaxParallelSessions);
        _timeout = TimeSpan.FromSeconds(settings.Copilot.Timeout);

        // Load custom agents from markdown files
        var converterAgent = LoadAgentConfig(settings.Copilot.ConverterAgentFile);
        var validatorAgent = LoadAgentConfig(settings.Copilot.ValidatorAgentFile);

        _converterService = new CopilotConverterService(_timeout, converterAgent);
        _validationService = new CopilotValidationService(_timeout, validatorAgent);
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
    /// Processes multiple pipelines in parallel with configurable concurrency.
    /// </summary>
    public async Task<List<PipelineProcessingResult>> ProcessAsync(
        IReadOnlyList<PipelineInfo> pipelines,
        WorkflowWriter writer,
        bool skipValidation,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_isStarted)
        {
            await StartAsync(cancellationToken);
        }

        var tasks = pipelines.Select(pipeline => 
            ProcessPipelineAsync(pipeline, writer, skipValidation, progress, cancellationToken));

        // Add aggregate timeout: per-pipeline timeout * max parallel sessions * 2 (safety buffer)
        var aggregateTimeout = TimeSpan.FromSeconds(_timeout.TotalSeconds * _settings.Copilot.MaxParallelSessions * 2);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(aggregateTimeout);

        try
        {
            var results = await Task.WhenAll(tasks).WaitAsync(timeoutCts.Token);
            return results.ToList();
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Pipeline processing exceeded aggregate timeout of {aggregateTimeout.TotalSeconds:F0} seconds.");
        }
    }

    /// <summary>
    /// Processes a single pipeline in its own session.
    /// </summary>
    private async Task<PipelineProcessingResult> ProcessPipelineAsync(
        PipelineInfo pipeline,
        WorkflowWriter writer,
        bool skipValidation,
        IProgress<ProcessingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        // Wait for a session slot
        await _sessionSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Starting));

            // Create a session for this pipeline with a unique ID
            // Sanitize name to only contain alphanumeric and hyphens (no dots, spaces, etc.)
            var sanitizedName = SessionIdSanitizer.SanitizeSessionId(pipeline.Name);
            var sessionConfig = new SessionConfig
            {
                SessionId = $"pipeline-{sanitizedName}-{Guid.NewGuid():N}",
                Model = _settings.Copilot.Model,
                CustomAgents = _converterService.CustomAgent is not null
                    ? [_converterService.CustomAgent]
                    : null
            };

            await using var session = await _client.CreateSessionAsync(sessionConfig, cancellationToken);

            // Phase 1: Conversion
            progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Converting));
            
            var conversionResult = await _converterService.ConvertInSessionAsync(session, pipeline, cancellationToken);
            
            if (!conversionResult.IsSuccess)
            {
                progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Failed, conversionResult.ErrorMessage));
                return new PipelineProcessingResult
                {
                    Pipeline = pipeline,
                    Conversion = conversionResult,
                    Duration = stopwatch.Elapsed
                };
            }

            progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.ConversionComplete));

            // Write the workflow file
            progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Writing));
            var workflowPath = await writer.WriteAsync(conversionResult, pipeline, cancellationToken);

            ValidationResult? validationResult = null;
            string? validationReportPath = null;

            // Phase 2: Validation (in same session, maintains context)
            if (!skipValidation)
            {
                progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Validating));
                
                validationResult = await _validationService.ValidateInSessionAsync(
                    session, 
                    pipeline.OriginalContent, 
                    conversionResult.WorkflowYaml!, 
                    cancellationToken);

                progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.ValidationComplete));

                // Overwrite the converted workflow with the improved version if available
                if (!string.IsNullOrWhiteSpace(validationResult.ImprovedWorkflow))
                {
                    await writer.OverwriteWithImprovedAsync(
                        workflowPath, validationResult.ImprovedWorkflow, cancellationToken);
                }

                // Write validation report
                if (_settings.Conversion.GenerateValidationReports)
                {
                    validationReportPath = await writer.WriteValidationReportAsync(
                        workflowPath, validationResult, cancellationToken);
                }
            }

            progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Complete));

            return new PipelineProcessingResult
            {
                Pipeline = pipeline,
                Conversion = conversionResult,
                Validation = validationResult,
                WorkflowPath = workflowPath,
                ValidationReportPath = validationReportPath,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Failed, ex.Message));
            
            return new PipelineProcessingResult
            {
                Pipeline = pipeline,
                Conversion = ConversionResult.Failed($"Processing failed: {ex.Message}"),
                Duration = stopwatch.Elapsed,
                Error = ex
            };
        }
        finally
        {
            if (!_disposed)
            {
                _sessionSemaphore.Release();
            }
        }
    }

    /// <summary>
    /// Loads a CustomAgentConfig from a markdown file, returning null if the file doesn't exist.
    /// Validates that the resolved path is under the application base directory.
    /// </summary>
    private static CustomAgentConfig? LoadAgentConfig(string? agentFilePath)
    {
        if (string.IsNullOrWhiteSpace(agentFilePath))
            return null;

        // Resolve relative to application base directory
        var fullPath = Path.IsPathRooted(agentFilePath)
            ? agentFilePath
            : Path.Combine(AppContext.BaseDirectory, agentFilePath);

        // Normalize paths and validate that fullPath is under AppContext.BaseDirectory
        var normalizedBasePath = Path.GetFullPath(AppContext.BaseDirectory);
        var normalizedFullPath = Path.GetFullPath(fullPath);
        
        if (!normalizedFullPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Agent file path '{agentFilePath}' resolves outside the application directory.");
        }

        if (!File.Exists(fullPath))
            return null;

        return CustomAgentConfigExtensions.FromMarkdownFile(fullPath);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        _disposed = true;
        GC.SuppressFinalize(this);
        await _converterService.DisposeAsync();
        await _validationService.DisposeAsync();
        if (_isStarted)
        {
            await _client.DisposeAsync();
        }
        _sessionSemaphore.Dispose();
    }
}
