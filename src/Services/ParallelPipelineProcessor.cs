using System.Diagnostics;
using GitHub.Copilot.SDK;
using PipelineConverter.Abstractions;
using PipelineConverter.Configuration;
using PipelineConverter.Extensions;
using PipelineConverter.Models;

namespace PipelineConverter.Services;

/// <summary>
/// Result of processing a single pipeline through conversion and validation.
/// </summary>
public record PipelineProcessingResult
{
    public required PipelineInfo Pipeline { get; init; }
    public required ConversionResult Conversion { get; init; }
    public AnalysisResult? Analysis { get; init; }
    public ValidationResult? Validation { get; init; }
    public string? WorkflowPath { get; init; }
    public string? AnalysisReportPath { get; init; }
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
    Analyzing,
    AnalysisComplete,
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
    private readonly CopilotAnalysisService _analysisService;
    private bool _isStarted;
    private bool _disposed;

    private ParallelPipelineProcessor(
        AppSettings settings,
        CustomAgentConfig? converterAgent,
        CustomAgentConfig? validatorAgent,
        CustomAgentConfig? analyzerAgent)
    {
        _settings = settings;
        _client = new CopilotClient();
        _sessionSemaphore = new SemaphoreSlim(settings.Copilot.MaxParallelSessions);
        _timeout = TimeSpan.FromSeconds(settings.Copilot.Timeout);
        _analysisService = new CopilotAnalysisService(_client, settings.Copilot.Model, _timeout, analyzerAgent);
        _converterService = new CopilotConverterService(_client, settings.Copilot.Model, _timeout, converterAgent);
        _validationService = new CopilotValidationService(_client, settings.Copilot.Model, _timeout, validatorAgent);
    }

    public static async Task<ParallelPipelineProcessor> CreateAsync(AppSettings settings)
    {
        var converterAgent = await LoadAgentConfigAsync(settings.Copilot.ConverterAgentFile);
        var validatorAgent = await LoadAgentConfigAsync(settings.Copilot.ValidatorAgentFile);
        var analyzerAgent = await LoadAgentConfigAsync(settings.Copilot.AnalyzerAgentFile);
        return new ParallelPipelineProcessor(settings, converterAgent, validatorAgent, analyzerAgent);
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
        bool skipAnalysis,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_isStarted)
        {
            await StartAsync(cancellationToken);
        }

        var tasks = pipelines.Select(pipeline => 
            ProcessPipelineAsync(pipeline, writer, skipValidation, skipAnalysis, progress, cancellationToken));

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
        bool skipAnalysis,
        IProgress<ProcessingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        // Wait for a session slot
        await _sessionSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Starting));

            // Phase 0: Analysis (optional, runs before conversion)
            AnalysisResult? analysisResult = null;
            string? analysisReportPath = null;

            if (!skipAnalysis && _settings.Analysis.Enabled)
            {
                progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Analyzing));

                analysisResult = await _analysisService.AnalyzeAsync(pipeline, cancellationToken);

                progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.AnalysisComplete));

                // Write analysis report
                if (_settings.Analysis.GenerateReports && analysisResult.IsSuccess)
                {
                    analysisReportPath = await writer.WriteAnalysisReportAsync(pipeline, analysisResult, cancellationToken);
                }

                // Block conversion if critical issues found
                if (_settings.Analysis.BlockOnCritical && !analysisResult.CanProceed)
                {
                    progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Failed,
                        $"Blocked by analysis: {analysisResult.ComplexityJustification}"));

                    return new PipelineProcessingResult
                    {
                        Pipeline = pipeline,
                        Analysis = analysisResult,
                        Conversion = ConversionResult.Failed($"Conversion blocked by analysis: {analysisResult.ComplexityJustification}"),
                        AnalysisReportPath = analysisReportPath,
                        Duration = stopwatch.Elapsed
                    };
                }
            }

            // Phase 1: Conversion
            progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Converting));
            
            var conversionResult = await _converterService.ConvertAsync(pipeline, analysisResult, cancellationToken);
            
            if (!conversionResult.IsSuccess)
            {
                progress?.Report(new ProcessingProgress(pipeline, ProcessingPhase.Failed, conversionResult.ErrorMessage));
                return new PipelineProcessingResult
                {
                    Pipeline = pipeline,
                    Analysis = analysisResult,
                    Conversion = conversionResult,
                    AnalysisReportPath = analysisReportPath,
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
                
                validationResult = await _validationService.ValidateAsync(
                    pipeline,
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
                Analysis = analysisResult,
                Conversion = conversionResult,
                Validation = validationResult,
                WorkflowPath = workflowPath,
                AnalysisReportPath = analysisReportPath,
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
    private static async Task<CustomAgentConfig?> LoadAgentConfigAsync(string? agentFilePath)
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

        return await CustomAgentConfigExtensions.FromMarkdownFileAsync(fullPath);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        _disposed = true;
        GC.SuppressFinalize(this);
        await _converterService.DisposeAsync();
        await _validationService.DisposeAsync();
        await _analysisService.DisposeAsync();
        if (_isStarted)
        {
            await _client.DisposeAsync();
        }
        _sessionSemaphore.Dispose();
    }
}
