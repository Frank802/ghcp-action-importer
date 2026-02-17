using System.Collections.Concurrent;
using PipelineConverter.Models;

namespace PipelineConverter.Services;

/// <summary>
/// Tracks the status of each pipeline being processed.
/// Used as a bridge between the processing pipeline and the Blazor UI.
/// </summary>
public sealed class PipelineStatusEntry
{
    public required PipelineInfo Pipeline { get; init; }
    public ProcessingPhase Phase { get; set; } = ProcessingPhase.Starting;
    public string? Message { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public TimeSpan Elapsed => (CompletedAt ?? DateTime.UtcNow) - StartedAt;
}

/// <summary>
/// Thread-safe singleton service that bridges IProgress&lt;ProcessingProgress&gt;
/// updates from the pipeline processor to the Blazor Server dashboard.
/// </summary>
public sealed class PipelineProgressService
{
    private readonly ConcurrentDictionary<string, PipelineStatusEntry> _entries = new();

    /// <summary>
    /// Raised whenever pipeline status changes. Blazor components subscribe to refresh UI.
    /// </summary>
    public event Action? OnChange;

    /// <summary>
    /// When processing started (first call to SetPipelines).
    /// </summary>
    public DateTime? ProcessingStartedAt { get; private set; }

    /// <summary>
    /// When processing finished (all pipelines completed or failed).
    /// </summary>
    public DateTime? ProcessingCompletedAt { get; private set; }

    public int TotalCount => _entries.Count;

    public int CompletedCount => _entries.Values
        .Count(e => e.Phase is ProcessingPhase.Complete);

    public int FailedCount => _entries.Values
        .Count(e => e.Phase is ProcessingPhase.Failed);

    public bool IsComplete => TotalCount > 0
        && _entries.Values.All(e => e.Phase is ProcessingPhase.Complete or ProcessingPhase.Failed);

    public TimeSpan TotalElapsed => ProcessingStartedAt.HasValue
        ? (ProcessingCompletedAt ?? DateTime.UtcNow) - ProcessingStartedAt.Value
        : TimeSpan.Zero;

    /// <summary>
    /// Returns a snapshot of all entries in their current state.
    /// </summary>
    public IReadOnlyList<PipelineStatusEntry> GetAll() => _entries.Values.ToList();

    /// <summary>
    /// Initializes entries for all pipelines about to be processed.
    /// </summary>
    public void SetPipelines(IReadOnlyList<PipelineInfo> pipelines)
    {
        _entries.Clear();
        ProcessingStartedAt = DateTime.UtcNow;
        ProcessingCompletedAt = null;

        foreach (var p in pipelines)
        {
            _entries[p.FilePath] = new PipelineStatusEntry
            {
                Pipeline = p,
                Phase = ProcessingPhase.Starting,
                StartedAt = DateTime.UtcNow
            };
        }

        OnChange?.Invoke();
    }

    /// <summary>
    /// Updates a pipeline's status from a ProcessingProgress event.
    /// </summary>
    public void Update(ProcessingProgress progress)
    {
        var key = progress.Pipeline.FilePath;

        if (_entries.TryGetValue(key, out var entry))
        {
            entry.Phase = progress.Phase;
            entry.Message = progress.Message;

            if (progress.Phase is ProcessingPhase.Complete or ProcessingPhase.Failed)
            {
                entry.CompletedAt = DateTime.UtcNow;
            }
        }
        else
        {
            _entries[key] = new PipelineStatusEntry
            {
                Pipeline = progress.Pipeline,
                Phase = progress.Phase,
                Message = progress.Message,
                StartedAt = DateTime.UtcNow,
                CompletedAt = progress.Phase is ProcessingPhase.Complete or ProcessingPhase.Failed
                    ? DateTime.UtcNow
                    : null
            };
        }

        // Mark overall processing complete when all pipelines are done
        if (IsComplete && ProcessingCompletedAt is null)
        {
            ProcessingCompletedAt = DateTime.UtcNow;
        }

        OnChange?.Invoke();
    }
}
